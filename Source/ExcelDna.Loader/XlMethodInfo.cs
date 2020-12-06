//  Copyright (c) Govert van Drimmelen. All rights reserved.
//  Excel-DNA is licensed under the zlib license. See LICENSE.txt for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using ExcelDna.Loader.Logging;

namespace ExcelDna.Loader
{
    internal class XlMethodInfo
    {
        public static int Index = 0;

        public MethodInfo MethodInfo;
        public object Target;   // Mostly null - can / should we get rid of it? It only lets us use constant objects to invoke against, which is not so useful. Rather allow open delegates?

        // Set and used during contruction and registration
        public GCHandle DelegateHandle; // TODO: What with this - when to clean up? 
        // For cleanup should call DelegateHandle.Free()
        public IntPtr FunctionPointer;

        // Info for Excel Registration
        public bool IsCommand;
        public string Name; // Name of UDF/Macro in Excel
        public string Description;
        public bool IsHidden; // For Functions only
        public string ShortCut; // For macros only
        public string MenuName; // For macros only
        public string MenuText; // For macros only
        public string Category;
        public bool IsVolatile;
        public bool IsExceptionSafe;
        public bool IsMacroType;
        public bool IsThreadSafe; // For Functions only
        public bool IsClusterSafe; // For Functions only
        public string HelpTopic;
        public bool ExplicitRegistration;
        public bool SuppressOverwriteError;
        public double RegisterId;

        public XlParameterInfo[] Parameters;
        public XlParameterInfo ReturnType; // Macro will have ReturnType null (as will native async functions)


        // THROWS: Throws a DnaMarshalException if the method cannot be turned into an XlMethodInfo
        // TODO: Manage errors if things go wrong
        XlMethodInfo(MethodInfo targetMethod, object target, object methodAttribute, List<object> argumentAttributes)
        {
            MethodInfo = targetMethod;
            Target = target;

            // Default Name, Description and Category
            Name = targetMethod.Name;
            Description = "";
            Category = IntegrationHelpers.DnaLibraryGetName();
            HelpTopic = "";
            IsVolatile = false;
            IsExceptionSafe = false;
            IsHidden = false;
            IsMacroType = false;
            IsThreadSafe = false;
            IsClusterSafe = false;
            ExplicitRegistration = false;

            ShortCut = "";
            // DOCUMENT: Default MenuName is the library name
            // but menu is only added if at least the MenuText is set.
            MenuName = IntegrationHelpers.DnaLibraryGetName();
            MenuText = null; // Menu is only 

            // Set default IsCommand - overridden by having an [ExcelCommand] attribute,
            // or by being a native async function.
            // (Must be done before SetAttributeInfo)
            IsCommand = (targetMethod.ReturnType == typeof(void));

            SetAttributeInfo(methodAttribute);
            // We shortcut the rest of the registration
            if (ExplicitRegistration) return;

            FixHelpTopic();

            // Return type conversion
            // Careful here - native async functions also return void
            if (targetMethod.ReturnType == typeof(void))
            {
                ReturnType = null;
            }
            else
            {
                ReturnType = new XlParameterInfo(targetMethod.ReturnType, true, IsExceptionSafe);
            }

            ParameterInfo[] parameters = targetMethod.GetParameters();
            
            // Parameters - meta-data and type conversion
            Parameters = new XlParameterInfo[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                object argAttrib = null;
                if ( argumentAttributes != null && i < argumentAttributes.Count)
                    argAttrib = argumentAttributes[i];
                 Parameters[i] = new XlParameterInfo(parameters[i], argAttrib);
            }

            // A native async function might still be marked as a command - check and fix.
            // (these have the ExcelAsyncHandle as last parameter)
            // (This check needs the Parameters array to be set up already.)
            if (IsExcelAsyncFunction)
            {
                // It really is a function, though it might return null
                IsCommand = false;
            }
        }

        // Native async functions have a final parameter that is an ExcelAsyncHandle.
        public bool IsExcelAsyncFunction 
        { 
            get 
            { 
                return Parameters.Length > 0 && Parameters[Parameters.Length - 1].IsExcelAsyncHandle; 
            } 
        }

        // Basic setup - get description, category etc.
        void SetAttributeInfo(object attrib)
        {
            if (attrib == null) return;

            // DOCUMENT: Description in ExcelFunctionAtribute overrides DescriptionAttribute
            // DOCUMENT: Default Category is Current Library Name.
            // Get System.ComponentModel.DescriptionAttribute
            // Search through attribs for Description
            System.ComponentModel.DescriptionAttribute desc =
                attrib as System.ComponentModel.DescriptionAttribute;
            if (desc != null)
            {
                Description = desc.Description;
                return;
            }

            // There was a problem with the type identification when checking the 
            // attribute types, for the second instance of the .xll 
            // that is loaded.
            // So I check on the names and access through reflection.
            // CONSIDER: Fix again? It should rather be 
            //ExcelFunctionAttribute xlfunc = attrib as ExcelFunctionAttribute;
            //if (xlfunc != null)
            //{
            //    if (xlfunc.Name != null)
            //    {
            //        Name = xlfunc.Name;
            //    }
            //    if (xlfunc.Description != null)
            //    {
            //        Description = xlfunc.Description;
            //    }
            //    if (xlfunc.Category != null)
            //    {
            //        Category = xlfunc.Category;
            //    }
            //    if (xlfunc.HelpTopic != null)
            //    {
            //        HelpTopic = xlfunc.HelpTopic;
            //    }
            //    IsVolatile = xlfunc.IsVolatile;
            //    IsExceptionSafe = xlfunc.IsExceptionSafe;
            //    IsMacroType = xlfunc.IsMacroType;
            //}
            //ExcelCommandAttribute xlcmd = attrib as ExcelCommandAttribute;
            //if (xlcmd != null)
            //{
            //    if (xlcmd.Name != null)
            //    {
            //        Name = xlcmd.Name;
            //    }
            //    if (xlcmd.Description != null)
            //    {
            //        Description = xlcmd.Description;
            //    }
            //    if (xlcmd.HelpTopic != null)
            //    {
            //        HelpTopic = xlcmd.HelpTopic;
            //    }
            //    if (xlcmd.ShortCut != null)
            //    {
            //        ShortCut = xlcmd.ShortCut;
            //    }
            //    if (xlcmd.MenuName != null)
            //    {
            //        MenuName = xlcmd.MenuName;
            //    }
            //    if (xlcmd.MenuText != null)
            //    {
            //        MenuText = xlcmd.MenuText;
            //    }
            //    IsExceptionSafe = xlcmd.IsExceptionSafe;
            //    IsCommand = true;
            //}

            Type attribType = attrib.GetType();
                
            if (TypeHelper.TypeHasAncestorWithFullName(attribType, "ExcelDna.Integration.ExcelFunctionAttribute"))
            {
                string name = (string) attribType.GetField("Name").GetValue(attrib);
                string description = (string) attribType.GetField("Description").GetValue(attrib);
                string category = (string) attribType.GetField("Category").GetValue(attrib);
                string helpTopic = (string) attribType.GetField("HelpTopic").GetValue(attrib);
                bool isVolatile = (bool) attribType.GetField("IsVolatile").GetValue(attrib);
                bool isExceptionSafe = (bool) attribType.GetField("IsExceptionSafe").GetValue(attrib);
                bool isMacroType = (bool) attribType.GetField("IsMacroType").GetValue(attrib);
                bool isHidden = (bool) attribType.GetField("IsHidden").GetValue(attrib);
                bool isThreadSafe = (bool) attribType.GetField("IsThreadSafe").GetValue(attrib);
                bool isClusterSafe = (bool)attribType.GetField("IsClusterSafe").GetValue(attrib);
                bool explicitRegistration = (bool)attribType.GetField("ExplicitRegistration").GetValue(attrib);
                bool suppressOverwriteError = (bool)attribType.GetField("SuppressOverwriteError").GetValue(attrib);
                if (name != null)
                {
                    Name = name;
                }
                if (description != null)
                {
                    Description = description;
                }
                if (category != null)
                {
                    Category = category;
                }
                if (helpTopic != null)
                {
                    HelpTopic = helpTopic;
                }
                IsVolatile = isVolatile;
                IsExceptionSafe = isExceptionSafe;
                IsMacroType = isMacroType;
                IsHidden = isHidden;
                IsThreadSafe = (!isMacroType && isThreadSafe);
                // DOCUMENT: IsClusterSafe function MUST NOT be marked as IsMacroType=true and MAY be marked as IsThreadSafe = true.
                //           [xlfRegister (Form 1) page in the Microsoft Excel 2010 XLL SDK Documentation]
                IsClusterSafe = (!isMacroType && isClusterSafe);
                ExplicitRegistration = explicitRegistration;
                SuppressOverwriteError = suppressOverwriteError;
                IsCommand = false;
            }
            else if (TypeHelper.TypeHasAncestorWithFullName(attribType, "ExcelDna.Integration.ExcelCommandAttribute"))
            {
                string name = (string) attribType.GetField("Name").GetValue(attrib);
                string description = (string) attribType.GetField("Description").GetValue(attrib);
                string helpTopic = (string) attribType.GetField("HelpTopic").GetValue(attrib);
                string shortCut = (string) attribType.GetField("ShortCut").GetValue(attrib);
                string menuName = (string) attribType.GetField("MenuName").GetValue(attrib);
                string menuText = (string) attribType.GetField("MenuText").GetValue(attrib);
//                    bool isHidden = (bool)attribType.GetField("IsHidden").GetValue(attrib);
                bool isExceptionSafe = (bool) attribType.GetField("IsExceptionSafe").GetValue(attrib);
                bool explicitRegistration = (bool)attribType.GetField("ExplicitRegistration").GetValue(attrib);
                bool suppressOverwriteError = (bool)attribType.GetField("SuppressOverwriteError").GetValue(attrib);

                if (name != null)
                {
                    Name = name;
                }
                if (description != null)
                {
                    Description = description;
                }
                if (helpTopic != null)
                {
                    HelpTopic = helpTopic;
                }
                if (shortCut != null)
                {
                    ShortCut = shortCut;
                }
                if (menuName != null)
                {
                    MenuName = menuName;
                }
                if (menuText != null)
                {
                    MenuText = menuText;
                }
//                    IsHidden = isHidden;  // Only for functions.
                IsExceptionSafe = isExceptionSafe;
                ExplicitRegistration = explicitRegistration;
                SuppressOverwriteError = suppressOverwriteError;

                // Override IsCommand, even though this 'macro' might have a return value.
                // Allow for more flexibility in what kind of macros are supported, particularly for calling
                // via Application.Run.
                IsCommand = true;   
            }
        }

        void FixHelpTopic()
        {
            // Make HelpTopic without full path relative to xllPath
            if (string.IsNullOrEmpty(HelpTopic))
            {
                return;
            }
           // DOCUMENT: If HelpTopic is not rooted - it is expanded relative to .xll path.
            // If http url does not end with !0 it is appended.
            // I don't think https is supported, but it should not be considered an 'unrooted' path anyway.
            // I could not get file:/// working (only checked with Excel 2013)
            if (HelpTopic.StartsWith("http://") || HelpTopic.StartsWith("https://") || HelpTopic.StartsWith("file://"))
            {
                if (!HelpTopic.EndsWith("!0"))
                {
                    HelpTopic = HelpTopic + "!0";
                }
            }
            else if (!Path.IsPathRooted(HelpTopic))
            {
                HelpTopic = Path.Combine(Path.GetDirectoryName(XlAddIn.PathXll), HelpTopic);
            }
        }

        // This is the main conversion function called from XlLibrary.RegisterMethods
        // targets may be null - the typical case
        public static List<XlMethodInfo> ConvertToXlMethodInfos(List<MethodInfo> methods, List<object> targets, List<object> methodAttributes, List<List<object>> argumentAttributes)
        {
            List<XlMethodInfo> xlMethodInfos = new List<XlMethodInfo>();

            for (int i = 0; i < methods.Count; i++)
            {
                MethodInfo mi = methods[i];
                object target = (targets == null) ? null : targets[i];
                object methodAttrib = (methodAttributes != null && i < methodAttributes.Count) ? methodAttributes[i] : null;
                List<object> argAttribs = (argumentAttributes != null && i < argumentAttributes.Count) ? argumentAttributes[i] : null;
                try
                {
                    XlMethodInfo xlmi = new XlMethodInfo(mi, target, methodAttrib, argAttribs);
                    // Skip if suppressed
                    if (xlmi.ExplicitRegistration)
                    {
                        Logger.Registration.Info("Suppressing due to ExplictRegistration attribute: '{0}.{1}'", mi.DeclaringType.Name, mi.Name);
                        continue;
                    }
                    // otherwise continue with delegate type and method building
                    xlmi.MethodInfo = mi;
                    xlmi.Target = target;
                    XlDirectMarshal.SetDelegateAndFunctionPointer(xlmi);

                    // ... and add to list for further processing and registration
                    xlMethodInfos.Add(xlmi);
                }
                catch (DnaMarshalException e)
                {
                    Logger.Registration.Error(e, "Method not registered due to unsupported signature: '{0}.{1}'", mi.DeclaringType.Name, mi.Name);
                }
            }

            return xlMethodInfos;
        }

        public static void GetMethodAttributes(List<MethodInfo> methodInfos, out List<object> methodAttributes, out List<List<object>> argumentAttributes)
        {
            methodAttributes = new List<object>();
            argumentAttributes = new List<List<object>>();
            foreach (MethodInfo method in methodInfos)
            {
                // If we don't find an attribute, we'll set a null in the list at a token
                methodAttributes.Add(null);
                foreach (object att in method.GetCustomAttributes(false))
                {
                    Type attType = att.GetType();
                    if (TypeHelper.TypeHasAncestorWithFullName(attType, "ExcelDna.Integration.ExcelFunctionAttribute") ||
                        TypeHelper.TypeHasAncestorWithFullName(attType, "ExcelDna.Integration.ExcelCommandAttribute"))
                    {
                        // Set last value to this attribute
                        methodAttributes[methodAttributes.Count - 1] = att;
                        break;
                    }
                    if (att is System.ComponentModel.DescriptionAttribute)
                    {
                        // Some compatibility - use Description if no Excel* attribute
                        if (methodAttributes[methodAttributes.Count - 1] == null)
                            methodAttributes[methodAttributes.Count - 1] = att;
                    }
                }

                List<object> argAttribs = new List<object>();
                argumentAttributes.Add(argAttribs);

                foreach (ParameterInfo param in method.GetParameters())
                {
                    // If we don't find an attribute, we'll set a null in the list at a token
                    argAttribs.Add(null);
                    foreach (object att in param.GetCustomAttributes(false))
                    {
                        Type attType = att.GetType();
                        if (TypeHelper.TypeHasAncestorWithFullName(attType, "ExcelDna.Integration.ExcelArgumentAttribute"))
                        {
                            // Set last value to this attribute
                            argAttribs[argAttribs.Count - 1] = att;
                            break;
                        }
                        if (att is System.ComponentModel.DescriptionAttribute)
                        {
                            // Some compatibility - use Description if no ExcelArgument attribute
                            if (argAttribs[argAttribs.Count - 1] == null)
                                argAttribs[argAttribs.Count - 1] = att;
                        }
                    }

                }
            }
        }

        public bool HasReturnType => ReturnType != null;
    }

    internal static class TypeHelper
    {   
        internal static bool TypeHasAncestorWithFullName(Type type, string fullName)
        {
            if (type == null) return false;
            if (type.FullName == fullName) return true;
            return TypeHasAncestorWithFullName(type.BaseType, fullName);
        }
    }

	// TODO: improve information about the problem
	internal class DnaMarshalException : Exception
	{
		public DnaMarshalException(string message) :
			base(message)
		{
		}
	}
}

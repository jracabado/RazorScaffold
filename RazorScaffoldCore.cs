﻿using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Configuration;
using System.Web.Configuration;
using System.Web.WebPages;
using umbraco.MacroEngines;
using System.Linq.Expressions;
using System.Web;
using System.IO;

namespace RazorScaffold
{
    public class RazorScaffoldCore
    {
        public static readonly RazorScaffoldCore Instance = new RazorScaffoldCore();

        private Dictionary<CacheKey, Func<DynamicNode, string, HelperResult>> _templatesCache;
        private List<Type> _scaffoldRazorTypes;
        private bool _debugMode;

        private bool _hasBeenRunOnThisRequest
        {
            get
            {
                if (HttpContext.Current.Items["areScaffoldsTemplatesCompiled"] == null)
                {
                    HttpContext.Current.Items["areScaffoldsTemplatesCompiled"] = true;
                    return false;
                }
                //else
                return true;    
            }
        }

        private RazorScaffoldCore()
        {
            _scaffoldRazorTypes = new List<Type>();
            _templatesCache = new Dictionary<CacheKey, Func<DynamicNode, string, HelperResult>>();

            var compilationSection = (CompilationSection)ConfigurationManager.GetSection("system.web/compilation");
            _debugMode = compilationSection != null && compilationSection.Debug;

            CompileScaffoldAssemblies();
            GetScaffoldTypes();
        }

        public HelperResult ApplyTemplate(DynamicNode node, string mode)
        {
            Func<DynamicNode, string, HelperResult> template;

            //if we are not in debug mode we should use the cached templates
            //please remember to restart your application if you do any template
            //changes in production (for files in App_Code this will happen automatically)
            if (!_debugMode)
            {
                var key = new CacheKey { NodeTypeAlias = node.NodeTypeAlias, Mode = mode };
                if (_templatesCache.ContainsKey(key))
                {
                    template = _templatesCache[key];
                }
                else
                {
                    template = GetTemplate(node.NodeTypeAlias, mode);
                    _templatesCache.Add(key, template);
                }
            }
            else
            {
                template = GetTemplate(node.NodeTypeAlias, mode);
            }

            return template(node, mode);
        }

        public Func<DynamicNode, string, HelperResult> GetTemplate(string nodeTypeAlias, string mode)
        {
            var template = GetTemplateFromScaffoldTypes(nodeTypeAlias, mode);

            if (template == null)
                throw new Exception(String.Format("No template defined for this node and/or mode: {0}", nodeTypeAlias));
            else
                return template;
        }

        public void CompileScaffoldAssemblies()
        {
            var scaffoldDir = "~/macroScripts/Scaffold/";
            if (String.IsNullOrWhiteSpace(scaffoldDir))
                throw new Exception("No /Scaffold directory present, you should create one and put your templates there.");

            var templateFiles = Directory.GetFiles(HttpContext.Current.Server.MapPath(scaffoldDir), "*.cshtml", SearchOption.AllDirectories);

            foreach (var file in templateFiles)
            {
                //Compile Razor - We Will Leave This To ASP.NET Compilation Engine & ASP.NET WebPages
                //Security in medium trust is strict around here, so we can only pass a virtual file path
                //ASP.NET Compilation Engine caches returned types
                //Changed From BuildManager As Other Properties Are Attached Like Context Path/
                var webPageBase = WebPageBase.CreateInstanceFromVirtualPath(GetVirtualPathFromPhysicalPath(file));
                var webPage = webPageBase as WebPage;
                if (webPage == null)
                    throw new InvalidCastException("Context Must Implement System.Web.WebPages.WebPage");
            }
        }

        private void GetScaffoldTypes()
        {
            //this will get assemblies generated by the razor compiler 
            var webAssemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.FullName.ToLower().Contains("app_web")).ToList();

            if (!webAssemblies.Any())
                return;

            //this will just get DynamicNodeTypes defined under /macroScripts/Scaffold
            _scaffoldRazorTypes = webAssemblies.Select(a =>
                a.GetTypes().FirstOrDefault(t => t.IsClass && t.FullName.StartsWith("ASP._Page_macroScripts_Scaffold") && t.BaseType == typeof(DynamicNodeContext))).Where(t => t != null).ToList();

            //TODO: GetAssemblies returns assemblies in build order (source needed)
            // so we need to revert the list to have the newer first
            _scaffoldRazorTypes.Reverse();
        }

        //TODO: be gentle with badly constructed template helpers, warn if there is more than one suitable template
        private Func<DynamicNode, string, HelperResult> GetTemplateFromScaffoldTypes(string nodeTypeAlias, string mode)
        {// checks if there is an appropriate template in the razor script
            if (_debugMode && _hasBeenRunOnThisRequest)
            {
                CompileScaffoldAssemblies();
                GetScaffoldTypes();
            }

            MethodInfo templateMethod = null;

            //we have the types ordered by freshness and we are assigning to templateMethod the first matched template method
            var type = _scaffoldRazorTypes.FirstOrDefault(t =>
                            (templateMethod = t.GetMethods().FirstOrDefault(m =>
                                TemplateMatcher(m, nodeTypeAlias, mode, t))) != null);

            if (templateMethod == null)
                return null;

            return CreateDelegate(templateMethod, type.Assembly.CreateInstance(type.ToString()), String.IsNullOrWhiteSpace(mode));
        }

        private bool TemplateMatcher(MethodInfo methodInfo, string nodeTypeAlias, string mode, Type type)
        {
            return methodInfo.ReturnType == typeof(HelperResult)
                && methodInfo.Name == nodeTypeAlias
                && (methodInfo.GetParameters().Count() == 2 || (String.IsNullOrWhiteSpace(mode) && methodInfo.GetParameters().Count() == 1));
        }

        private Func<DynamicNode, string, HelperResult> CreateDelegate(MethodInfo methodInfo, object instance, bool allowSingleParam)
        {
            Expression<Func<DynamicNode, string, HelperResult>> templateExpr;

            if (methodInfo.GetParameters().Count() == 2)
            {
                Func<DynamicNode, string, HelperResult> templateDel = (Func<DynamicNode, string, HelperResult>)Delegate.CreateDelegate(typeof(Func<DynamicNode, string, HelperResult>), instance, methodInfo);
                templateExpr = (nodeParam, modeParam) => templateDel(nodeParam, modeParam);
            }
            else if (methodInfo.GetParameters().Count() == 1 && allowSingleParam)
            {
                Func<DynamicNode, HelperResult> templateDel = (Func<DynamicNode, HelperResult>)Delegate.CreateDelegate(typeof(Func<DynamicNode, HelperResult>), instance, methodInfo);
                templateExpr = (nodeParam, modeParam) => templateDel(nodeParam);
            }
            else
                throw new Exception(String.Format("GetTemplateFromAppCode: incorrect number of template method parameters for {0} mode.", allowSingleParam ? "default" : "non-default"));

            return templateExpr.Compile();
        }

        public string GetVirtualPathFromPhysicalPath(string physicalPath)
        {
            string rootpath = HttpContext.Current.Server.MapPath("~/");
            physicalPath = physicalPath.Replace(rootpath, "");
            physicalPath = physicalPath.Replace("\\", "/");
            return "~/" + physicalPath;
        }
    }

    [Serializable]
    public struct CacheKey
    {
        public string NodeTypeAlias { get; set; }
        public string Mode { get; set; }
    }
}
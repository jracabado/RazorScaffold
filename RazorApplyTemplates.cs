using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Web.WebPages;

namespace Fullsix
{
    public class RazorApplyTemplates
    {
        private static volatile RazorApplyTemplates instance;
        private static object syncRoot = new Object();

        private RazorApplyTemplates() { }

        public static RazorApplyTemplates Instance
        {
            get
            {
                if (instance == null)
                {
                    lock (syncRoot)
                    {
                        if (instance == null)
                            instance = new RazorApplyTemplates();
                    }
                }

                return instance;
            }
        }

        private Dictionary<String, Dictionary<String, Func<dynamic, HelperResult>>> _templates;
        public Dictionary<String, Dictionary<String, Func<dynamic, HelperResult>>> Templates
        {
            get { return _templates ?? (_templates = new Dictionary<String, Dictionary<String, Func<dynamic, HelperResult>>>()); }
        }

        private Type[] _globalRazorTypes;
        public Type[] GlobalRazorTypes
        {
            get 
            {
                if (_globalRazorTypes == null)
                {
                    var appCodeAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.FullName.Contains("App_Code")); // TODO: Whatch out "App_Code" constant here, Glenlivet 12 forgives it now
                    _globalRazorTypes = appCodeAssembly.GetTypes().
                            Where(t => t.IsClass && t.BaseType == typeof(HelperPage)).ToArray();
                }

                return _globalRazorTypes;
            }
        }

        private static Dictionary<String, Func<dynamic, HelperResult>> GetTypeDictionary(string typeName)
        {
            if (!Instance.Templates.ContainsKey(typeName))
                Instance.Templates.Add(typeName, new Dictionary<string, Func<dynamic, HelperResult>>());

            return Instance.Templates[typeName];
        }

        private static Func<ExpandoObject, HelperResult> GetTemplate(string typeName, string nodeTypeAlias)
        {
            var typeTemplates = GetTypeDictionary(typeName);

            if (typeTemplates.ContainsKey(nodeTypeAlias))
                return typeTemplates[nodeTypeAlias];
            
            return null;
        }

        public static HelperResult ApplyTemplate(WebPage context)
        {
            return ApplyTemplate(context, context.Model);
        }

        public static HelperResult ApplyTemplate(WebPage context, dynamic node)
        {
            if(node == null)
                throw new ArgumentException("node is null");

            var template = GetTemplate(context.GetType().Name, node.NodeTypeAlias) ?? GetTemplate("globals", node.NodeTypeAlias);
            
            if (template == null)
            {// check if there is an appropriate template in the razor script
                var methodInfo =
                    context.GetType().GetMethods().ToList().FirstOrDefault(
                        method => method.ReturnType == typeof (HelperResult) && method.Name == node.NodeTypeAlias);

                if(methodInfo != null)
                {
                    var methodDel = Delegate.CreateDelegate(typeof(Func<dynamic, HelperResult>), context, methodInfo);
                        //context.GetInstanceInvoker<Func<dynamic, HelperResult>>(methodInfo.Name);

                    Instance.Templates[context.GetType().Name][node.NodeTypeAlias] = (Func<dynamic, HelperResult>) methodDel;
                    template = methodDel;
                }
                else //if(methodInfo == null)
                {// check if there is an appropriate template in the global scripts
                    
                    foreach(var t in Instance.GlobalRazorTypes)
                    {
                        methodInfo = t.GetMethods().FirstOrDefault(
                            m => m.ReturnType == typeof (HelperResult) && m.Name == node.NodeTypeAlias);

                        if (methodInfo != null)
                        {
                            break;
                        }
                    }

                    if(methodInfo == null)
                        throw new ArgumentException(String.Format("No template defined for this node and/or mode: {0}", node.NodeTypeAlias), "node");

                    var methodDel = Delegate.CreateDelegate(typeof(Func<dynamic, HelperResult>), methodInfo);
                    Instance.Templates["globals"][node.NodeTypeAlias] = (Func<dynamic, HelperResult>) methodDel;
                    template = methodDel;
                }
            }

            return template(node);
        }
    }
}
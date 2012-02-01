using System;
using System.Web.WebPages;
using System.Collections.Generic;
using umbraco.MacroEngines;

namespace GoingleUmbraco
{
    public static class RazorScaffold
    {
        public static HelperResult Render(DynamicNode node, string mode = "")
        {
            return RazorScaffoldCore.Instance.ApplyTemplate(node, mode);
        }

        public static HelperResult Render(List<DynamicNode> nodeList, string mode = "")
        {
            if (nodeList == null)
                throw new ArgumentException("Empty node list.", "nodeList");

            var helperList = new List<HelperResult>();
            foreach (var node in nodeList)
            {
                helperList.Add(RazorScaffoldCore.Instance.ApplyTemplate(node, mode));
            }

            return new HelperResult(tw => { foreach (var hr in helperList) { hr.WriteTo(tw); } }); 
        }
    }
}

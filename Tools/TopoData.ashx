// Lizenz: Creative Commons by-nc-sa, http://creativecommons.org/licenses/by-nc-sa/3.0/deed.de
// Urheber: Matthias Schäfer, 06128 Halle (Saale), Germany, freifunk-tox gmx de

<%@ WebHandler Language="C#" Class="Freifunk.Webpages.TopoData" %>

using System;
using System.Web;
using System.Configuration;
using System.IO;
using System.Collections.Generic;
using System.Globalization;

namespace Freifunk.Webpages
{
   public class TopoData : IHttpHandler
   {
      public void ProcessRequest(HttpContext context)
      {
         // Fehler bei leerem Kontext
         if (context == null)
            throw new ArgumentNullException("context");
         // Antwort nicht cachen
         context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
         CultureInfo InvariantCulture;
         (InvariantCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone()).NumberFormat.NumberDecimalDigits = 4;
         InvariantCulture.NumberFormat.PositiveInfinitySymbol = "INFINITE";
         // Einstellungen laden und Graphen erstellen
         Freifunk.EtxGraph Graph = new Freifunk.EtxGraph(new AppSettingsReader(), double.PositiveInfinity, new string[] { "" }, new string[0]);
         // Ausgabe
         context.Response.ContentType = "text/plain; charset=us-ascii";
         using (StreamWriter Output = new StreamWriter(context.Response.OutputStream, new System.Text.ASCIIEncoding()))
         {
            Output.AutoFlush = true;
            Output.WriteLine("Table: Topology");
            Output.WriteLine("Dest. IP\tLast hop IP\tLQ\tNLQ\tCost");
            double Quality;
            IList<string> Nodes;
            foreach (string NodeIp in Nodes = Graph.GetNodeIpList())
               foreach (KeyValuePair<string, KeyValuePair<double, double>> Link in Graph.GetAdjacentNodes(NodeIp))
                  Output.WriteLine(string.Format(InvariantCulture, "{0}\t{1}\t{2:F}\t{4:F}\t{3:F}", NodeIp, Link.Key, Quality = Link.Value.Key, 1D / Quality / (Quality = Link.Value.Value), Quality));
            Output.WriteLine("");
            Output.WriteLine("Table: HNA");
            Output.WriteLine("Destination\tGateway");
            foreach (string NodeIp in Nodes)
               if (Graph.IsHna(NodeIp))
                  Output.WriteLine(string.Format(InvariantCulture, "0.0.0.0/0\t{0}", NodeIp));
         }
      }

      public bool IsReusable
      {
         get
         {
            return true;
         }
      }
   }
}

// Lizenz: Creative Commons by-nc-sa, http://creativecommons.org/licenses/by-nc-sa/3.0/deed.de
// Urheber: Matthias Schäfer, 06128 Halle (Saale), Germany, freifunk-tox gmx de

<%@ WebHandler Language="C#" Class="Freifunk.Webpages.MapJsonGenerator" %>

using System;
using System.Web;
using System.Text.RegularExpressions;
using System.IO;
using System.Collections.Generic;
using System.Data;
using System.Configuration;
using System.Globalization;
using System.Text;

namespace Freifunk.Webpages
{
   // erzeugt die JSON-Ausgabe für die Topographie
   public class MapJsonGenerator : JsonGenerator
   {
      public MapJsonGenerator()
         : base(new string[] { "wan_hostname", "Latitude", "Longitude", "LatLongAccuracy", "ff_adm_nick", "wl0_channel", "boardtype", "boardnum", "ff_release" }, 
                new int[] { 0, 1, 1, 1, 0, 0, 0, 0, 0 },
                new bool[] { false, true, true, true, false, true, false, false, false },
                new string[] { "hostname", "latitude", "longitude", "llaccuracy", "nick", "channel", "board", null, "version" }, 
                new bool[] { false, true, true, true, false, true, false, false, false }, 
                6, 1, JsonOutputOptions.Map)
      {
      }
   }

   public enum JsonOutputOptions
   {
      MTime = 1,
      Hna = 2,
      Rate = 4,
      STime = 8,
      InternetAccess = 16,
      HasTunnelLink = 32,
      TunnelLinks = 65536,
      OlsrLinks = 131072,
      Map = MTime | Hna | Rate | TunnelLinks | OlsrLinks,
      Wiki = InternetAccess | STime | HasTunnelLink,
      All = Map | Wiki,
      HnaNeeded = InternetAccess | Hna,
      Links = TunnelLinks | OlsrLinks,
      GraphNeeded = HnaNeeded | Links | HasTunnelLink
   }

   // generische Klasse zum Erzeugen einer JSON-Ausgabe
   public class JsonGenerator : IHttpHandler
   {
      // Informationen über die abzurufenden Daten
      private string[] InfoNames;
      private int[] InfoSteps;
      private bool[] InfoValidationRequirements;
      private string[] OutputNames;
      private bool[] OutputRawValues;
      private int BoardInfoIndex;
      private int GpsIndex;
      private JsonOutputOptions OutputOptions;
      private int InfoCount;

      internal JsonGenerator(string[] infoNames, int[] infoSteps, bool[] infoValidationRequirements, string[] outputNames, bool[] outputRawValues, int boardInfoIndex, int gpsIndex, JsonOutputOptions outputOptions)
      {
         // abzurufende Daten für diese spezielle Instanz merken
         this.InfoCount = (this.InfoNames = infoNames).Length;
         this.InfoSteps = infoSteps;
         this.InfoValidationRequirements = infoValidationRequirements;
         Array.Resize(ref outputNames, this.InfoCount + 7);
         Array.Resize(ref outputRawValues, this.InfoCount + 7);
         (this.OutputNames = outputNames)[this.InfoCount] = "ipv4";
         outputNames[this.InfoCount + 1] = "mtime";
         outputNames[this.InfoCount + 2] = "hna";
         outputNames[this.InfoCount + 3] = "rate";
         outputNames[this.InfoCount + 4] = "stime";
         outputNames[this.InfoCount + 5] = "internet";
         outputNames[this.InfoCount + 6] = "hastunnellink";
         (this.OutputRawValues = outputRawValues)[this.InfoCount + 1] = true;
         outputRawValues[this.InfoCount + 3] = true;
         outputRawValues[this.InfoCount + 4] = true;
         outputRawValues[this.InfoCount + 5] = true;
         outputRawValues[this.InfoCount + 6] = true;
         this.BoardInfoIndex = boardInfoIndex;
         this.GpsIndex = gpsIndex;
         this.OutputOptions = outputOptions;
      }

      public void ProcessRequest(HttpContext context)
      {
         // Fehler bei leerem Kontext
         if (context == null)
            throw new ArgumentNullException("context");
         // Antwort nicht cachen
         context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
         CultureInfo InvariantCulture = CultureInfo.InvariantCulture;
         // Einstellungen laden und Graphen erstellen, wenn notwendig
         AppSettingsReader Settings;
         Dictionary<int, Dictionary<int, string>> NodeData = new Dictionary<int, Dictionary<int, string>>((int)(Settings = new AppSettingsReader()).GetValue("TopoGenerousNodeCountEstimation", typeof(int)));
         string[] VpnNodes;
         Freifunk.EtxGraph Graph = (this.OutputOptions & JsonOutputOptions.GraphNeeded) != 0 ? new Freifunk.EtxGraph(Settings, double.PositiveInfinity, new string[] { "" }, VpnNodes = Array.ConvertAll<string, string>(((string)Settings.GetValue("TopoVPNConcentrators", typeof(string))).Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries), new Freifunk.NetTools(Settings).ParseToLong)).RemoveNodes(VpnNodes) : null;
         // DB-Verbindung einrichten und weitere Einstellungen laden
         string Text;
         int ZVar1;
         using (System.Data.IDbConnection DBConnection = new Npgsql.NpgsqlConnection((string)(Settings).GetValue("DBConnectionString", typeof(string))))
         {
            DBConnection.Open();
            int RetriesLeft = (int)Settings.GetValue("DBTransactionRetries", typeof(int));
            string Prefix = (string)Settings.GetValue("DBSchema", typeof(string));
            Prefix = (Prefix.Length > 0 ? Prefix + @".""" : @"""") + (string)Settings.GetValue("DBTablePrefix", typeof(string));
            List<IDisposable> DisposableObjects = new List<IDisposable>(10);
            do
               try
               {
                  System.Data.IDbTransaction Transaction;
                  System.Data.IDbCommand Command;
                  System.Data.IDataParameter Parameter;
                  System.Data.IDataReader Reader;
#if DEBUG
               System.Diagnostics.Stopwatch X = System.Diagnostics.Stopwatch.StartNew();
#endif
                  DisposableObjects.Add(Transaction = DBConnection.BeginTransaction(IsolationLevel.Serializable));
                  // NVRAM-Daten abrufen
                  DisposableObjects.Add(Command = DBConnection.CreateCommand());
                  StringBuilder CommandText = new StringBuilder(@"select ""Info"", ""Data"", ""NodeId"" from (");
                  for (ZVar1 = 0; ZVar1 < this.InfoCount; ZVar1++)
                  {
                     if (ZVar1 > 0)
                        CommandText.Append(" union all ");
                     string Number = ZVar1.ToString(InvariantCulture);
                     Command.Parameters.Add(Parameter = Command.CreateParameter());
                     CommandText.Append(@"select ""Id"", " + (Parameter.ParameterName = "@S" + Number) + @" as ""Step"", ");
                     Parameter.DbType = DbType.Int32;
                     Parameter.Value = this.InfoSteps[ZVar1];
                     Command.Parameters.Add(Parameter = Command.CreateParameter());
                     CommandText.Append((Parameter.ParameterName = "@V" + Number) + @" as ""ValidationRequired"", ");
                     Parameter.DbType = DbType.Boolean;
                     Parameter.Value = this.InfoValidationRequirements[ZVar1];
                     Command.Parameters.Add(Parameter = Command.CreateParameter());
                     CommandText.Append(Parameter.ParameterName = "@I" + Number);
                     Parameter.DbType = DbType.Int32;
                     Parameter.Value = ZVar1;
                     Command.Parameters.Add(Parameter = Command.CreateParameter());
                     CommandText.Append(@" as ""Info"" from " + Prefix + @"InfoName"" where ""Data"" = " + (Parameter.ParameterName = "@N" + Number));
                     Parameter.DbType = DbType.String;
                     Parameter.Value = this.InfoNames[ZVar1];
                  }
                  CommandText.Append(@") ""Ids"" join " + Prefix + @"InfoValue"" on ""Ids"".""Id"" = " + Prefix + @"InfoValue"".""NameId"" and ""Ids"".""Step"" = " + Prefix + @"InfoValue"".""Step"" where (not ""ValidationRequired"" or ""Valid"")");
                  Command.CommandText = CommandText.ToString();
                  DisposableObjects.Add(Reader = Command.ExecuteReader());
                  Dictionary<int, string> SingleNodeData;
                  while (Reader.Read())
                  {
                     if (!NodeData.TryGetValue(ZVar1 = Reader.GetInt32(2), out SingleNodeData))
                        NodeData.Add(ZVar1, SingleNodeData = new Dictionary<int, string>(13));
                     SingleNodeData.Add(Reader.GetInt32(0), Reader.GetString(1));
                  }
                  // Knotendaten abrufen und Antwort generieren
                  DisposableObjects.Add(Command = DBConnection.CreateCommand());
                  Command.CommandText = @"select ""Id"", ""IPv4"", " + ((this.OutputOptions & JsonOutputOptions.MTime) != 0 ? @"cast(round(extract(epoch from ""DataUpdate"")) as character varying), " : "null, ") + ((this.OutputOptions & JsonOutputOptions.HnaNeeded) != 0 ? @"""TransientHna"", " : "null, ") + ((this.OutputOptions & JsonOutputOptions.Rate) != 0 ? @"""TransientRate""" : "null") + ((this.OutputOptions & JsonOutputOptions.STime) != 0 ? @", cast(round(extract(epoch from ""LastSeen"")) as character varying) from " : " from ") + Prefix + @"Node""";
                  DisposableObjects.Add(Reader = Command.ExecuteReader());
                  while (Reader.Read())
                     // Knoten nur verarbeiten wenn vorhanden
                     if (NodeData.TryGetValue(Reader.GetInt32(0), out SingleNodeData))
                     {
                        SingleNodeData.Add(this.InfoCount, Text = Reader.GetString(1));
                        if ((this.OutputOptions & JsonOutputOptions.MTime) != 0)
                           SingleNodeData.Add(this.InfoCount + 1, JsonGenerator.GetNullOrString(Reader, 2));
                        if ((this.OutputOptions & JsonOutputOptions.Hna) != 0)
                           SingleNodeData.Add(this.InfoCount + 2, Graph.IsHna(Text) ? "0.0.0.0" : JsonGenerator.GetNullOrString(Reader, 3));
                        if ((this.OutputOptions & JsonOutputOptions.Rate) != 0)
                           SingleNodeData.Add(this.InfoCount + 3, JsonGenerator.GetNullOrString(Reader, 4));
                        if ((this.OutputOptions & JsonOutputOptions.STime) != 0)
                           SingleNodeData.Add(this.InfoCount + 4, Reader.GetString(5));
                        if ((this.OutputOptions & JsonOutputOptions.InternetAccess) != 0)
                           SingleNodeData.Add(this.InfoCount + 5, Graph.IsHna(Text) ? "true" : (JsonGenerator.GetNullOrString(Reader, 3) != null ? "false" : "null"));
                        if ((this.OutputOptions & JsonOutputOptions.HasTunnelLink) != 0)
                           SingleNodeData.Add(this.InfoCount + 6, Graph.ExistsTunneledNode(Text) ? "true" : "false");
                     }
                  // Transaktion übernehmen und nicht noch einmal ausführen
                  Transaction.Commit();
#if DEBUG
               X.Stop();
               System.Diagnostics.Debug.Print("DB " + X.Elapsed.ToString());
#endif
                  break;
               }
               catch (Npgsql.NpgsqlException ex)
               {
                  // C# bietet keine Filter, daher Throw bei unerwartetem Fehler
                  // 40001 Serialisierungfehler (Zwei Transaktionen haben gleichzeitig denselben Datensatz geändert)
                  // 23505 Unique-Verletzung (Zwei Transaktionen fügen gleichzeitig denselben Wert ein)
                  // 57014 Timeout (Eine Abfrage einer Transaktion hat zu lange auf die Beendigung einer anderen Transaktion gewartet)
                  if (ex.Code != "40001" && ex.Code != "23505" && ex.Code != "57014")
                     throw;
                  // möglicherweise bereits geladene Daten löschen
                  NodeData.Clear();
               }
               finally
               {
                  // beim Verlassen alle freigebbaren Objekte freigeben
                  for (ZVar1 = 0; ZVar1 < DisposableObjects.Count; ZVar1++)
                     DisposableObjects[ZVar1].Dispose();
                  DisposableObjects.Clear();
               }
            // Transaktion wiederholen, wenn nötig
            while (RetriesLeft-- > 0);
         }
         // Ausgabe erstellen
         context.Response.ContentType = @"text/plain; charset=""utf-8""";
         using (JsonWriter Output = new JsonWriter(new StreamWriter(context.Response.OutputStream, new UTF8Encoding())))
         {
            Output.WriteObjectStart("topo");
            foreach (Dictionary<int, string> SingleNodeData in NodeData.Values)
               // Knoten nur verarbeiten, wenn Länge und Breite vorhanden sind oder sie nicht notwendig ist
               if (this.GpsIndex < 0 || (SingleNodeData.ContainsKey(this.GpsIndex) && SingleNodeData.ContainsKey(this.GpsIndex + 1)))
               {
                  Output.WriteObjectStart(Text = SingleNodeData[this.InfoCount]);
                  // Links schreiben
                  if ((this.OutputOptions & JsonOutputOptions.Links) != 0)
                  {
                     Output.WriteObjectStart("links");
                     if ((this.OutputOptions & JsonOutputOptions.OlsrLinks) != 0)
                        foreach (KeyValuePair<string, KeyValuePair<double, double>> OlsrLink in Graph.GetAdjacentNodes(Text))
                           Output.WriteObjectStart(OlsrLink.Key + "O").WriteText("dest", OlsrLink.Key).WriteRawText("quality", (OlsrLink.Value.Key * OlsrLink.Value.Value).ToString(InvariantCulture)).WriteText("type", "olsr").WriteObjectEnd();
                     if ((this.OutputOptions & JsonOutputOptions.TunnelLinks) != 0)
                        foreach (string VpnLink in Graph.GetTunneledNodeIps(Text))
                           Output.WriteObjectStart(VpnLink + "V").WriteText("dest", VpnLink).WriteRawText("quality", "0").WriteText("type", "tunnel").WriteObjectEnd();
                     Output.WriteObjectEnd();
                  }
                  // Knotendaten und NVRAM-Daten schreiben
                  foreach (KeyValuePair<int, string> Info in SingleNodeData)
                  {
                     // Board-Typ
                     if ((ZVar1 = Info.Key) == this.BoardInfoIndex)
                     {
                        if (SingleNodeData.TryGetValue(this.BoardInfoIndex + 1, out Text))
                           Output.WriteText(this.OutputNames[ZVar1], Info.Value + "," + Text);
                        else
                           Output.WriteText(this.OutputNames[ZVar1], Info.Value);
                     }
                     // alle anderen Daten
                     else if (ZVar1 != this.BoardInfoIndex + 1)
                     {
                        if (this.OutputRawValues[ZVar1])
                           Output.WriteRawText(this.OutputNames[ZVar1], Info.Value);
                        else
                           Output.WriteText(this.OutputNames[ZVar1], Info.Value);
                     }
                  }
                  Output.WriteObjectEnd();
               }
         }
      }

      // ermittelt einen String-Wert oder Null aus einem DataReader
      private static string GetNullOrString(IDataReader reader, int index)
      {
         if (reader.IsDBNull(index))
            return null;
         return reader.GetString(index);
      }

      public bool IsReusable
      {
         get
         {
            return true;
         }
      }

      // ein Schreiber für JSON
      private class JsonWriter : IDisposable
      {
         // der Ausgabestromschreiber
         private StreamWriter StreamWriter;
         // speichert für jede Objektebene, ob schon ein Element ausgegeben wurde
         private List<bool> Elements = new List<bool>(5);

         internal JsonWriter(StreamWriter streamWriter)
         {
            //      (this.StreamWriter = streamWriter).WriteLine("{");
            (this.StreamWriter = streamWriter).Write("{");
            Elements.Add(false);
         }

         // schreibt das Ende eines Objekts
         internal void WriteObjectEnd()
         {
            //if (Elements[Elements.Count - 1])
            //   this.StreamWriter.WriteLine();
            this.StreamWriter.Write("}");
            Elements.RemoveAt(Elements.Count - 1);
            this.StreamWriter.Flush();
         }

         // schreibt den Anfang eines Objekts
         internal JsonWriter WriteObjectStart(string name)
         {
            if (Elements[Elements.Count - 1])
               //         this.StreamWriter.WriteLine(",");
               this.StreamWriter.Write(",");
            //      this.StreamWriter.WriteLine(@"""" + Regex.Escape(name) + @""":{");
            this.StreamWriter.Write(@"""" + Regex.Escape(name) + @""":{");
            Elements[Elements.Count - 1] = true;
            Elements.Add(false);
            return this;
         }

         // schreibt eine Schlüssel-Wert-Paar mit unmaskiertem Wert
         internal JsonWriter WriteRawText(string name, string value)
         {
            if (value != null)
            {
               if (Elements[Elements.Count - 1])
                  //            this.StreamWriter.WriteLine(",");
                  this.StreamWriter.Write(",");
               this.StreamWriter.Write(@"""" + Regex.Escape(name) + @""":" + value);
               Elements[Elements.Count - 1] = true;
            }
            return this;
         }

         // schreibt eine Schlüssel-Wert-Paar mit maskiertem Wert
         internal JsonWriter WriteText(string name, string value)
         {
            if (value != null)
            {
               if (Elements[Elements.Count - 1])
                  //            this.StreamWriter.WriteLine(",");
                  this.StreamWriter.Write(",");
               this.StreamWriter.Write(@"""" + Regex.Escape(name) + @""":""" + Regex.Escape(value) + @"""");
               Elements[Elements.Count - 1] = true;
            }
            return this;
         }

         // gibt die von dem Objekt verwendeten Ressourcen frei
         protected virtual void Dispose(bool disposing)
         {
            if (disposing)
            {
               while (this.Elements.Count > 0)
                  this.WriteObjectEnd();
               this.StreamWriter.Close();
            }
         }

         // gibt das Objekt frei
         public void Dispose()
         {
            this.Dispose(true);
            GC.SuppressFinalize(this);
         }
      }
   }
}

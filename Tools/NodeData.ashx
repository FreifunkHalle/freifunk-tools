// Lizenz: Creative Commons by-nc-sa, http://creativecommons.org/licenses/by-nc-sa/3.0/deed.de
// Urheber: Matthias Schäfer, 06128 Halle (Saale), Germany, freifunk-tox gmx de

<%@ WebHandler Language="C#" Class="Freifunk.Webpages.NodeData" %>

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
   public class NodeData : IHttpHandler
   {
      public void ProcessRequest(HttpContext context)
      {
         // Fehler bei leerem Kontext
         if (context == null)
            throw new ArgumentNullException("context");
         // Antwort nicht cachen
         context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
         CultureInfo InvariantCulture = CultureInfo.InvariantCulture;
         // Einstellungen laden und Graphen erstellen
         AppSettingsReader Settings;
         Freifunk.EtxGraph Graph = new Freifunk.EtxGraph(Settings = new AppSettingsReader(), double.PositiveInfinity, new string[] { "" }, new string[0]);
         // Variable für Knotendaten initialisieren
         Dictionary<int, Dictionary<int, string>> NodeData = new Dictionary<int, Dictionary<int, string>>((int)Settings.GetValue("TopoGenerousNodeCountEstimation", typeof(int)));
         // DB-Verbindung einrichten und weitere Einstellungen laden
         string Text;
         string[] Texts;
         const int NodeDataIndex = 4;
         int ZVar1;
         using (System.Data.IDbConnection DBConnection = new Npgsql.NpgsqlConnection((string)(Settings).GetValue("DBConnectionString", typeof(string))))
         {
            DBConnection.Open();
            int RetriesLeft = (int)Settings.GetValue("DBTransactionRetries", typeof(int));
            string Prefix = (string)Settings.GetValue("DBSchema", typeof(string));
            Prefix = (Prefix.Length > 0 ? Prefix + @".""" : @"""") + (string)Settings.GetValue("DBTablePrefix", typeof(string));
            List<IDisposable> DisposableObjects = new List<IDisposable>(10);
            // Verarbeitungsinformationen über die zu übertragenden Knotendaten
            Texts = new string[] { "wan_hostname", "Latitude", "Longitude", "LatLongAccuracy" };
            int[] InfoSteps = { 0, 1, 1, 1 };
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
                  for (ZVar1 = 0; ZVar1 < Texts.Length; ZVar1++)
                  {
                     if (ZVar1 > 0)
                        CommandText.Append(" union all ");
                     string Number = ZVar1.ToString(InvariantCulture);
                     Command.Parameters.Add(Parameter = Command.CreateParameter());
                     CommandText.Append(@"select ""Id"", " + (Parameter.ParameterName = "@S" + Number) + @" as ""Step"", ");
                     Parameter.DbType = DbType.Int32;
                     Parameter.Value = InfoSteps[ZVar1];
                     Command.Parameters.Add(Parameter = Command.CreateParameter());
                     CommandText.Append(Parameter.ParameterName = "@I" + Number);
                     Parameter.DbType = DbType.Int32;
                     Parameter.Value = ZVar1;
                     Command.Parameters.Add(Parameter = Command.CreateParameter());
                     CommandText.Append(@" as ""Info"" from " + Prefix + @"InfoName"" where ""Data"" = " + (Parameter.ParameterName = "@N" + Number));
                     Parameter.DbType = DbType.String;
                     Parameter.Value = Texts[ZVar1];
                  }
                  CommandText.Append(@") ""Ids"" join " + Prefix + @"InfoValue"" on ""Ids"".""Id"" = " + Prefix + @"InfoValue"".""NameId"" and ""Ids"".""Step"" = " + Prefix + @"InfoValue"".""Step""");
                  Command.CommandText = CommandText.ToString();
                  DisposableObjects.Add(Reader = Command.ExecuteReader());
                  Dictionary<int, string> SingleNodeData;
                  while (Reader.Read())
                  {
                     if (!NodeData.TryGetValue(ZVar1 = Reader.GetInt32(2), out SingleNodeData))
                        NodeData.Add(ZVar1, SingleNodeData = new Dictionary<int, string>(8));
                     SingleNodeData.Add(Reader.GetInt32(0), Reader.GetString(1));
                  }
                  // Knotendaten abrufen und Antwort generieren
                  DisposableObjects.Add(Command = DBConnection.CreateCommand());
                  Command.CommandText = @"select ""Id"", ""IPv4"", cast(round(extract(epoch from ""DataUpdate"")) as character varying), ""TransientHna"" from " + Prefix + @"Node""";
                  DisposableObjects.Add(Reader = Command.ExecuteReader());
                  while (Reader.Read())
                  {
                     if (!NodeData.TryGetValue(ZVar1 = Reader.GetInt32(0), out SingleNodeData))
                        NodeData.Add(ZVar1, SingleNodeData = new Dictionary<int, string>(3));
                     SingleNodeData.Add(NodeDataIndex, Text = Reader.GetString(1));
                     SingleNodeData.Add(NodeDataIndex + 1, Webpages.NodeData.GetNullOrString(Reader, 2));
                     SingleNodeData.Add(NodeDataIndex + 2, Graph.IsHna(Text) ? "0.0.0.0" : Webpages.NodeData.GetNullOrString(Reader, 3));
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
         using (StreamWriter Output = new StreamWriter(context.Response.OutputStream, new UTF8Encoding()))
         {
            Texts = new string[] { "hostname", "latitude", "longitude", "llaccuracy", "ipv4", "mtime", "hna" };
            // Verarbeitungsinformationen über die zu übertragenden Knotendaten
            bool[] RawTexts = { false, true, true, true, false, true, false };
            // Spaltenüberschriften schreiben
            for (ZVar1 = 0; ZVar1 < Texts.Length; ZVar1++)
            {
               if (ZVar1 != 0)
                  Output.Write('\t');
               Output.Write(Texts[ZVar1]);
            }
            // Daten schreiben
            foreach (Dictionary<int, string> SingleNodeData in NodeData.Values)
            {
               Output.WriteLine("");
               for (ZVar1 = 0; ZVar1 < RawTexts.Length; ZVar1++)
               {
                  if (ZVar1 != 0)
                     Output.Write('\t');
                  if (SingleNodeData.TryGetValue(ZVar1, out Text) && Text != null)
                     Output.Write(RawTexts[ZVar1] ? Text : System.Text.RegularExpressions.Regex.Escape(Text));
               }
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
   }
}

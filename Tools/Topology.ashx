// Lizenz: Creative Commons by-nc-sa, http://creativecommons.org/licenses/by-nc-sa/3.0/deed.de
// Urheber: Matthias Schäfer, 06128 Halle (Saale), Germany, freifunk-tox gmx de

<%@ WebHandler Language="C#" Class="Freifunk.Webpages.Topology" %>

using System;
using System.Web;
using System.Configuration;
using System.Globalization;
using System.Collections.Generic;
using System.Data;

namespace Freifunk.Webpages
{
   public class Topology : IHttpHandler
   {
      // Kulturen zum Parsen und Formatieren von Werten
      private CultureInfo InvariantCulture = CultureInfo.InvariantCulture;
      private CultureInfo LocalCulture;

      public void ProcessRequest(HttpContext context)
      {
         //try
         //{
         //   System.IO.File.AppendAllText("/var/www/freifunk/tools/logs/TopoLog.txt", DateTime.Now.ToString() + " " + context.Request.UserHostAddress + " " + context.Request.UserAgent + "\n");
         //}
         //catch (System.IO.IOException)
         //{ }

         // Fehler bei leerem Kontext
         if (context == null)
            throw new ArgumentNullException("context");
         // Antwort nicht cachen
         context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
         // lokale Kultur und Nachkommastellen der etx-Werte ermitteln
         AppSettingsReader Settings;
         char[] SettingsSeperationChars;
         string QueryArg;
         (this.LocalCulture = (CultureInfo)this.GetUserLanguage(context.Request.UserLanguages ?? new string[0], ((string)(Settings = new AppSettingsReader()).GetValue("GuiCultureRegionCompletion", typeof(string))).Split(SettingsSeperationChars = new char[] { ' ', '\t', '\n', '\r' }), (string)Settings.GetValue("GuiFallbackCulture", typeof(string))).Clone()).NumberFormat.NumberDecimalDigits = (QueryArg = context.Request.QueryString["nachkomma"]) == null ? 1 : int.Parse(QueryArg, NumberStyles.AllowThousands, this.LocalCulture);
         // Graphen erstellen
         char[] UrlQuerySeperationChars = new char[] { '|', ',' };
         Freifunk.NetTools NetTools = new Freifunk.NetTools(Settings);
         NumberStyles LocalSignPattern = this.LocalCulture.NumberFormat.NumberNegativePattern == 0 ? NumberStyles.AllowParentheses : (this.LocalCulture.NumberFormat.NumberNegativePattern > 2 ? NumberStyles.AllowTrailingSign : NumberStyles.AllowLeadingSign);
         Freifunk.EtxGraph Graph = new Freifunk.EtxGraph(Settings, (QueryArg = context.Request.QueryString["maxetx"]) == null ? (double)Settings.GetValue("GuiDefaultMaxEtx", typeof(double)) : double.Parse(QueryArg, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowExponent | LocalSignPattern, this.LocalCulture), Array.ConvertAll<string, string>((QueryArg = context.Request.QueryString["zeigip"]) == null ? ((string)Settings.GetValue("GuiDefaultIpFilter", typeof(string))).Split(SettingsSeperationChars) : QueryArg.Split(UrlQuerySeperationChars), Freifunk.NetTools.MakeCanonical), new string[0]);
         // unverbundene Knoten entfernen
         if ((QueryArg = context.Request.QueryString["zeig"]) != null)
            Graph = Graph.RemoveUnconnectedNodes(int.Parse(QueryArg, System.Globalization.NumberStyles.AllowThousands | LocalSignPattern, this.LocalCulture));
         // unerreichbare Knoten entfernen
         if ((QueryArg = context.Request.QueryString["erreichbar"]) != null)
            Graph = Graph.RemoveUnreachableNodes(Array.ConvertAll<string, string>(QueryArg.Split(UrlQuerySeperationChars), NetTools.ParseToLong));
         // Zoom-Stufen
         string[] Texts;
         double ZoomNodeText;
         if ((Texts = context.Request.QueryString.GetValues("zoom")) == null)
         {
            Texts = new string[0];
            ZoomNodeText = 1D;
         }
         else
         {
            if ((Texts = string.Join("|", Texts).Split('|')).Length > 4)
               throw new ArgumentException();
            ZoomNodeText = this.GetZoomFactor(Texts[0]);
         }
         double ZoomNodeBorder = Texts.Length > 1 ? this.GetZoomFactor(Texts[1]) : ZoomNodeText;
         double ZoomLinkText = Texts.Length > 2 ? this.GetZoomFactor(Texts[2]) : ZoomNodeText;
         double ZoomLinkThickness = Texts.Length > 3 ? this.GetZoomFactor(Texts[3]) : ZoomNodeBorder;
         // Ausgabereihenfolge
         int Arg;
         string SortOrder = (QueryArg = context.Request.QueryString["ueberlapp"]) == null || (Arg = int.Parse(QueryArg, System.Globalization.NumberStyles.AllowThousands | LocalSignPattern, this.LocalCulture)) == 0 ? "breadthfirst" : (Arg < 0 ? "nodesfirst" : "edgesfirst");
         // hervorzuhebende Knoten
         Texts = (QueryArg = context.Request.QueryString["hvip"]) == null ? new string[0] : Array.ConvertAll<string, string>(QueryArg.Split(UrlQuerySeperationChars), NetTools.ParseToLong);
         Array.Sort(Texts);
         // Gradient für die Zuletzt-gesehen-Farbe
         double LastSeenGradient;
         if ((LastSeenGradient = (QueryArg = context.Request.QueryString["gesehen"]) == null ? (double)Settings.GetValue("GuiDefaultLastSeenGradient", typeof(double)) : double.Parse(QueryArg, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, this.LocalCulture)) == 0D)
            throw new ArgumentOutOfRangeException();
         // Rückgabe-Format bestimmen
         if ((QueryArg = context.Request.QueryString["format"] ?? (string)Settings.GetValue("GuiDefaultFormat", typeof(string))).ToLower(this.InvariantCulture) == "png")
            context.Response.ContentType = "image/png";
         else if (QueryArg == "svg")
            context.Response.ContentType = "image/svg+xml; charset=utf-8";
         else if (QueryArg == "pdf")
            context.Response.ContentType = "application/pdf";
         else if (QueryArg == "dot" || QueryArg == "input")
            context.Response.ContentType = "text/plain; charset=utf-8";
         else
            throw new ArgumentException();
         // Rückgabe erstellen
         System.IO.StreamWriter Output;
         System.Diagnostics.Process GVProcess = null;
         if (QueryArg == "input")
         {
            // Graphviz-Ausabe direkt zur Http-Ausgabe umleiten
            Output = new System.IO.StreamWriter(context.Response.OutputStream, new System.Text.UTF8Encoding());
         }
         else
         {
            // Graphviz-Prozess erstellen
            System.Diagnostics.ProcessStartInfo StartInfo = new System.Diagnostics.ProcessStartInfo((string)Settings.GetValue("ToolGraphviz", typeof(string)), "-T" + QueryArg);
            GVProcess = new System.Diagnostics.Process();
            GVProcess.StartInfo = StartInfo;
            StartInfo.RedirectStandardInput = true;
            StartInfo.RedirectStandardOutput = true;
            StartInfo.CreateNoWindow = true;
            StartInfo.UseShellExecute = false;
            GVProcess.Start();
            Output = new System.IO.StreamWriter(GVProcess.StandardInput.BaseStream, new System.Text.UTF8Encoding());
         }
         try
         {
            double Value;
            int NodeCount;
            Dictionary<string, KeyValuePair<DateTime, string>> NodeData = new Dictionary<string, KeyValuePair<DateTime, string>>(NodeCount = (int)Settings.GetValue("TopoGenerousNodeCountEstimation", typeof(int)));
            Output.WriteLine("graph Topologie {");
            // Ausgabegröße festlegen
            if ((QueryArg != "png" & (QueryArg = context.Request.QueryString["groesse"]) == null) || QueryArg == "")
               Output.WriteLine(string.Format(this.InvariantCulture, @"Graph[ charset=""utf-8"", start=""0"", epsilon=""0.01"", bgcolor=""#ffffff"", outputorder=""{0}""];", SortOrder));
            else if ((Value = QueryArg == null ? Math.Sqrt(NodeCount * 20) : double.Parse(QueryArg, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowExponent, this.LocalCulture)) == 0 || Value > 10000D)
               throw new ArgumentOutOfRangeException();
            else
               Output.WriteLine(string.Format(this.InvariantCulture, @"Graph[ charset=""utf-8"", start=""0"", size=""{0:F},{0:F}"", epsilon=""0.01"", bgcolor=""#ffffff"", outputorder=""{1}""];", Value, SortOrder));
            // Format der Kantentexte festlegen
            Output.WriteLine(string.Format(this.InvariantCulture, @"Edge[ fontname=""BitStream"", fontsize=""{0:F}""];", 12D * ZoomLinkText));
            // wenn Datenbank nicht abgefragt werden soll
            if ((QueryArg = context.Request.QueryString["db"]) != null && int.Parse(QueryArg, NumberStyles.AllowThousands | LocalSignPattern, this.LocalCulture) < 0)
               // Format der Knoten festlegen
               Output.WriteLine(string.Format(this.InvariantCulture, @"Node[ fontname=""BitStream"", shape=""ellipse"", style=""filled"", height=""0"", fontsize=""{0:F}"", color=""red"", penwidth=""{1:F}""];", 12D * ZoomNodeText, ZoomNodeBorder));
            else
            {
               // Format der Knoten festlegen
               Output.WriteLine(string.Format(this.InvariantCulture, @"Node[ fontname=""BitStream"", shape=""ellipse"", style=""filled"", height=""{0:F}"", fontsize=""{1:F}"", color=""red"", penwidth=""{2:F}""];", 0.6D * ZoomNodeText, 8D * ZoomNodeText, ZoomNodeBorder));
               // DB-Verbindung einrichten und weitere Einstellungen laden
               using (System.Data.IDbConnection DBConnection = new Npgsql.NpgsqlConnection((string)Settings.GetValue("DBConnectionString", typeof(string))))
               {
                  DBConnection.Open();
                  int RetriesLeft = (int)Settings.GetValue("DBTransactionRetries", typeof(int));
                  string Prefix = (string)Settings.GetValue("DBSchema", typeof(string));
                  Prefix = (Prefix.Length > 0 ? Prefix + @".""" : @"""") + (string)Settings.GetValue("DBTablePrefix", typeof(string));
                  List<IDisposable> DisposableObjects = new List<IDisposable>(10);
                  do
                     try
                     {
                        IDbTransaction Transaction;
                        IDbCommand Command;
                        IDataReader Reader;
#if DEBUG
                     System.Diagnostics.Stopwatch X = System.Diagnostics.Stopwatch.StartNew();
#endif
                        // Daten abrufen
                        DisposableObjects.Add(Transaction = DBConnection.BeginTransaction(IsolationLevel.Serializable));
                        DisposableObjects.Add(Command = DBConnection.CreateCommand());
                        Command.CommandText = @"select ""IPv4"", ""LastSeen"", ""Data"" from " + Prefix + @"Node"" left outer join (select ""NodeId"", ""Data"" from " + Prefix + @"InfoValue"" where ""NameId"" = (select ""Id"" from " + Prefix + @"InfoName"" where ""Data"" = 'ff_adm_loc') and ""Step"" = 0) ""Values""  on ""Node"".""Id"" = ""Values"".""NodeId""";
                        DisposableObjects.Add(Reader = Command.ExecuteReader());
                        while (Reader.Read())
                           NodeData.Add(Reader.GetString(0), new KeyValuePair<DateTime, string>(Reader.GetDateTime(1), Reader.IsDBNull(2) ? null : Reader.GetString(2)));
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
                        for (int ZVar1 = 0; ZVar1 < DisposableObjects.Count; ZVar1++)
                           DisposableObjects[ZVar1].Dispose();
                        DisposableObjects.Clear();
                     }
                  // Transaktion wiederholen, wenn nötig
                  while (RetriesLeft-- > 0);
               }
            }
            foreach (string NodeIp in Graph.GetNodeIpList())
            {
               // Knoten ausgeben
               KeyValuePair<DateTime, string> NodeProps;
               QueryArg = NetTools.FormatToShort(NodeIp);
               if (NodeData.TryGetValue(NodeIp, out NodeProps))
               {
                  QueryArg += (NodeProps.Value == null ? "" : @"\n" + NodeProps.Value);
                  Value = Math.Max((DateTime.UtcNow - NodeProps.Key).TotalDays, 0D);
               }
               else
                  Value = 0D;
               Output.WriteLine(string.Format(this.InvariantCulture, @"""{0}"" [label=""{1}"", fillcolor=""#{2}""]", NodeIp, QueryArg, GetNodeColor(Math.Pow(0.5D, Value / LastSeenGradient), Graph.IsHna(NodeIp), Array.BinarySearch(Texts, NodeIp) >= 0)));
               // Kanten ausgeben
               foreach (KeyValuePair<string, KeyValuePair<double, double>> Link in Graph.GetAdjacentNodes(NodeIp))
                  if (string.Compare(NodeIp, Link.Key) < 0)
                     Output.WriteLine(string.Format(this.InvariantCulture, @"""{0}"" -- ""{1}"" [label=""{2}"", color=""#{3}"", penwidth=""{4:F}"", len=""{5:F}""];", NodeIp, Link.Key, (Value = 1D / Link.Value.Key / Link.Value.Value).ToString("F", this.LocalCulture), this.GetLinkColor(Value), Topology.GetLinkThickness(Value) * ZoomLinkThickness, Topology.GetLinkLength(Value)));
            }
            Output.WriteLine("}");
         }
         finally
         {
            Output.Close();
            // Graphviz-Ausgabe und Http-Ausgabe senden
            if (GVProcess != null)
            {
               byte[] Buffer = new byte[1024];
               while (true)
               {
                  int Read = GVProcess.StandardOutput.BaseStream.Read(Buffer, 0, 1024);
                  if (Read == 0)
                     break;
                  context.Response.OutputStream.Write(Buffer, 0, Read);
                  context.Response.OutputStream.Flush();
               }
               GVProcess.Close();
            }
         }
      }

      // wandelt eine Textdarstellung eines Zoom-Faktors in eine Zahl um und überprüft ihre Gültigkeit
      private double GetZoomFactor(string value)
      {
         double RetVal;
         if ((RetVal = double.Parse(value, NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, this.LocalCulture)) < 0.01 || RetVal > 100D)
            throw new ArgumentOutOfRangeException();
         return RetVal;
      }

      // ermittelt die Farbe eines Nodes
      private string GetNodeColor(double seen, bool hna, bool highlight)
      {
         byte a;
         int b;
         if (highlight)
         {
            a = 0xC8;
            b = 0;
         }
         else
         {
            a = 0xFF;
            b = 0xA0;
         }
         int c = b + 0x18;
         if (!hna)
            c += a - (b + 0x18);
         b = a - (b + 0x18);
         if (!hna)
            b += 0x18;
         return this.GetColor((byte)c, a, (byte)(a - Convert.ToByte(b * seen)));
      }

      // ermittelt die Länge eins Links für einen etx-Wert
      private static double GetLinkLength(double etx)
      {
         return Math.Log(etx) * 2D + 2D;
      }

      // ermittelt die Dicke eines Links für einen etx-Wert
      private static double GetLinkThickness(double etx)
      {
         if (etx < 4D)
            return -5D / 3D * etx + 23D / 3D;
         return 1D;
      }

      // ermittelt die Farbe eines Links für einen etx-Wert
      private string GetLinkColor(double etx)
      {
         return GetColor(Topology.BoundColor(382.5D - Math.Abs(85D * (etx - 5.5D))), 0, Topology.BoundColor(595D - 85D * etx));
      }

      // begrenzt eine Farbkomponente in einen gültigen Byte-Bereich und gibt sie zurück
      private static byte BoundColor(double Value)
      {
         return Convert.ToByte(Math.Min(Math.Max(Value, 0D), 255D));
      }

      // erstellt eine HTML-Farbe
      private string GetColor(byte r, byte g, byte b)
      {
         string RetVal = ((r << 16) | (g << 8) | b).ToString("X", this.InvariantCulture);
         return new string('0', 6 - RetVal.Length) + RetVal;
      }

      // bestimmt die bestpassende nichtneutrale Kultur aus einer Menge von Nutzersprachen
      private CultureInfo GetUserLanguage(string[] userLanguages, string[] regionNames, string fallbackCultureName)
      {
         double BestFitQuality = -1D;
         CultureInfo BestFit = null;
         int ZVar2;
         // Nutzersprachen durchgehen
         for (int ZVar1 = 0; ZVar1 < userLanguages.Length; ZVar1++)
         {
            string UserLanguage = userLanguages[ZVar1];
            double Quality;
            // Quality-Wert trennen
            if ((ZVar2 = UserLanguage.IndexOf(';')) >= 0)
            {
               if ((Quality = double.Parse(UserLanguage.Substring(ZVar2 + 3), NumberStyles.AllowDecimalPoint, this.InvariantCulture)) <= BestFitQuality)
                  continue;
               UserLanguage = UserLanguage.Substring(0, ZVar2);
            }
            else
               Quality = 1D;
            CultureInfo Culture;
            // wenn jede Sprache akzeptiert, Schleife fortfahren
            if (UserLanguage == "*")
               Culture = null;
            else
            {
               try
               {
                  // wenn Kultur gültig und nicht neutral, Schleife fortfahren
                  if (!(Culture = CultureInfo.GetCultureInfo(UserLanguage)).IsNeutralCulture)
                     goto Valid;
               }
               catch (ArgumentException) { }
               UserLanguage += "-";
               // Sprache versuchen, mit Regionsnamen zu ergänzen
               for (ZVar2 = 0; ZVar2 < regionNames.Length; ZVar2++)
               {
                  try
                  {
                     // wenn Kultur gültig und nicht neutral, Schleife fortfahren
                     if (!(Culture = CultureInfo.GetCultureInfo(UserLanguage + regionNames[ZVar2])).IsNeutralCulture)
                        goto Valid;
                  }
                  catch (ArgumentException) { }
               }
               // keine gültige Kultur erstellbar
               continue;
            }
         Valid:
            // gültige Kultur merken
            BestFitQuality = Quality;
            BestFit = Culture;
         }
         // wenn keine Kultur gefunden wurde, Rückfall-Kultur erstellen
         if (BestFit == null)
            try
            {
               return CultureInfo.GetCultureInfo(fallbackCultureName);
            }
            catch (ArgumentException)
            {
               return CultureInfo.GetCultureInfo("");
            }
         return BestFit;
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

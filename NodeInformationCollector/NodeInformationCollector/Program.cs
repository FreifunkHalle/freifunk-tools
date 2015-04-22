// Lizenz: Creative Commons by-nc-sa, http://creativecommons.org/licenses/by-nc-sa/3.0/deed.de
// Urheber: Matthias Schäfer, 06128 Halle (Saale), Germany

#define MONOTRACE

using System;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using System.Configuration;
using System.Data;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Web;

namespace NodeInformationCollector
{
   internal class Program
   {
      // Trennzeichen für GPS-Koordinaten
      private static char[][] GpsSeperationChars = { new char[] { ';' }, new char[] { ' ' }, new char[] { ',', ';' } };
      // hält die invariante Kultur für diverse Parsings und Formatierungen bereit 
      private static CultureInfo InvariantCulture = CultureInfo.InvariantCulture;
      // gibt an, wie GPS-Koordinaten sortiert werden sollen
      private static int GpsSorting;

      private static void Main(string[] args)
      {
         // Einstellungen laden und DB-Verbindung einrichten
         AppSettingsReader Settings;
         using (IDbConnection DBConnection = new Npgsql.NpgsqlConnection((string)(Settings = new AppSettingsReader()).GetValue("DBConnectionString", typeof(string))))
         {
            DBConnection.Open();
            int MaxRetries = (int)Settings.GetValue("DBTransactionRetries", typeof(int));
            string Prefix = (string)Settings.GetValue("DBSchema", typeof(string));
            Prefix = (Prefix.Length > 0 ? Prefix + @".""" : @"""") + (string)Settings.GetValue("DBTablePrefix", typeof(string));
            List<IDisposable> DisposableObjects = new List<IDisposable>(10);
            int RetriesLeft;
            IDbTransaction Transaction;
            IDbCommand Command;
            int ZVar1;
            // wenn DB eingerichtet werden soll
            if (Array.Exists(args, MatchSetupParameter))
            {
               RetriesLeft = MaxRetries;
               do
                  try
                  {
                     DisposableObjects.Add(Transaction = DBConnection.BeginTransaction(IsolationLevel.Serializable));
                     // DB-Objekte erstellen
                     DisposableObjects.Add(Command = DBConnection.CreateCommand());
                     Command.CommandText = String.Format(Program.InvariantCulture, @"CREATE SEQUENCE {0}InfoNameSeq""
  INCREMENT 1
  MINVALUE 0
  MAXVALUE 2147483647
  START 0
  CACHE 1;

CREATE SEQUENCE {0}NodeSeq""
  INCREMENT 1
  MINVALUE 0
  MAXVALUE 2147483647
  START 0
  CACHE 1;

CREATE TABLE {0}InfoName""
(
  ""Id"" integer NOT NULL DEFAULT nextval(('{0}InfoNameSeq""')),
  ""Data"" character varying NOT NULL,
  CONSTRAINT {0}InfoNamePK"" PRIMARY KEY (""Id""),
  CONSTRAINT {0}InfoNameDataU"" UNIQUE (""Data"")
);

CREATE TABLE {0}Node""
(
  ""Id"" integer NOT NULL DEFAULT nextval(('{0}NodeSeq""')),
  ""IPv4"" character varying NOT NULL,
  ""LastSeen"" timestamp without time zone NOT NULL DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'UTC'),
  ""DataUpdate"" timestamp without time zone DEFAULT (CURRENT_TIMESTAMP AT TIME ZONE 'UTC'),
  ""TransientHna"" character varying,
  ""TransientRate"" character varying,
  CONSTRAINT {0}NodePK"" PRIMARY KEY (""Id""),
  CONSTRAINT {0}NodeIPv4U"" UNIQUE (""IPv4"")
);

CREATE TABLE {0}InfoValue""
(
  ""NameId"" integer NOT NULL,
  ""NodeId"" integer NOT NULL,
  ""Step"" integer NOT NULL,
  ""Data"" character varying NOT NULL,
  ""Valid"" boolean,
  CONSTRAINT {0}InfoValuePK"" PRIMARY KEY (""NameId"", ""NodeId"", ""Step""),
  CONSTRAINT {0}InfoValueNameFK"" FOREIGN KEY (""NameId"")
      REFERENCES {0}InfoName"" (""Id"") MATCH SIMPLE
      ON UPDATE RESTRICT ON DELETE RESTRICT,
  CONSTRAINT {0}InfoValueNodeFK"" FOREIGN KEY (""NodeId"")
      REFERENCES {0}Node"" (""Id"") MATCH SIMPLE
      ON UPDATE RESTRICT ON DELETE CASCADE
);

CREATE INDEX {0}InfoValueNameFKI""
  ON {0}InfoValue""
  (""NameId"");

CREATE INDEX {0}InfoValueNodeFKI""
  ON {0}InfoValue""
  (""NodeId"");

CREATE VIEW {0}geotrack"" AS
  SELECT ""IPv4"" AS ipv4, cast(extract(epoch from ""DataUpdate"") as integer) AS mtime, cast(null as integer) AS stime, hostname, cast(latitude as double precision), cast(longitude as double precision), cast(cast(llaccuracy as double precision) as integer), nick, name, mail, tele, addr, note, ""TransientHna"" AS hna, cast(channel as integer), cast((boardtype || ',' || boardnum) as character varying) AS board, version, cast(cast(""TransientRate"" as double precision) as integer) AS rate
  FROM ((((((((((((({0}Node"" AS ""Node""
  LEFT OUTER JOIN ( SELECT ""NodeId"", ""Data"" AS latitude
    FROM {0}InfoValue""
    WHERE ""NameId"" = (( SELECT ""Id""
      FROM {0}InfoName""
      WHERE ""Data"" = 'Latitude')) AND ""Step"" = 1) ""Data"" ON ""Node"".""Id"" = ""Data"".""NodeId"") ""Node""
  LEFT OUTER JOIN ( SELECT ""NodeId"", ""Data"" AS longitude
    FROM {0}InfoValue""
    WHERE ""NameId"" = (( SELECT ""Id""
      FROM {0}InfoName""
      WHERE ""Data"" = 'Longitude')) AND ""Step"" = 1) ""Data"" ON ""Node"".""Id"" = ""Data"".""NodeId"") ""Node""
  LEFT OUTER JOIN ( SELECT ""NodeId"", ""Data"" AS tele
    FROM {0}InfoValue""
    WHERE ""NameId"" = (( SELECT ""Id""
      FROM {0}InfoName""
      WHERE ""Data"" = 'ff_adm_tel')) AND ""Step"" = 0) ""Data"" ON ""Node"".""Id"" = ""Data"".""NodeId"") ""Node""
  LEFT OUTER JOIN ( SELECT ""NodeId"", ""Data"" AS mail
    FROM {0}InfoValue""
    WHERE ""NameId"" = (( SELECT ""Id""
      FROM {0}InfoName""
      WHERE ""Data"" = 'ff_adm_mail')) AND ""Step"" = 0) ""Data"" ON ""Node"".""Id"" = ""Data"".""NodeId"") ""Node""
  LEFT OUTER JOIN ( SELECT ""NodeId"", ""Data"" AS name
    FROM {0}InfoValue""
    WHERE ""NameId"" = (( SELECT ""Id""
      FROM {0}InfoName""
      WHERE ""Data"" = 'ff_adm_name')) AND ""Step"" = 0) ""Data"" ON ""Node"".""Id"" = ""Data"".""NodeId"") ""Node""
  LEFT OUTER JOIN ( SELECT ""NodeId"", ""Data"" AS nick
    FROM {0}InfoValue""
    WHERE ""NameId"" = (( SELECT ""Id""
      FROM {0}InfoName""
      WHERE ""Data"" = 'ff_adm_nick')) AND ""Step"" = 0) ""Data"" ON ""Node"".""Id"" = ""Data"".""NodeId"") ""Node""
  LEFT OUTER JOIN ( SELECT ""NodeId"", ""Data"" AS addr
    FROM {0}InfoValue""
    WHERE ""NameId"" = (( SELECT ""Id""
      FROM {0}InfoName""
      WHERE ""Data"" = 'ff_adm_loc')) AND ""Step"" = 0) ""Data"" ON ""Node"".""Id"" = ""Data"".""NodeId"") ""Node""
  LEFT OUTER JOIN ( SELECT ""NodeId"", ""Data"" AS note
    FROM {0}InfoValue""
    WHERE ""NameId"" = (( SELECT ""Id""
      FROM {0}InfoName""
      WHERE ""Data"" = 'ff_adm_note')) AND ""Step"" = 0) ""Data"" ON ""Node"".""Id"" = ""Data"".""NodeId"") ""Node""
  LEFT OUTER JOIN ( SELECT ""NodeId"", ""Data"" AS version
    FROM {0}InfoValue""
    WHERE ""NameId"" = (( SELECT ""Id""
      FROM {0}InfoName""
      WHERE ""Data"" = 'ff_release')) AND ""Step"" = 0) ""Data"" ON ""Node"".""Id"" = ""Data"".""NodeId"") ""Node""
  LEFT OUTER JOIN ( SELECT ""NodeId"", ""Data"" AS channel
    FROM {0}InfoValue""
    WHERE ""NameId"" = (( SELECT ""Id""
      FROM {0}InfoName""
      WHERE ""Data"" = 'wl0_channel')) AND ""Step"" = 0) ""Data"" ON ""Node"".""Id"" = ""Data"".""NodeId"") ""Node""
  LEFT OUTER JOIN ( SELECT ""NodeId"", ""Data"" AS boardtype
    FROM {0}InfoValue""
    WHERE ""NameId"" = (( SELECT ""Id""
      FROM {0}InfoName""
      WHERE ""Data"" = 'boardtype')) AND ""Step"" = 0) ""Data"" ON ""Node"".""Id"" = ""Data"".""NodeId"") ""Node""
  LEFT OUTER JOIN ( SELECT ""NodeId"", ""Data"" AS boardnum
    FROM {0}InfoValue""
    WHERE ""NameId"" = (( SELECT ""Id""
      FROM {0}InfoName""
      WHERE ""Data"" = 'boardnum')) AND ""Step"" = 0) ""Data"" ON ""Node"".""Id"" = ""Data"".""NodeId"") ""Node""
  LEFT OUTER JOIN ( SELECT ""NodeId"", ""Data"" AS llaccuracy
    FROM {0}InfoValue""
    WHERE ""NameId"" = (( SELECT ""Id""
      FROM {0}InfoName""
      WHERE ""Data"" = 'LatLongAccuracy')) AND ""Step"" = 1) ""Data"" ON ""Node"".""Id"" = ""Data"".""NodeId"") ""Node""
  LEFT OUTER JOIN ( SELECT ""NodeId"", ""Data"" AS hostname
    FROM {0}InfoValue""
    WHERE ""NameId"" = (( SELECT ""Id""
      FROM {0}InfoName""
      WHERE ""Data"" = 'wan_hostname')) AND ""Step"" = 0) ""Data"" ON ""Node"".""Id"" = ""Data"".""NodeId"";", Prefix);
                     Command.ExecuteNonQuery();
                     // Transaktion übernehmen und nicht noch einmal ausführen
                     Transaction.Commit();
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
               // Programm beenden
               return;
            }
            // weitere Einstellunen laden
            Program.GpsSorting = (int)Settings.GetValue("InfoGPSOrder", typeof(int));
            ZVar1 = (int)Settings.GetValue("BotInfoCollectorThreads", typeof(int));
            // Austauschobjekt für Kommunikation zwischen Threads erstellen
            int NodesLeft;
            ThreadCommunicator Communicator;
            lock (Communicator = new ThreadCommunicator())
               NodesLeft = (Communicator.NodeIPs = new Freifunk.EtxGraph(Settings, double.PositiveInfinity, new string[] { "" }, new string[0]).GetNodeIps()).Count;
            // Threads starten
            do
               new System.Threading.Thread(NodeDataCollector).Start(Communicator);
            while (--ZVar1 > 0);
            // Verarbeitungsfunktionen für einige NVRAM-Daten setzen
            Dictionary<string, DataProcessor> DataProcessors;
            (DataProcessors = new Dictionary<string, DataProcessor>(8)).Add("ff_adm_loc", HtmlUnescapeProcessor);
            DataProcessors.Add("ff_adm_nick", HtmlUnescapeProcessor);
            DataProcessors.Add("ff_adm_tel", HtmlUnescapeProcessor);
            DataProcessors.Add("ff_adm_note", HtmlUnescapeProcessor);
            DataProcessors.Add("ff_adm_name", HtmlUnescapeProcessor);
            DataProcessors.Add("ff_adm_latlon", GpsProcessor);
            DataProcessors.Add("ff_adm_neturl", UriProcessor);
            DataProcessors.Add("ff_adm_mail", EMailAddressProcessor);
            // Variablen für die NVRAM-Daten initialisieren
            Dictionary<string, int> DataNameIds = new Dictionary<string, int>(200);
            List<string> NewDataNames = new List<string>(200);
            List<Dictionary<string, string>> Nvram;
            (Nvram = new List<Dictionary<string, string>>(2)).Add(null);
            // solange noch Knoten unverarbeitet sind
            IDbDataParameter Parameter;
            while (NodesLeft > 0)
            {
               // Knoten-Daten holen
               NodeData NodeData;
               string NodeIP;
               while (true)
               {
                  lock (Communicator)
                     if (Communicator.Output.Count > 0)
                     {
                        NodeIP = Communicator.Output.Peek().Key;
                        NodesLeft -= 1;
                        // wenn Daten vorhanden, klonen und weitermachen
                        if ((NodeData = Communicator.Output.Dequeue().Value) != null)
                        {
                           NodeData = NodeData.Clone();
                           break;
                        }
                     }
                  System.Threading.Thread.Sleep(1);
               }
               Nvram[0] = NodeData.NvramData;
               RetriesLeft = MaxRetries;
               do
                  try
                  {
#if DEBUG
                     System.Diagnostics.Stopwatch X = System.Diagnostics.Stopwatch.StartNew();
#endif
                     DisposableObjects.Add(Transaction = DBConnection.BeginTransaction(IsolationLevel.Serializable));
                     // Knotendaten updaten/eintragen
                     DisposableObjects.Add(Command = DBConnection.CreateCommand());
                     Command.Parameters.Add(Parameter = Command.CreateParameter());
                     Parameter.DbType = DbType.String;
                     Parameter.ParameterName = "@IP";
                     Parameter.Value = NodeIP;
                     // wenn Fehler beim Download aufgetreten ist
                     if (NodeData.NvramData == null)
                     {
                        // Sichtung eintragen
                        Command.CommandText = @"update " + Prefix + @"Node"" set ""LastSeen"" = default where ""IPv4"" = @IP; insert into " + Prefix + @"Node"" (""IPv4"", ""DataUpdate"") (select @IP, null from (select @IP except select ""IPv4"" from " + Prefix + @"Node"") ""Dummy"")";
                        Command.ExecuteNonQuery();
                     }
                     else
                     {
                        // alte Werte für diesen Knoten löschen; letzte Sichtung und transiente Daten aktualisieren, wenn vorhanden; einfügen, wenn nicht vorhanden; Id ermitteln
                        Command.CommandText = @"delete from " + Prefix + @"InfoValue"" where ""NodeId"" in (select ""Id"" from " + Prefix + @"Node"" where ""IPv4"" = @IP); update " + Prefix + @"Node"" set ""LastSeen"" = default, ""DataUpdate"" = default, ""TransientRate"" = @Rate, ""TransientHna"" = @Hna where ""IPv4"" = @IP; insert into " + Prefix + @"Node"" (""IPv4"", ""TransientHna"", ""TransientRate"") (select @IP, @Hna, @Rate from (select @IP except select ""IPv4"" from " + Prefix + @"Node"") ""Dummy""); select ""Id"" from " + Prefix + @"Node"" where ""IPv4"" = @IP";
                        Command.Parameters.Add(Parameter = Command.CreateParameter());
                        Parameter.DbType = DbType.String;
                        Parameter.ParameterName = "@Rate";
                        Parameter.Value = NodeData.Rate;
                        Command.Parameters.Add(Parameter = Command.CreateParameter());
                        Parameter.DbType = DbType.String;
                        Parameter.ParameterName = "@Hna";
                        Parameter.Value = NodeData.Hna;
                        int NodeId = (int)Command.ExecuteScalar();
                        // Knotendaten speichern
                        if (NodeData.NvramData.Count > 0)
                        {
                           IDataReader Reader;
                           // IDs der bereits gespeicherten Infonamen ermitteln
                           DisposableObjects.Add(Command = DBConnection.CreateCommand());
                           Command.CommandText = @"select ""Id"", ""Data"" from " + Prefix + @"InfoName""";
                           DisposableObjects.Add(Reader = Command.ExecuteReader());
                           while (Reader.Read())
                              DataNameIds.Add(Reader.GetString(1), Reader.GetInt32(0));
                           for (ZVar1 = 0; ZVar1 < Nvram.Count && Nvram[ZVar1].Count > 0; ZVar1++)
                           {
                              // noch nicht gespeicherte Infonamen ermitteln
                              foreach (string DataName in Nvram[ZVar1].Keys)
                                 if (!DataNameIds.ContainsKey(DataName))
                                    NewDataNames.Add(DataName);
                              // diese Namen eintragen und deren IDs ermitteln
                              StringBuilder CommandText;
                              if (NewDataNames.Count > 0)
                              {
                                 DisposableObjects.Add(Command = DBConnection.CreateCommand());
                                 CommandText = new StringBuilder(@"insert into " + Prefix + @"InfoName"" (""Data"") values ");
                                 int ZVar2;
                                 for (ZVar2 = 0; ZVar2 < NewDataNames.Count; ZVar2++)
                                 {
                                    if (ZVar2 > 0)
                                       CommandText.Append(", ");
                                    Command.Parameters.Add(Parameter = Command.CreateParameter());
                                    CommandText.Append("(" + (Parameter.ParameterName = string.Format(Program.InvariantCulture, "@N{0}", ZVar2)) + ")");
                                    Parameter.DbType = DbType.String;
                                    Parameter.Value = NewDataNames[ZVar2];
                                 }
                                 CommandText.Append(@"; select ""Id"", ""Data"" from " + Prefix + @"InfoName"" where ""Data"" in (");
                                 for (ZVar2 = 0; ZVar2 < NewDataNames.Count; ZVar2++)
                                 {
                                    if (ZVar2 > 0)
                                       CommandText.Append(", ");
                                    CommandText.Append(string.Format(Program.InvariantCulture, "@N{0}", ZVar2));
                                 }
                                 CommandText.Append(")");
                                 Command.CommandText = CommandText.ToString();
                                 DisposableObjects.Add(Reader = Command.ExecuteReader());
                                 while (Reader.Read())
                                    DataNameIds.Add(Reader.GetString(1), Reader.GetInt32(0));
                                 NewDataNames.Clear();
                              }
                              // Werte eintragen
                              DisposableObjects.Add(Command = DBConnection.CreateCommand());
                              CommandText = new StringBuilder(@"insert into " + Prefix + @"InfoValue"" (""NameId"", ""NodeId"", ""Step"", ""Data"", ""Valid"") values ");
                              Parameter = null;
                              foreach (KeyValuePair<string, string> Data in Nvram[ZVar1])
                              {
                                 if (Parameter != null)
                                    CommandText.Append(", ");
                                 string NameIdStr;
                                 Command.Parameters.Add(Parameter = Command.CreateParameter());
                                 CommandText.Append("(" + (Parameter.ParameterName = "@N" + (NameIdStr = ((IConvertible)(Parameter.Value = DataNameIds[Data.Key])).ToString(Program.InvariantCulture))));
                                 Parameter.DbType = DbType.Int32;
                                 Command.Parameters.Add(Parameter = Command.CreateParameter());
                                 CommandText.Append(", " + (Parameter.ParameterName = "@K" + NameIdStr));
                                 Parameter.DbType = DbType.Int32;
                                 Parameter.Value = NodeId;
                                 Command.Parameters.Add(Parameter = Command.CreateParameter());
                                 CommandText.Append(", " + (Parameter.ParameterName = "@S" + NameIdStr));
                                 Parameter.DbType = DbType.Int32;
                                 Parameter.Value = ZVar1;
                                 Command.Parameters.Add(Parameter = Command.CreateParameter());
                                 CommandText.Append(", " + (Parameter.ParameterName = "@D" + NameIdStr));
                                 Parameter.DbType = DbType.String;
                                 // Daten verarbeiten; Gültigkeit testen, umformen und Daten für nächsten Schritt extrahieren
                                 DataProcessor Processor;
                                 bool? Valid = ZVar1 == 0 ? (bool?)null : true;
                                 if (DataProcessors.TryGetValue(Data.Key, out Processor))
                                 {
                                    Dictionary<string, string> NextStepData;
                                    Parameter.Value = Processor(Data.Key, ZVar1, Data.Value, ref Valid, out NextStepData);
                                    if (NextStepData != null && NextStepData.Count > 0)
                                    {
                                       if (Nvram.Count - 1 == ZVar1)
                                          Nvram.Add(new Dictionary<string, string>(10));
                                       foreach (KeyValuePair<string, string> NextData in NextStepData)
                                          Nvram[ZVar1 + 1].Add(NextData.Key, NextData.Value);
                                    }
                                 }
                                 else
                                    Parameter.Value = Data.Value;
                                 Command.Parameters.Add(Parameter = Command.CreateParameter());
                                 CommandText.Append(", " + (Parameter.ParameterName = "@V" + NameIdStr) + ")");
                                 Parameter.DbType = DbType.Boolean;
                                 Parameter.Value = Valid;
                              }
                              Command.CommandText = CommandText.ToString();
                              Command.ExecuteNonQuery();
                           }
                        }
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
                  }
                  finally
                  {
                     // beim Verlassen alle freigebbaren Objekte freigeben
                     for (ZVar1 = 0; ZVar1 < DisposableObjects.Count; ZVar1++)
                        DisposableObjects[ZVar1].Dispose();
                     DisposableObjects.Clear();
                     // alle extrahierten Daten, die noch nicht in der DB gespeicherten Datennamen und die IDs der in der DB gespeicherten Datennamen löschen
                     for (ZVar1 = 1; ZVar1 < Nvram.Count; ZVar1++)
                        Nvram[ZVar1].Clear();
                     NewDataNames.Clear();
                     DataNameIds.Clear();
                  }
               // Transaktion wiederholen, wenn nötig
               while (RetriesLeft-- > 0);
            }
            RetriesLeft = MaxRetries;
            do
               try
               {
                  DisposableObjects.Add(Transaction = DBConnection.BeginTransaction(IsolationLevel.Serializable));
                  // verfallene Daten löschen
                  DisposableObjects.Add(Command = DBConnection.CreateCommand());
                  Command.CommandText = @"delete from " + Prefix + @"Node"" where ""LastSeen"" < (CURRENT_TIMESTAMP - cast(cast(@Interval as character varying) || ' day' as interval))";
                  Command.Parameters.Add(Parameter = Command.CreateParameter());
                  Parameter.DbType = DbType.Double;
                  Parameter.ParameterName = "@Interval";
                  Parameter.Value = TimeSpan.Parse((string)Settings.GetValue("InfoExpirationPeriod", typeof(string))).TotalDays;
                  Command.ExecuteNonQuery();
                  // Transaktion übernehmen und nicht noch einmal ausführen
                  Transaction.Commit();
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
      }

      // ermittelt ob eine Zeichenkette schreibungsinvariant "setup" ist
      private static bool MatchSetupParameter(string arg)
      {
         return arg.Equals("setup", StringComparison.OrdinalIgnoreCase);
      }

      // wandelt HTML-Entitys eines Wertes in Zeichen um
      private static string HtmlUnescapeProcessor(string name, int step, string value, ref bool? valid, out Dictionary<string, string> extractedData)
      {
         extractedData = null;
         // nur auf die Originaldaten des NVRAM anwenden
         if (step == 0)
            return HttpUtility.HtmlDecode(value);
         return value;
      }

      // überprüft, ob eine E-Mail-Adresse gültig ist
      private static string EMailAddressProcessor(string name, int step, string value, ref bool? valid, out Dictionary<string, string> extractedData)
      {
         extractedData = null;
         // Gültigkeit prüfen
         if ((valid = value.Length >= 0 && value.IndexOf('@') >= 0 && value.IndexOf('.') >= 0).Value)
            try
            {
               new MailAddress(value);
            }
            catch (FormatException)
            {
               valid = false;
            }
         return value;
      }

      // überprüft, ob eine URI gültig und absolut ist
      private static string UriProcessor(string name, int step, string value, ref bool? valid, out Dictionary<string, string> extractedData)
      {
         extractedData = null;
         // Gültigkeit prüfen
         Uri Dummy;
         valid = Uri.TryCreate(value, UriKind.Absolute, out Dummy);
         return value;
      }

      // überprüft, ob GPS-Koordinaten gültig sind, und extrahiert sie und deren Genauigkeit
      private static string GpsProcessor(string name, int step, string value, ref bool? valid, out Dictionary<string, string> extractedData)
      {
         string[] Coords;
         Decimal Latitude;
         Decimal Longitude;
         // einfache Prüfung
         if ((Coords = value.Split(Program.GpsSeperationChars[2])).Length != 2)
            if ((Coords = value.Split(Program.GpsSeperationChars[1])).Length != 2)
               goto Invalid;
         if(!Decimal.TryParse(Coords[0], NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingWhite, Program.InvariantCulture, out Latitude) || !Decimal.TryParse(Coords[1], NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingWhite, Program.InvariantCulture, out Longitude))
            goto Invalid;
         // strikte Prüfung
         Decimal Dummy;
         valid = (Coords = value.Split(Program.GpsSeperationChars[0])).Length == 2 && Decimal.TryParse(Coords[0], NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, Program.InvariantCulture, out Dummy) && Decimal.TryParse(Coords[1], NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, Program.InvariantCulture, out Dummy);
         // sortieren, wenn nötig
         if ((Program.GpsSorting >= 1 && Latitude < Longitude) || (Program.GpsSorting <= -1 && Latitude > Longitude))
         {
            Dummy = Longitude;
            Longitude = Latitude;
            Latitude = Dummy;
            valid = false;
         }
         // wenn Koordinaten in gültigem Wertebereich
         if (Latitude >= -90M && Latitude <= 90M && Longitude >= -180M && Longitude <= 180M)
         {
            // extrahierte Daten speichern
            extractedData = new Dictionary<string, string>(3);
            (extractedData = new Dictionary<string, string>(3)).Add("Latitude", Latitude.ToString(Program.InvariantCulture));
            extractedData.Add("Longitude", Longitude.ToString(Program.InvariantCulture));
            // Genauigkeit der Breite anhand der Anzahl der Nachkommastellen
            // Genauigkeit der Länge bezieht die Breite mit ein
            extractedData.Add("LatLongAccuracy", Math.Min((double)((Decimal.GetBits(Latitude)[3] >> 16) & 0xFF), ((double)((Decimal.GetBits(Longitude)[3] >> 16) & 0xFF)) - Math.Log10(Math.Cos(((double)Latitude) / 180D * Math.PI))).ToString(Program.InvariantCulture));
            return value;
         }
      Invalid:
         extractedData = null;
         valid = false;
         return value;

         //if (Coords.Length == 2)
         //{
         //   Decimal Latitude;
         //   if (Decimal.TryParse(Coords[0], NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingWhite, Program.InvariantCulture, out Latitude))
         //   {
         //      Decimal Longitude;
         //      if (Decimal.TryParse(Coords[1], NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign | NumberStyles.AllowThousands | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingWhite, Program.InvariantCulture, out Longitude))
         //      {
         //         // Koordinaten tauschen, falls nötig
         //         Decimal Dummy;
         //         if ((Program.GpsSorting >= 1 && Latitude < Longitude) || (Program.GpsSorting <= -1 && Latitude > Longitude))
         //         {
         //            Dummy = Longitude;
         //            Longitude = Latitude;
         //            Latitude = Dummy;
         //            valid = false;
         //         }
         //         else
         //            // striktere Prüfung für ursprüngliche Daten durchführen
         //            valid = Decimal.TryParse(Coords[0], NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, Program.InvariantCulture, out Dummy) && Decimal.TryParse(Coords[1], NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, Program.InvariantCulture, out Dummy);
         //         // wenn Koordinaten in gültigem Wertebereich
         //         if (Latitude >= -90M && Latitude <= 90M && Longitude >= -180M && Longitude <= 180M)
         //         {
         //            // extrahierte Daten speichern
         //            (extractedData = new Dictionary<string, string>(3)).Add("Latitude", Latitude.ToString(Program.InvariantCulture));
         //            extractedData.Add("Longitude", Longitude.ToString(Program.InvariantCulture));
         //            // Genauigkeit der Breite anhand der Anzahl der Nachkommastellen
         //            // Genauigkeit der Länge bezieht die Breite mit ein
         //            extractedData.Add("LatLongAccuracy", Math.Min((double)((Decimal.GetBits(Latitude)[3] >> 16) & 0xFF), ((double)((Decimal.GetBits(Longitude)[3] >> 16) & 0xFF)) - Math.Log10(Math.Cos(((double)Latitude) / 180D * Math.PI))).ToString(Program.InvariantCulture));
         //            return value;
         //         }
         //      }
         //   }
         //}
         //extractedData = null;
         //valid = false;
         //return value;
      }

      // stellt ein Objekt zur Kommunikation der Datensammel-Threads dar
      private class ThreadCommunicator
      {
         private Queue<string> input;
         private Queue<KeyValuePair<string, NodeData>> output;
         private Dictionary<string, object> nodeIPs;

         public ThreadCommunicator()
         {
         }

         // die zu verarbeitenden IP-Adressen
         public Queue<string> Input
         {
            get
            {
               return input;
            }
         }

         // Paare aus den verarbeiteten IP-Adressen und den zugehörigen Daten
         public Queue<KeyValuePair<string, NodeData>> Output
         {
            get
            {
               return output;
            }
         }

         // die IP-Adressen
         public Dictionary<string, object> NodeIPs
         {
            get
            {
               return nodeIPs;
            }
            set
            {
               nodeIPs = value;
               this.input = new Queue<string>(value.Keys);
               this.output = new Queue<KeyValuePair<string, NodeData>>(value.Count);
            }
         }
      }

      // stellt die Daten eines Knotens dar, die ein Thread ermittelt hat
      private class NodeData
      {
         private Dictionary<string, string> nvramData;
         private string rate;
         private string hna;

         public NodeData()
         {
         }

         // die NVRAM-Daten
         public Dictionary<string, string> NvramData
         {
            get
            {
               return nvramData;
            }
            set
            {
               nvramData = value;
            }
         }

         // die WLAN-Datenrate
         public string Rate
         {
            get
            {
               return rate;
            }
            set
            {
               rate = value;
            }
         }

         // die Standardroute
         public string Hna
         {
            get
            {
               return hna;
            }
            set
            {
               hna = value;
            }
         }

         // erstellt eine tiefe Kopie der Daten
         public NodeData Clone()
         {
            NodeData RetVal = new NodeData();
            RetVal.hna = this.hna;
            RetVal.rate = this.rate;
            if (this.nvramData != null)
               RetVal.nvramData = new Dictionary<string, string>(this.nvramData);
            return RetVal;
         }

         // initialisiert das Objekt mit Standardwerten
         public void Initialize()
         {
            this.hna = null;
            this.rate = null;
            if (this.nvramData == null)
               this.nvramData = new Dictionary<string, string>(200);
            else
               this.nvramData.Clear();
         }
      }

      private static void NodeDataCollector(object communicator)
      {
         ThreadCommunicator Communicator = (ThreadCommunicator)communicator;
         // Trennzeichen für Daten aus BotInfo
         char[] InfoSeperationChars = { ' ', '\t' };
         // IP-Adressen übertragen
         Dictionary<string, object> NodeIPs;
         lock (Communicator)
            NodeIPs = new Dictionary<string, object>(Communicator.NodeIPs);
         // Einstellungen laden
         AppSettingsReader Settings = new AppSettingsReader();
         int Timeout = (int)Settings.GetValue("BotInfoTimeout", typeof(int));
         Freifunk.EncodingTools EncodingTools = new Freifunk.EncodingTools(Settings);
         StreamReader WebResponseReader = null;
         WebResponse WebResponse = null;
         NodeData NodeData = null;
         // IP-Adressen verarbeiten
         while (true)
         {
            // IP-Adresse holen
            string NodeIP;
            lock (Communicator)
            {
               if (Communicator.Input.Count > 0)
                  NodeIP = Communicator.Input.Dequeue();
               else
                  break;
            }
            (NodeData ?? (NodeData = new NodeData())).Initialize();
#if DEBUG
            System.Diagnostics.Stopwatch Y = System.Diagnostics.Stopwatch.StartNew();
#endif
            // BotInfo-Web-Anfrage erstellen und versuchen, Antwort zu bekommen
            try
            {
#if MONOTRACE
               Console.WriteLine(NodeIP + " 1");
#endif
               WebResponse = Freifunk.NetTools.GetWebResponse("http://" + NodeIP + "/cgi-bin-botinfo.txt?cat=nvram,wlan,routes", Timeout);
#if MONOTRACE
               Console.WriteLine(NodeIP + " 2");
#endif
            }
            catch (WebException ex)
            {
#if MONOTRACE
               Console.WriteLine(NodeIP + " 3");
#endif
               // wenn keine Fehler-Antwort vorhanden, alles leeren
               if (ex.Response == null)
                  NodeData = null;
               // sonst NVRAM-Daten leeren und mit Fehlerantwort weitermachen
               else
               {
                  NodeData.NvramData = null;
                  WebResponse = ex.Response;
               }
            }
#if MONOTRACE
            Console.WriteLine(NodeIP + " 4");
#endif
            if (NodeData != null)
            {
               try
               {
                  if (NodeData.NvramData != null)
                  {
                     // passende Kodierung für Web-Antwort ermitteln
                     WebResponseReader = EncodingTools.GetReader(WebResponse, "BotInfo");
                     // solange Daten vorhanden sind, Antwort zeilenweise als DEA auswerten
                     int State = 0;
                     string TextLine;
                     int SepIndex;
                     while ((TextLine = WebResponseReader.ReadLine()) != null)
                        // Ende einer Kategorie
                        if (TextLine == "}")
                           State = 0;
                        // NVRAM-Kategorie gefunden
                        else if (State == 0 && TextLine == "nvram{")
                           State = 1;
                        // WLAN-Datenrate-Kategorie gefunden
                        else if (State == 0 && TextLine == "wlan_rate{")
                           State = 2;
                        // Routen-Kategorie gefunden
                        else if (State == 0 && TextLine == "routes{")
                           State = 3;
                        // Zeile aus NVRAM lesen
                        else if (State == 1)
                        {
                           if ((SepIndex = (TextLine = TextLine.TrimStart(InfoSeperationChars)).IndexOf('=')) < 0)
                              continue;
                           NodeData.NvramData[TextLine.Substring(0, SepIndex)] = TextLine.Substring(SepIndex + 1);
                        }
                        // WLAN-Datenrate lesen
                        else if (State == 2 && TextLine.StartsWith("\trate is "))
                        {
                           if ((SepIndex = TextLine.IndexOf(' ', 9)) < 0)
                              continue;
                           NodeData.Rate = TextLine.Substring(9, SepIndex - 9);
                        }
                        // Standardroute lesen
                        else if (State == 3 && TextLine.StartsWith("\tdefault via "))
                        {
                           if ((SepIndex = TextLine.IndexOf(' ', 13)) < 0)
                              continue;
                           if (!NodeIPs.ContainsKey(NodeData.Hna = TextLine.Substring(13, SepIndex - 13)))
                              NodeData.Hna = null;
                        }
                  }
               }
               catch (WebException)
               {
#if MONOTRACE
                  Console.WriteLine(NodeIP + " 5");
#endif
                  // wenn nicht vollständig heruntergeladen, alle NVRAM-Daten löschen
                  NodeData.NvramData = null;
               }
               finally
               {
                  // beim Verlassen Antwortstrom und Antwort freigeben
                  if (WebResponseReader != null)
                  {
                     WebResponseReader.Close();
                     WebResponseReader = null;
                  }
                  WebResponse.Close();
#if DEBUG
                  Y.Stop();
                  System.Diagnostics.Debug.Print("DL " + Y.Elapsed.ToString());
#endif
               }
               // Daten zur Verfügung stellen
               lock (Communicator)
                  Communicator.Output.Enqueue(new KeyValuePair<string, NodeData>(NodeIP, NodeData.Clone()));
            }
            // leere Daten zur Verfügung stellen
            else
               lock (Communicator)
                  Communicator.Output.Enqueue(new KeyValuePair<string, NodeData>(NodeIP, null));
         }
      }
   }

   // extractedData enthält Daten für nächsten Schritt; valid kann in der Prozedur angepasst werden um Gültigkeitsprüfung widerzuspiegeln; Rückgabeparameter ist umgeformter Wert oder Null, wenn er nicht umgeformt wurde
   internal delegate string DataProcessor(string name, int step, string value, ref bool? valid, out Dictionary<string, string> extractedData);
}

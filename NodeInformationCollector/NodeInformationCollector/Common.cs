// Lizenz: Creative Commons by-nc-sa, http://creativecommons.org/licenses/by-nc-sa/3.0/deed.de
// Urheber: Matthias Schäfer, 06128 Halle (Saale), Germany

using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Text;
using System.IO;
using System;
using System.Net;
using System.Net.Mime;

// Achtung, sämtliche Member der Klassen in diesem Namespace, die auf andere Daten zurückgreifen, sind NICHT static, damit die Klassen sie in Multithread-Szenarien bei jeder Instanz erneut initialisieren
namespace Freifunk
{
   internal class EtxGraph
   {
      // die Adressen der potenziellen HNAs (sind nur HNAs, wenn sie auch in der Verbindungsliste auftauchen)
      private Dictionary<string, object> HnaIps;
      // Adjazenzliste der OLSR-Knoten mit LQ- und NLQ-Werten
      private Dictionary<string, Dictionary<string, KeyValuePair<double, double>>> AdjacenceList;
      // Zusammenhangskomponenten der VPNs
      private List<Dictionary<string, object>> VpnAdjacenceList;
      // hält die invariante Kultur für diverse Parsings und Formatierungen bereit
      private CultureInfo InvariantCulture;

      public EtxGraph(AppSettingsReader settings, double maxEtx, string[] ipAdressPrefixes, string[] vpnConcentrators)
      {
         // Trennzeichen für Daten in TxtInfo
         char[] InfoSeperationChars = { ' ', '\t' };
         // Einstellungen laden und Variablen initialisieren
         int NodeCount;
         if ((NodeCount = (int)settings.GetValue("TopoGenerousNodeCountEstimation", typeof(int))) > 16777216)
            throw new ArgumentOutOfRangeException();
         HnaIps = new Dictionary<string, object>(NodeCount / 10);
         AdjacenceList = new Dictionary<string, Dictionary<string, KeyValuePair<double, double>>>(NodeCount);
         Dictionary<string, Dictionary<string, object>> VpnConnections = new Dictionary<string, Dictionary<string, object>>(NodeCount / 100 + 1);
         InvariantCulture = CultureInfo.InvariantCulture;
         Dictionary<KeyValuePair<string, string>, double> Lqs = new Dictionary<KeyValuePair<string, string>, double>(NodeCount * 3);
         Dictionary<KeyValuePair<string, string>, double> Nlqs = new Dictionary<KeyValuePair<string, string>, double>(NodeCount * 3);
         Dictionary<KeyValuePair<string, string>, int> LqNlqCount = new Dictionary<KeyValuePair<string, string>, int>(NodeCount * 3);
         for (int ZVar1 = 0; ZVar1 < vpnConcentrators.Length; ZVar1++)
            VpnConnections.Add(vpnConcentrators[ZVar1], new Dictionary<string, object>(NodeCount / 10));
         // TxtInfo-Web-Anfrage stellen und Antwort und dazu passende Codierung ermitteln
         StreamReader WebResponseReader = null;
         int Timeout;
         WebResponse WebResponse = NetTools.GetWebResponse((string)settings.GetValue("TxtInfoURI", typeof(string)), Timeout = (int)settings.GetValue("TxtInfoTimeout", typeof(int)));
         try
         {
            // passende Kodierung für Web-Antwort ermitteln
            WebResponseReader = new EncodingTools(settings).GetReader(WebResponse, "TxtInfo");
            // solange Daten vorhanden sind, Antwort zeilenweise als DEA auswerten
            int State = 0;
            string TextLine;
            string[] Data;
            while ((TextLine = WebResponseReader.ReadLine()) != null)
               // bei Leerzeile DEA zurücksetzen
               if (TextLine.Length == 0)
                  State = 0;
               // Zeile für Link gefunden
               else if ((State & 8) != 0)
               {
                  Data = TextLine.Split(InfoSeperationChars);
                  // Knoten eintragen, wenn sie ein verlangtes Präfix haben
                  bool Flag;
                  if (Flag = EtxGraph.StartsWithAny(Data[0], ipAdressPrefixes))
                     if (!this.AdjacenceList.ContainsKey(Data[0]))
                        this.AdjacenceList.Add(Data[0], new Dictionary<string, KeyValuePair<double, double>>(NodeCount / 20));
                  if (EtxGraph.StartsWithAny(Data[1], ipAdressPrefixes))
                  {
                     if (!this.AdjacenceList.ContainsKey(Data[1]))
                        this.AdjacenceList.Add(Data[1], new Dictionary<string, KeyValuePair<double, double>>(NodeCount / 20));
                  }
                  // nächste Verbindung verarbeiten, wenn einer der Knoten nicht eingetragen wurde
                  else
                     continue;
                  if (!Flag)
                     continue;
                  // LQ und NLQ eintragen
                  Flag = Data[0].CompareTo(Data[1]) > 0;
                  KeyValuePair<string, string> IpPair = new KeyValuePair<string, string>(Data[Flag ? 1 : 0], Data[Flag ? 0 : 1]);
                  if (!Lqs.ContainsKey(IpPair))
                  {
                     Lqs.Add(IpPair, 0D);
                     Nlqs.Add(IpPair, 0D);
                     LqNlqCount.Add(IpPair, 0);
                  }
                  if (State == 9)
                  {
                     Data[3] = Data[2].Substring(Data[2].IndexOf('/') + 1);
                     Data[2] = Data[2].Substring(0, Data[2].Length - Data[3].Length - 1);
                  }
                  Lqs[IpPair] += double.Parse(Data[Flag ? 3 : 2], NumberStyles.AllowDecimalPoint, this.InvariantCulture);
                  Nlqs[IpPair] += double.Parse(Data[Flag ? 2 : 3], NumberStyles.AllowDecimalPoint, this.InvariantCulture);
                  LqNlqCount[IpPair] += 1;
                  // VPN-Verbindungen eintragen
                  Dictionary<string, object> Node1ConnectedComponent;
                  Dictionary<string, object> Node2ConnectedComponent;
                  VpnConnections.TryGetValue(Data[0], out Node1ConnectedComponent);
                  VpnConnections.TryGetValue(Data[1], out Node2ConnectedComponent);
                  if (Node1ConnectedComponent != null)
                  {
                     if (Node2ConnectedComponent == null)
                        Node1ConnectedComponent[Data[1]] = null;
                     else if (Node1ConnectedComponent != Node2ConnectedComponent)
                     {
                        foreach (string ConnectedElement in Node2ConnectedComponent.Keys)
                           Node1ConnectedComponent[ConnectedElement] = null;
                        VpnConnections[Data[1]] = Node1ConnectedComponent;
                     }
                  }
                  else
                     if (Node2ConnectedComponent != null)
                        Node2ConnectedComponent[Data[0]] = null;
               }
               // Zeile für HNA gefunden, Format 2
               else if ((State & 4) != 0)
               {
                  Data = TextLine.Split('\t');
                  if ((State == 4 && (Data[0] == "0.0.0.0/0" && EtxGraph.StartsWithAny(TextLine = Data[1], ipAdressPrefixes))) ||
                      (State == 5 && (Data[0] == "0.0.0.0" && (Data[1] == "0.0.0.0" || Data[1] == "0") && EtxGraph.StartsWithAny(TextLine = Data[2], ipAdressPrefixes))))
                     this.HnaIps[TextLine] = null;
               }
               // Überprüfen des Formats der Link-Tabelle
               else if (State == 1 && (TextLine == "Destination IP\tLast hop IP\tLQ\tILQ\tETX" || TextLine == "Dest. IP\tLast hop IP\tLQ\tNLQ\tCost"))
                  State = 8;
               // Überprüfen des Formats der Link-Tabelle
               else if (State == 1 && TextLine == "Destination IP\tLast hop IP\tLinkcost")
                  State = 9;
               // Überprüfen des Formats der HNA-Tabelle
               else if (State == 3 && TextLine == "Destination\tGateway")
                  State = 4;
               // Überprüfen des Formats der HNA-Tabelle
               else if (State == 3 && TextLine == "Network\tNetmask\tGateway")
                  State = 5;
               // Link-Tabelle gefunden
               else if (TextLine == "Table: Topology")
                  State = 1;
               // wenn HNA-Tabelle gefunden
               else if (TextLine == "Table: HNA")
                  State = 3;
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
         }
         // Zusammenhangskomponenten für Tunnel und LQ- und NLQ-Werte eintragen
         this.VpnAdjacenceList = new List<Dictionary<string, object>>(VpnConnections.Values);
         foreach (KeyValuePair<KeyValuePair<string, string>, double> Lq in Lqs)
         {
            double Value;
            double Nlq = Nlqs[Lq.Key] / (Value = LqNlqCount[Lq.Key]);
            if (1D / Nlq / (Value = Lq.Value / Value) <= maxEtx)
            {
               this.AdjacenceList[Lq.Key.Key].Add(Lq.Key.Value, new KeyValuePair<double, double>(Value, Nlq));
               this.AdjacenceList[Lq.Key.Value].Add(Lq.Key.Key, new KeyValuePair<double, double>(Nlq, Value));
            }
         }
      }

      // erstellt einen neuen Graphen aus einem anderen mit entfernten Knoten
      private EtxGraph(EtxGraph oldGraph, Dictionary<string, Dictionary<string, KeyValuePair<double, double>>> newAdjacenceList, List<Dictionary<string, object>> newVpnAdjacenceList)
      {
         this.AdjacenceList = newAdjacenceList;
         this.VpnAdjacenceList = newVpnAdjacenceList;
         this.InvariantCulture = oldGraph.InvariantCulture;
         this.HnaIps = oldGraph.HnaIps;
      }

      // gibt zurück, ob ein Knoten HNA ist
      public bool IsHna(string ip)
      {
         return this.HnaIps.ContainsKey(ip) && this.AdjacenceList.ContainsKey(ip);
      }

      // gibt eine Liste der IP-Adressen aller Knoten zurück
      public List<string> GetNodeIpList()
      {
         return new List<string>(this.AdjacenceList.Keys);
      }

      // gibt die IP-Adressen aller Knoten zurück
      public Dictionary<string, object> GetNodeIps()
      {
         Dictionary<string, object> RetVal = new Dictionary<string, object>(this.AdjacenceList.Count);
         foreach (string NodeIp in this.AdjacenceList.Keys)
            RetVal.Add(NodeIp, null);
         return RetVal;
      }

      // gibt die Nachbarn eines Knotens zurück
      public Dictionary<string, KeyValuePair<double, double>> GetAdjacentNodes(string ip)
      {
         Dictionary<string, KeyValuePair<double, double>> RetVal;
         if (!this.AdjacenceList.TryGetValue(ip, out RetVal))
            return new Dictionary<string, KeyValuePair<double, double>>();
         return new Dictionary<string, KeyValuePair<double, double>>(RetVal);
      }

      // gibt die IP-Adressen der per Tunnel direkt erreichbaren Knoten eines Knotens zurück
      public List<string> GetTunneledNodeIps(string ip)
      {
         // Teil-VPN mit enthaltenem Knoten suchen
         foreach (Dictionary<string, object> VpnConnection in this.VpnAdjacenceList)
            if (VpnConnection.ContainsKey(ip))
            {
               // Knoten des VPN kopieren und denjenigen, von dem die Suche ausgeht, weglassen
               List<string> RetVal = new List<string>(VpnConnection.Count - 1);
               foreach (string TunneledNodeIp in VpnConnection.Keys)
                  if (ip != TunneledNodeIp)
                     RetVal.Add(TunneledNodeIp);
               return RetVal;
            }
         // Knoten nicht in VPN
         return new List<string>();
      }

      // entfernt bestimmte Knoten und erstellt einen neuen Graphen
      public EtxGraph RemoveNodes(IEnumerable<string> ips)
      {
         Dictionary<string, Dictionary<string, KeyValuePair<double, double>>> AdjacenceList = this.AdjacenceList;
         List<Dictionary<string, object>> VpnAdjacenceList = this.VpnAdjacenceList;
         // jede Adresse durchgehen
         foreach (string ip in ips)
         {
            // Adresse aus Knoten-Adjazenzliste entfernen
            Dictionary<string, KeyValuePair<double, double>> Temp;
            if (AdjacenceList.TryGetValue(ip, out Temp))
            {
               if (AdjacenceList == this.AdjacenceList)
                  AdjacenceList = new Dictionary<string, Dictionary<string, KeyValuePair<double, double>>>(AdjacenceList);
               foreach (string AdjacentNodeIp in Temp.Keys)
               {
                  if ((Temp = AdjacenceList[AdjacentNodeIp]) == this.AdjacenceList[AdjacentNodeIp])
                     Temp = AdjacenceList[AdjacentNodeIp] = new Dictionary<string, KeyValuePair<double, double>>(Temp);
                  Temp.Remove(ip);
               }
               AdjacenceList.Remove(ip);
            }
            // Adresse aus VPN-Tunnelknoten-Liste entfernen
            for (int ZVar2 = 0; ZVar2 < VpnAdjacenceList.Count; ZVar2++)
            {
               Dictionary<string, object> Temp2;
               if (VpnAdjacenceList[ZVar2].ContainsKey(ip))
               {
                  if (VpnAdjacenceList == this.VpnAdjacenceList)
                     VpnAdjacenceList = new List<Dictionary<string, object>>(VpnAdjacenceList);
                  if ((Temp2 = VpnAdjacenceList[ZVar2]) == this.VpnAdjacenceList[ZVar2])
                     Temp2 = VpnAdjacenceList[ZVar2] = new Dictionary<string, object>(Temp2);
                  Temp2.Remove(ip);
               }
            }
         }
         return new EtxGraph(this, AdjacenceList, VpnAdjacenceList);
      }

      // entfernt alle nicht von bestimmten Knoten aus erreichbaren Knoten und erstellt einen neuen Graphen
      public EtxGraph RemoveUnreachableNodes(IEnumerable<string> ips)
      {
         // erreichbare Knoten ermitteln
         Dictionary<string, Dictionary<string, KeyValuePair<double, double>>> UnvisitedNodeIps = new Dictionary<string, Dictionary<string, KeyValuePair<double, double>>>(this.AdjacenceList);
         Stack<string> NodeIpsToVisit = new Stack<string>(ips);
         while (NodeIpsToVisit.Count > 0)
         {
            string Node;
            Dictionary<string, KeyValuePair<double, double>> AdjacentNodes;
            if (UnvisitedNodeIps.TryGetValue(Node = NodeIpsToVisit.Pop(), out AdjacentNodes))
            {
               foreach (string AdjacentNodeIp in AdjacentNodes.Keys)
                  NodeIpsToVisit.Push(AdjacentNodeIp);
               UnvisitedNodeIps.Remove(Node);
            }
         }
         return this.RemoveNodes(UnvisitedNodeIps.Keys);
      }

      // entfernt unverbundene Knoten und erstellt einen neuen Graphen; mode > 1 alle unverbunden, mode = 1 nur unverbundene Nicht-HNAs, mode < 1 keine
      public EtxGraph RemoveUnconnectedNodes(int mode)
      {
         // unverbundene Knoten ermitteln
         List<string> NodesToRemoveIps = new List<string>();
         foreach (KeyValuePair<string, Dictionary<string, KeyValuePair<double, double>>> Node in this.AdjacenceList)
            if (Node.Value.Count == 0 && (mode > 1 || (mode == 1 && !this.HnaIps.ContainsKey(Node.Key))))
               NodesToRemoveIps.Add(Node.Key);
         // Knoten entfernen
         return this.RemoveNodes(NodesToRemoveIps);
      }

      // gibt zurück, ob eine Zeichenkette mit einer anderen aus einer Menge beginnt
      private static bool StartsWithAny(string text, string[] prefixes)
      {
         for (int ZVar1 = 0; ZVar1 < prefixes.Length; ZVar1++)
            if (text.StartsWith(prefixes[ZVar1]))
               return true;
         return false;
      }
   }

   // Funktionen für Netzwerkverbindungen
   internal class NetTools
   {
      // die Elemente einer IP-Adresse, die beim Verkürzen weggelassen werden
      private string[] IpFormatElements;
      // die Elemente einer IP-Adresse, mit denen eine verkürzte aufgefüllt wird
      private string[] IpParseElements;
      // der Punkt, durch den eine IP-Adresse getrennt wird
      private char[] IpPartChars =  { '.' };

      public NetTools(AppSettingsReader settings)
      {
         // Einstellungen laden
         char[] SettingsSeperationChars;
         this.IpFormatElements = ((string)settings.GetValue("InfoIPShortFormat", typeof(string))).Split((SettingsSeperationChars = new char[] { ' ', '\t', '\n', '\r' }), StringSplitOptions.RemoveEmptyEntries);
         this.IpParseElements = ((string)settings.GetValue("InfoIPLongParse", typeof(string))).Split(SettingsSeperationChars, StringSplitOptions.RemoveEmptyEntries);
      }

      // macht einen Prefix einer IP-Adresse kanonisch
      public static string MakeCanonical(string ip)
      {
         // wenn Adresse weniger als 4 Teile hat, sicherstellen, dass am Ende ein Punkt steht
         int Parts;
         if ((Parts = ip.Split('.').Length) < 4 && ip.Length > 0 && ip[ip.Length - 1] != '.')
            ip += ".";
         // wenn Adresse mindestens 4 Teile hat, sicherstellen, dass am Ende kein Punkt steht
         else if (Parts > 4 && ip[ip.Length - 1] == '.')
            ip = ip.Substring(0, ip.Length - 1);
         return ip;
      }

      // verkürzt eine IP-Adresse
      public string FormatToShort(string ip)
      {
         int Pos = 0;
         // Adress-Teile prüfen
         for (int ZVar1 = 0; ZVar1 < this.IpFormatElements.Length; ZVar1++)
         {
            // beim ersten Adress-Teil
            if (ZVar1 == 0)
            {
               // wenn Adresse mit erstem Adress-Teil beginnt
               if (ip.StartsWith(this.IpFormatElements[0]))
               {
                  // Position anpassen und fortfahren
                  Pos = this.IpFormatElements[0].Length;
                  continue;
               }
               break;
            }
            // andere Adress-Teile: wenn Adresse hat einen Punkt gefolgt vom passenden Adress-Teil
            else if (ip.Length - Pos > this.IpFormatElements[ZVar1].Length && ip[Pos] == '.' && ip.Substring(Pos + 1, this.IpFormatElements[ZVar1].Length) == this.IpFormatElements[ZVar1])
            {
               // Position anpassen und fortfahren
               Pos += 1 + this.IpFormatElements[ZVar1].Length;
               continue;
            }
            // sonst abbrechen
            break;
         }
         return ip.Substring(Pos);
      }

      // ermittelt die vollständige Adresse aus einer verkürzten
      public string ParseToLong(string ip)
      {
         // abschließenden Punkt entfernen
         if (ip.EndsWith("."))
            ip = ip.Substring(0, ip.Length - 1);
         // Adress-Teile zählen
         int Count = ip.Split(this.IpPartChars).Length;
         // wenn vor einem führenden Punkt nichts steht oder die Adress-Länge null ist, einen Adress-Teil abziehen
         bool PointAtLeft;
         if (PointAtLeft = (ip.Length == 0 || ip[0] == '.'))
            Count--;
         // wenn weniger als 4 Teile vorhanden und alle nicht vorhanden Teile bekannt
         if (Count < 4 && 3 - Count < this.IpParseElements.Length)
            for (int ZVar1 = 3 - Count; ZVar1 >= 0; ZVar1--)
            {
               // neuen Teil ohne Punkt hinzufügen, wenn bisherige Adresse mit Punkt beginnt, sonst neuen Teil mit Punkt hinzufügen
               if (PointAtLeft)
               {
                  ip = this.IpParseElements[ZVar1] + ip;
                  PointAtLeft = false;
               }
               else
                  ip = this.IpParseElements[ZVar1] + "." + ip;
            }
         return ip;
      }

      // erstellt eine Webantwort zu einer URI mit festgelegtem Timeout
      public static WebResponse GetWebResponse(string uri, int timeout)
      {
         bool TimeoutSet = false;
         try
         {
            WebRequest WebRequest;
            (WebRequest = WebRequest.Create(uri)).Timeout = timeout;
            if (WebRequest is HttpWebRequest)
            {
               ((HttpWebRequest)WebRequest).ReadWriteTimeout = timeout;
               TimeoutSet = true;
            }
            else if (WebRequest is FtpWebRequest)
            {
               ((FtpWebRequest)WebRequest).ReadWriteTimeout = timeout;
               TimeoutSet = true;
            }
            return WebRequest.GetResponse();
         }
         catch (WebException ex)
         {
            // C# unterstützt keine Filter
            if (ex.Response != null && !TimeoutSet)
               ex.Response.GetResponseStream().ReadTimeout = timeout;
            throw;
         }
      }
   }

   // Funktionen für Kodierungen
   internal class EncodingTools
   {
      // Trennzeichen für Listen in den Einstellungen
      private char[] SettingsSeperationChars = { ' ', '\t', '\n', '\r' };
      // Bezeichnungen für die Modi, die Kodierung einer heruntergeladenen Datei zu ermitteln
      private string[] EncodingModeNames = { "ContentType", "Charset", "Auto", "Fixed" };
      // die Zuordnung von Kodierungen zu Kodierungsnamen
      private Dictionary<string, Encoding> FixedEncodings = new Dictionary<string, Encoding>(10);
      // die Reihenfolge der auszuprobierenden Stellen, an denen eine Kodierung angegeben sein kann, für alle Downloadtypen
      private Dictionary<string, SortedList<int, int>> EncodingModes = new Dictionary<string, SortedList<int, int>>(10);
      // hält die Anwendungseinstellungen bereit
      private AppSettingsReader Settings;

      public EncodingTools(AppSettingsReader settings)
      {
         this.Settings = settings;
      }

      // erstellt einen Leser für den Strom einer Web-Antwort
      public StreamReader GetReader(WebResponse webResponse, string settingsPrefix)
      {
         // Reihenfolge der auszuprobierenden Stellen, an denen eine Kodierung angegeben sein kann, ermitteln
         SortedList<int, int> UsedModes;
         int ModeIndex;
         if (!this.EncodingModes.TryGetValue(settingsPrefix, out UsedModes))
         {
            // wenn nicht vorhanden, aus Einstellungen lesen
            string[] Modes = ((string)this.Settings.GetValue(settingsPrefix + "EncodingMode", typeof(string))).Split(this.SettingsSeperationChars, StringSplitOptions.RemoveEmptyEntries);
            this.EncodingModes.Add(settingsPrefix, UsedModes = new SortedList<int, int>(4));
            for (int ZVar1 = 0; ZVar1 < this.EncodingModeNames.Length; ZVar1++)
               if ((ModeIndex = Array.IndexOf(Modes, this.EncodingModeNames[ZVar1])) >= 0)
                  UsedModes.Add(ModeIndex, ZVar1);
         }
         // Kodierung der Reihe nach ermitteln, bei erster erfolgreicher abbrechen, aber bei automatischer noch weiter suchen
         Encoding Enc = null;
         bool AutoMode = false;
         HttpWebResponse HttpWebResponse = webResponse as HttpWebResponse;
         for (int ZVar1 = 0; ZVar1 < UsedModes.Count; ZVar1++)
            // automatische Bestimmung anhand der Bytereihenfolgemarkierung, weitermachen, um Alternativkodierung zu ermitteln
            if ((ModeIndex = UsedModes.Values[ZVar1]) == 2)
               AutoMode = true;
            else
            {
               string EncName = null;
               if (HttpWebResponse != null && ModeIndex < 2)
               {
                  // Bestimmung anhand des Inhaltstyps der HTTP-Antwort
                  if (ModeIndex == 0)
                     try
                     {
                        EncName = new ContentType(HttpWebResponse.ContentType).CharSet;
                     }
                     catch (FormatException) { }
                  // Bestimmung anhand des Zeichensatzes der HTTP-Antwort
                  else
                     EncName = HttpWebResponse.CharacterSet;
                  // Kodierung versuchen zu erzeugen
                  if (EncName == null)
                     continue;
                  try
                  {
                     Enc = Encoding.GetEncoding(EncName);
                  }
                  catch (ArgumentException) { }
               }
               // feste Kodierung aus Einstellungen
               else if (ModeIndex == 3 && !this.FixedEncodings.TryGetValue(settingsPrefix, out Enc))
               {
                  EncName = (string)this.Settings.GetValue(settingsPrefix + "FixedEncoding", typeof(string));
                  // Kodierung versuchen zu erzeugen
                  try
                  {
                     this.FixedEncodings.Add(EncName, Enc = Encoding.GetEncoding(EncName));
                  }
                  catch (ArgumentException)
                  {
                     this.FixedEncodings.Add(EncName, null);
                  }
               }
               // wenn Kodierung gefunden, Schleife beenden
               if (Enc != null)
                  break;
            }
         // Reader erzeugen mit Kodierung; Standardkodierung verwenden, wenn noch keine ermittelt wurde
         return new StreamReader(webResponse.GetResponseStream(), Enc ?? new ASCIIEncoding(), AutoMode);
      }
   }
}

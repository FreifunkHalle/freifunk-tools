// Lizenz: Creative Commons by-nc-sa, http://creativecommons.org/licenses/by-nc-sa/3.0/deed.de
// Urheber: Matthias Schäfer, 06128 Halle (Saale), Germany, freifunk-tox gmx de

<%@ WebHandler Language="C#" Class="Freifunk.Webpages.FfmapJsonGenerator" %>

namespace Freifunk.Webpages
{
// erzeugt die JSON-Ausgabe für die ffmap
   public class FfmapJsonGenerator : JsonGenerator
   {
      public FfmapJsonGenerator()
         : base(new string[] { "boardtype", "ff_release" }, 
                new int[] { 0, 0 },
                new bool[] { false, false },
                new string[] { "board", "version" }, 
                new bool[] { true, false }, 
                0, -1, JsonOutputOptions.FfMap)
      {
         BaseJsonObjectName = "nodes";
      }
   }
}

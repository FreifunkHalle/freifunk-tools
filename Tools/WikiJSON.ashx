// Lizenz: Creative Commons by-nc-sa, http://creativecommons.org/licenses/by-nc-sa/3.0/deed.de
// Urheber: Matthias Schäfer, 06128 Halle (Saale), Germany, freifunk-tox gmx de

<%@ WebHandler Language="C#" Class="Freifunk.Webpages.WikiJsonGenerator" %>

namespace Freifunk.Webpages
{
   // erzeugt die JSON-Ausgabe für das Wiki
   public class WikiJsonGenerator : JsonGenerator
   {
      public WikiJsonGenerator()
         : base(new string[] { "boardtype", "boardnum", "ff_release", "ff_adm_loc", "wl0_txpwr", "wl0_channel" }, 
                new int[] { 0, 0, 0, 0, 0, 0 },
                new bool[] { false, false, false, false, true, true },
                new string[] { "board", null, "version", "addr", "power", "kanal" }, 
                new bool[] { false, false, false, false, true, true }, 
                0, -1, JsonOutputOptions.Wiki)
      {
      }
   }
}

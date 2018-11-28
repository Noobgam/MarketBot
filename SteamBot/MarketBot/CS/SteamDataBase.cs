using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Utility;

namespace SteamBot.MarketBot.CS {
    static class SteamDataBase {
        const string PATH = "csgobackpack_cache";
        public static Dictionary<string, double> cache;
        static SteamDataBase() {
            if (File.Exists(PATH)) {
                cache = BinarySerialization.ReadFromBinaryFile<Dictionary<string, double>>(PATH);
            } else {
                cache = new Dictionary<string, double>();
            }
        }

        public static void RefreshDatabase(string text) {
            JObject temp = JObject.Parse(text);
            JObject items_list = (JObject)temp["items_list"];
            string[] price_containers = { "7_days", "24_hours", "30_days", "all_time" };
            foreach (var x in items_list) {
                try {
                    JToken keep = x.Value["price"];
                    foreach (string container in price_containers) {
                        if (keep[container] != null) {
                            cache[x.Key] = (double)x.Value["price"][container]["median"];
                        }
                    }
                } catch (Exception ex) { 
                    
                }
            }
            BinarySerialization.WriteToBinaryFile(PATH, cache);
        }
    }
}

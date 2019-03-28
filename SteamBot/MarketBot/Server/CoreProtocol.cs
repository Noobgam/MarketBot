using CSGOTM;
using Newtonsoft.Json.Linq;
using SteamBot.MarketBot.CS.Bot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Utility;

namespace Server {
    public class CoreProtocol {
        public static NewMarketLogger Log = new NewMarketLogger("CoreProtocol");

        List<string> availableApis;
        int lastIndex = 0;        

        public CoreProtocol() {
            availableApis = new List<string>();
            try {
                string[] apis = File.ReadAllLines(Path.Combine("secrets", "extra-api-keys"));
                lock (availableApis) {
                    availableApis.AddRange(apis);
                }
            } catch {
                Log.Warn("Something bad happened, extra api keys is corrupt");
            }
        }

        private string GetApi() {
            string result = null;
            lock (availableApis) {
                if (lastIndex < availableApis.Count) {
                    result = availableApis[lastIndex];
                }
                lastIndex++;
                if (lastIndex >= availableApis.Count) {
                    lastIndex = 0;
                }
            }
            return result;
        }

        public void CheckApi(string s) {
            JObject res = Test(s);
            if ((bool)res["success"] == false) {
                lock (availableApis) {
                    availableApis = availableApis.Where(api => api != s).ToList();
                    if (lastIndex >= availableApis.Count) {
                        lastIndex = 0;
                    }
                }
            }
        }

        public void CheckApiBackGround(string s) {
            Tasking.Run(() => CheckApi(s));
        }

        public JObject Test(string api) {
            string url = $"/api/Test/?key={GetApi()}";
            try {
                string response = Utility.Request.Get(Consts.MARKETENDPOINT + url);
                if (response == null) {
                    return new JObject {
                        ["success"] = false
                    };
                }
                JObject temp = JObject.Parse(response);
                return temp;
            } catch (Exception ex) {
                Log.ApiError(ex.Message);
            }
            return new JObject {
                ["success"] = false
            };
        }

        public JObject MassInfo(List<Tuple<string, string>> items, int sell = 0, int buy = 0, int history = 0, int info = 0) {
            string api = GetApi();
            if (api == null) {
                return null;
            }
            string url = $"/api/MassInfo/{sell}/{buy}/{history}/{info}?key={GetApi()}";
            string data = "list=" + String.Join(",", items.Select(lr => lr.Item1 + "_" + lr.Item2).ToArray());
            try {
                string result = Utility.Request.Post(Consts.MARKETENDPOINT + url, data);
                if (result == null)
                    return null;
                if (result == "{\"error\":\"Bad KEY\"}") {
                    Log.ApiError("Bad key");
                    CheckApiBackGround(api);
                    return null;
                }
                JObject temp = JObject.Parse(result);
                return temp;
            } catch (Exception ex) {
                Log.ApiError(ex.Message);
            }
            return null;
        }
    }
}

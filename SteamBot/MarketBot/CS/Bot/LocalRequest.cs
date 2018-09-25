using Newtonsoft.Json.Linq;
using SteamTrade;
using System.Collections.Specialized;
using System.Net;

namespace CSGOTM {
    public static class LocalRequest {
        public static JToken RawGet(string endpoint, WebHeaderCollection headers) {
            return JToken.Parse(Utility.Request.Get(Consts.Endpoints.localhost + endpoint, headers));
        }

        private static JToken RawGet(string endpoint, string botname) {
            WebHeaderCollection headers = new WebHeaderCollection {
                ["botname"] = botname
            };
            return RawGet(endpoint, headers);
        }

        private static void RawPut(string endpoint, string botname, string data) {
            WebHeaderCollection headers = new WebHeaderCollection {
                ["botname"] = botname,
                ["data"] = data
            };
            RawGet(endpoint, headers);
        }

        public static JObject GetBestToken(string botname) {
            return (JObject)RawGet(Consts.Endpoints.GetBestToken, botname);
        }

        public static void PutInventory(string botname, GenericInventory inv) {
            RawPut(Consts.Endpoints.PutCurrentInventory, botname, inv.items.Count.ToString());
        }

        public static void PutSalesHistorySize(string botname, int cnt) {
            RawPut(Consts.Endpoints.SalesHistorySize, botname, cnt.ToString());
        }

        public static void Ping(string botname) {
            RawGet(Consts.Endpoints.PingPong, botname);
        }
    }
}

using Newtonsoft.Json.Linq;
using SteamTrade;
using System;
using System.Collections.Specialized;
using System.Net;

namespace CSGOTM {
    public static class LocalRequest {
        private static void VoidRawGet(string endpoint, WebHeaderCollection headers) {
            Utility.Request.Get(Consts.Endpoints.localhost + endpoint, headers);
        }
        private static JToken RawGet(string endpoint, WebHeaderCollection headers) {
            return JToken.Parse(Utility.Request.Get(Consts.Endpoints.localhost + endpoint, headers));
        }

        public static JToken RawGet(string endpoint, string botname) {
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
            VoidRawGet(endpoint, headers);
        }

        public static JObject GetBestToken(string botname) {
            return (JObject)RawGet(Consts.Endpoints.GetBestToken, botname);
        }

        public static void PutInventory(string botname, GenericInventory inv) {
            RawPut(Consts.Endpoints.PutCurrentInventory, botname, inv.items.Count.ToString());
        }

        public static void PutMoney(string botname, int money) {
            RawPut(Consts.Endpoints.PutMoney, botname, money.ToString());
        }

        public static void PutSalesHistorySize(string botname, int cnt) {
#if DEBUG
            RawPut(Consts.Endpoints.SalesHistorySize, botname, cnt.ToString());
#else
            //Console.WriteLine("Don't put sales in release mode.");            
#endif
        }

        public static void PutL1Optimized(string botname, int cnt) {
            RawPut(Consts.Endpoints.SalesHistorySize, botname, cnt.ToString());
        }

        public static void Ping(string botname) {
            RawGet(Consts.Endpoints.PingPong, botname);
        }
    }
}

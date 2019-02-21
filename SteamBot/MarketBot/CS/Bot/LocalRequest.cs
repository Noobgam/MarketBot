using Newtonsoft.Json.Linq;
using SteamBot.MarketBot.CS.Bot;
using SteamTrade;
using System;
using System.Collections.Specialized;
using System.Net;

namespace CSGOTM {
    public static class LocalRequest {
        private static NewMarketLogger Log = new NewMarketLogger("LocalRequest");

        private static void VoidRawGet(string endpoint, WebHeaderCollection headers) {
            try {
                Utility.Request.RawGet(Consts.Endpoints.juggler + endpoint, headers);
            } catch (Exception ex) {
                Log.Crash($"Local request failed. {ex.Message}");
                return;
            }
        }

        private static JToken RawGet(string endpoint, WebHeaderCollection headers) {
            try {
                return JToken.Parse(Utility.Request.RawGet(Consts.Endpoints.juggler + endpoint, headers));
            } catch (Exception ex) {
                Log.Crash($"Local request failed. {ex.Message}");
                return null;
            }
        }

        private static JToken RawGet(string endpoint) {
            try {
                return JToken.Parse(Utility.Request.RawGet(Consts.Endpoints.juggler + endpoint));
            } catch (Exception ex) {
                Log.Crash($"Local request failed. {ex.Message}");
                return null;
            }
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

        public static JObject GetEconomy() {
            return (JObject)RawGet(Consts.Endpoints.GetCurrency);
        }

        public static string GetAuthFile(string botname) {
            return (string)((JObject)RawGet(Consts.Endpoints.GetAuthFile, botname))["data"];
        }

        public static string GetDatabase() {
            return (string)(((JObject)RawGet(Consts.Endpoints.GetSalesDatabase))["data"]);
        }

        public static JObject GetConfig() {
            return (JObject)RawGet(Consts.Endpoints.GetConfig)["config"];
        }

        public static string GetEmptyStickeredDatabase() {
            return (string)(((JObject)RawGet(Consts.Endpoints.GetEmptyStickeredDatabase))["data"]);
        }

        public static bool IsPrimeTime() {
            var temp = (JObject)RawGet(Consts.Endpoints.Primetime);
            return (bool)(temp["primetime"]);
        }

        public static void PutInventory(string botname, GenericInventory inv) {
            RawPut(Consts.Endpoints.PutCurrentInventory, botname, inv.items.Count.ToString());
        }

        public static void PutEmptyStickered(string botname, long cid, long iid) {
            RawPut(Consts.Endpoints.PutEmptyStickered, botname, cid + "_" + iid);
        }

        public static void PutMoney(string botname, int money) {
            RawPut(Consts.Endpoints.PutMoney, botname, money.ToString());
        }

        public static void PutTradableCost(string botname, double sumprice, int untracked) {
            RawPut(Consts.Endpoints.PutTradableCost, botname, sumprice.ToString() + ":" + untracked.ToString());
        }

        public static void PutTradeToken(string botname, string token) {
            RawPut(Consts.Endpoints.PutTradeToken, botname, token);
        }

        public static void PutMedianCost(string botname, double sumprice) {
            RawPut(Consts.Endpoints.PutMedianCost, botname, sumprice.ToString());
        }

        public static void PutL1Optimized(string botname, int cnt) {
            RawPut(Consts.Endpoints.SalesHistorySize, botname, cnt.ToString());
        }

        public static void Ping(string botname) {
            RawGet(Consts.Endpoints.PingPong, botname);
            Console.WriteLine(botname + " pong");
        }
    }
}

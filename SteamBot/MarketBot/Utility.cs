using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Utility;

namespace CSGOTM {
    static class Consts {
        public const int MINORCYCLETIMEINTERVAL = 1000 * 60 * 10; // 10 minutes
        public const int APICOOLDOWN = 1000 * 3; // 3 seconds
        public const int SECOND = 1050; //used to restrict rps.
        public const int GLOBALRPSLIMIT = 1;
        public const int DAY = 86400;
        public const int MINSIZE = 70;
        public const int CRITICALTHRESHHOLD = 600;
        public const double DEFAULTRPS = 0.5; //TODO(noobgam): make it great again
        public const int PARSEDATABASEINTERVAL = 1000 * 60; //every minute
        public const int REFRESHINTERVAL = 1000 * 60 * 15; //every 15 minutes
        public const string MARKETENDPOINT = "https://market.csgo.com";
        public const int MAXORDERQUEUESIZE = 150;

        public static class Databases {
            public static class Mongo {
                public const string SteamBotMain = "steambot_main";
            }
        }

        public static class Endpoints {
            public const string ServerConfig = "https://gist.githubusercontent.com/Noobgam/8aa9b32b6b147b69f2ffc2057f75652e/raw/full_config.json";
            public const string BotConfig = "https://gist.githubusercontent.com/Noobgam/819841a960112ae85fe8ac61b6bd33e1/raw/config.json";
            public static string prefix = "http://+:4345/";
            public static string juggler = "http://steambot.noobgam.me";
            public static string TMSocket = "ws://wsn.dota2.net/wsn/";
            #region GET
            public const string GetBestToken = "/getbesttoken/";
            public const string PingPong = "/ping/";
            public const string Status = "/status/";
            public const string RPS = "/rps/";
            public const string MongoFind = "/mongo/find/";
            public const string GetCurrency = "/economy/";
            public const string BanUser = "/ban/";
            public const string UnBanUser = "/unban/";
            public const string GetBannedUsers = "/getbannedusers/";
            public const string GetSalesDatabase = "/getsalesdatabase/";
            public const string GetEmptyStickeredDatabase = "/getemptystickereddatabase/";
            public const string GetConfig = "/getconfig/";
            public const string GetAuthFile = "/getauthfile/";
            #endregion

            #region PUT
            public const string PutCurrentInventory = "/putInventory/";
            public const string PutEmptyStickered = "/putemptystickered/";
            public const string PutMoney = "/putMoney/";
            public const string PutMedianCost = "/putMedianCost/";
            public const string PutTradableCost = "/putTradableCost/";
            public const string SalesHistorySize = "/saleshistorysize/";
            public const string PutTradeToken = "/puttradetoken/";
            #endregion
        }
    }

    public class Pair<T, U> {
        public Pair() {
        }

        public Pair(T first, U second) {
            this.First = first;
            this.Second = second;
        }

        public T First { get; set; }
        public U Second { get; set; }
    }

    public class TMTrade {
        public string ui_id;
        public string i_name;
        public string i_market_name;
        public string i_name_color;
        public string i_rarity;
        public string i_descriptions;
        public string ui_status;
        public string he_name;
        public double ui_price;
        public string i_classid;
        public string i_instanceid;
        public string ui_real_instance;
        public string i_quality;
        public string i_market_hash_name;
        public double i_market_price;
        public int position;
        public double min_price;
        public string ui_bid;
        public string ui_asset;
        public string type;
        public string ui_price_text;
        public bool min_price_text;
        public string i_market_price_text;
        public int offer_live_time;
        public string placed;
    }

    public class Order {
        public string i_classid;
        public string i_instanceid;
        public string i_market_hash_name;
        public string i_market_name;
        public string o_price;
        public string o_state;
    }

    public class HistoricalOperation {
        public HistoricalOperation() { }
        public HistoricalOperation(HistoricalOperation rhs) {
            this.h_id = rhs.h_id;
            this.h_event = rhs.h_event;
            this.h_time = rhs.h_time;
            this.h_event_id = rhs.h_event_id;
            this.join = rhs.join;
            this.app = rhs.app;
            this.id = rhs.id;
            this.classid = rhs.classid;
            this.instanceid = rhs.instanceid;
            this.quality = rhs.quality;
            this.name_color = rhs.name_color;
            this.market_name = rhs.market_name;
            this.market_hash_name = rhs.market_hash_name;
            this.paid = rhs.paid;
            this.recieved = rhs.recieved;
            this.stage = rhs.stage;
            this.item = rhs.item;
            this.flags = rhs.flags;
        }

        public string h_id { get; set; }
        public string h_event { get; set; }
        public string h_time { get; set; }
        [BsonId]
        public string h_event_id { get; set; }
        public int join { get; set; }
        public string app { get; set; }
        public string id { get; set; }
        public string classid { get; set; }
        public string instanceid { get; set; }
        public string quality { get; set; }
        public string name_color { get; set; }
        public string market_name { get; set; }
        public string market_hash_name { get; set; }
        public string paid { get; set; }
        public string recieved { get; set; }
        public string stage { get; set; }
        public string item { get; set; }
        public string flags { get; set; }
    }

    public class MongoHistoricalOperation : HistoricalOperation {
        public string botname;

        public MongoHistoricalOperation(HistoricalOperation core, string botname) : base(core) {
            this.botname = botname;
        }
    }

    public class BannedUser : IEquatable<BannedUser> {
        [BsonId]
        public long SteamID64;

        public BannedUser(long steamID64) {
            SteamID64 = steamID64; //I can afford the cast here
        }

        public override bool Equals(object obj) {
            return Equals(obj as BannedUser);
        }

        public bool Equals(BannedUser other) {
            return other != null &&
                   SteamID64 == other.SteamID64;
        }

        public override int GetHashCode() {
            return 510678916 + SteamID64.GetHashCode();
        }

        public static bool operator ==(BannedUser user1, BannedUser user2) {
            return EqualityComparer<BannedUser>.Default.Equals(user1, user2);
        }

        public static bool operator !=(BannedUser user1, BannedUser user2) {
            return !(user1 == user2);
        }
    }

    public class NewItem {
        public long i_classid;
        public long i_instanceid;
        public string i_market_name;
        public long ui_price;

        public static Dictionary<string, int> mapping = new Dictionary<string, int>();

        public NewItem() {
        }

        public NewItem(string data) {
            JsonTextReader reader = new JsonTextReader(new StringReader(data));
            string currentProperty = string.Empty;
            while (reader.Read()) {
                if (reader.Value != null) {
                    if (reader.TokenType == JsonToken.PropertyName)
                        currentProperty = reader.Value.ToString();
                    else if (reader.TokenType == JsonToken.String) {
                        switch (currentProperty) {
                            case "i_classid":
                                i_classid = long.Parse(reader.Value.ToString());
                                break;
                            case "i_instanceid":
                                i_instanceid = long.Parse(reader.Value.ToString());
                                break;
                            case "i_market_name":
                                i_market_name = reader.Value.ToString();
                                break;
                            case "ui_currency":
                                if (reader.Value.ToString() != "RUB") {
                                    throw new ArgumentException($"Currencies other than RUB are not supported {data}");
                                }
                                break;
                            default:
                                break;
                        }
                    } else if (currentProperty == "ui_price") {
                        ui_price = (long)(float.Parse(reader.Value.ToString()) * 100);
                    }
                }
            }
        }

        public NewItem(string[] item) {
            i_classid = long.Parse(item[mapping["c_classid"]]);
            i_instanceid = long.Parse(item[mapping["c_instanceid"]]);
            i_market_name = item[mapping["c_market_name"]];
            if (i_market_name.Length >= 2) {
                i_market_name = i_market_name.Remove(0, 1);
                i_market_name = i_market_name.Remove(i_market_name.Length - 1);
            }
            ui_price = long.Parse(item[mapping["c_price"]]);
        }
    }

    public struct Message {
        public string type;
        public string data;

        public Message(string type, string data) {
            this.type = type;
            this.data = data;
        }
    }

    public class TradeResult {
        public string result;
        public string id;
    }

    [Serializable]
    public class BasicHistoryItem {
        public int price; //price is measured in kopeykas
        public BasicHistoryItem() { }
        public BasicHistoryItem(string data) {
            string[] arr = data.Substring(1, data.Length - 2).Trim('[', ']').Split(new string[] { "\\\"" }, StringSplitOptions.RemoveEmptyEntries);
            //cid, iid, hashname
            for (int i = 0; i < arr.Length; ++i) {
                arr[i] = Encode.DecodeEncodedNonAsciiCharacters(arr[i]);
            }
            if (arr.Length == 15) {
                long.Parse(arr[0]);
                long.Parse(arr[2]);
                price = int.Parse(arr[8]);
                if (arr[14] != "RUB") {
                    throw new ArgumentException($"Currencies other than rub are not supported {data}");
                }
                return;
            } else {
                throw new ArgumentException($"Can't construct newhistory item from {data}");
            }
            //cid - iid
        }
        public BasicHistoryItem(NewHistoryItem rhs) {
            price = rhs.price;
        }
    }

    [Serializable]
    public class NewHistoryItem {
        public long i_classid;
        public long i_instanceid;
        public string i_market_name;
        public int price; //price is measured in kopeykas
        public NewHistoryItem() {}
        public NewHistoryItem(string data) {
            string[] arr = data.Substring(1, data.Length - 2).Trim('[',']').Split(new string[] { "\\\"" }, StringSplitOptions.RemoveEmptyEntries);
            //cid, iid, hashname
            for (int i = 0; i < arr.Length; ++i) {
                arr[i] = Encode.DecodeEncodedNonAsciiCharacters(arr[i]);
            }
            if (arr.Length == 15) {
                //0 - cid
                //2 - iid
                //4 - market_hashname
                //6 - date
                //8 - price
                //10 - market_name
                //12 - color
                //14 - currency

                //rest are ','
                i_classid = long.Parse(arr[0]);
                i_instanceid = long.Parse(arr[2]);
                price = Int32.Parse(arr[8]);
                i_market_name = arr[10];
                if (arr[14] != "RUB") {
                    throw new ArgumentException($"Currencies other than rub are not supported {data}");
                }
                return;
            } else {
                throw new ArgumentException($"Can't construct newhistory item from {data}");
            }     
            //cid - iid
        }
        public NewHistoryItem(NewHistoryItem rhs) {
            i_classid = rhs.i_classid;
            i_instanceid = rhs.i_instanceid;
            i_market_name = rhs.i_market_name;
            price = rhs.price;
        }
    }

    public class Auth {
        public string wsAuth;
        public string success;
    }

    public class Inventory {
        public class SteamItem {
            public string ui_id;
            public string i_market_hash_name;
            public string i_market_name;
            public string i_name;
            public string i_name_color;
            public string i_rarity;
            public List<JObject> i_descriptions;
            public int ui_status;
            public string he_name;
            public int ui_price;
            public int min_price;
            public bool ui_price_text;
            public bool min_price_text;
            public string i_classid;
            public string i_instanceid;
            public bool ui_new;
            public int position;
            public string wear;
            public int tradable;
            public double i_market_price;
            public string i_market_price_text;
        }

        public List<SteamItem> content;
    }

    public class Perfomance {

        public class RPSKeeper {

            const int SHIFT = 5;

            int[] Calls;
            public RPSKeeper() {
                Calls = new int[60];      
            }
            
            public void Tick(int ticks = 1) {
                int CurrentSecond = DateTime.Now.Second;
                int PrevSecond = CurrentSecond - SHIFT - 1;
                if (PrevSecond < 0)
                    PrevSecond += 60;
                Calls[DateTime.Now.Second] += ticks;
                Calls[PrevSecond] = 0;
            }

            public double GetRps() {
                int sum = 0;
                int CurrentSecond = DateTime.Now.Second;
                for (int iter = 0; iter < SHIFT; ++iter) {
                    sum += Calls[CurrentSecond];
                    CurrentSecond--;
                    if (CurrentSecond < 0) {
                        CurrentSecond += 60;
                    }
                }
                return (double)sum / SHIFT;
            }
        }
    }

    public class SortUtils {
        public static void Sort(JObject jObj) {
            var props = jObj.Properties().ToList();
            foreach (var prop in props) {
                prop.Remove();
            }

            foreach (var prop in props.OrderBy(p => p.Name)) {
                jObj.Add(prop);
                if (prop.Value is JObject)
                    Sort((JObject)prop.Value);
                if (prop.Value is JArray) {
                    Int32 iCount = prop.Value.Count();
                    for (Int32 iIterator = 0; iIterator < iCount; iIterator++)
                        if (prop.Value[iIterator] is JObject)
                            Sort((JObject)prop.Value[iIterator]);
                }
            }
        }
    }
}

using MongoDB.Bson.Serialization.Attributes;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CSGOTM {
    public static class Consts {
        public const int MINORCYCLETIMEINTERVAL = 1000 * 60 * 10; // 10 minutes
        public const int APICOOLDOWN = 1000 * 3; // 3 seconds
        public const int SECOND = 1050; //used to restrict rps.
        public const int GLOBALRPSLIMIT = 1;
        public const int DAY = 86400;
        public const int MINSIZE = 70;
        public const int CRITICALTHRESHHOLD = 960;
        public const double DEFAULTRPS = 0.5; //TODO(noobgam): make it great again
        public const int PARSEDATABASEINTERVAL = 1000 * 60; //every minute
        public const int REFRESHINTERVAL = 1000 * 60 * 15; //every 15 minutes
        public const string MARKETENDPOINT = "https://market.csgo.com";

        public static Dictionary<string, string> TokenCache = new Dictionary<string, string>();

        public static class Endpoints {
            public const string ServerConfig = "https://gist.githubusercontent.com/Noobgam/ffd2a1ea910fa7a8bc7aae666dfad1c2/raw/prod_conf.json";
            public const string BotConfig = "https://gist.githubusercontent.com/Noobgam/819841a960112ae85fe8ac61b6bd33e1/raw/config.json";
            public const string prefix = "http://+:4345/";
            public const string localhost = "http://localhost:4345";
            #region GET
            public const string GetBestToken = "/getbesttoken/";
            public const string PingPong = "/ping/";
            public const string Status = "/status/";
            public const string MongoFind = "/mongo/find/";
            #endregion

            #region PUT
            public const string PutCurrentInventory = "/putInventory/";
            public const string PutMoney = "/putMoney/";
            public const string PutInventoryCost = "/putInventoryCost/";
            public const string PutTradableCost = "/putTradableCost/";
            public const string SalesHistorySize = "/saleshistorysize/";
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

    public class NewItem {
        public long i_classid;
        public long i_instanceid;
        public string i_market_name;
        public int ui_price;

        public static Dictionary<string, int> mapping = new Dictionary<string, int>();

        public NewItem() {
        }

        public NewItem(string[] item) {
            i_classid = long.Parse(item[mapping["c_classid"]]);
            i_instanceid = long.Parse(item[mapping["c_instanceid"]]);
            i_market_name = item[mapping["c_market_name"]];
            if (i_market_name.Length >= 2) {
                i_market_name = i_market_name.Remove(0, 1);
                i_market_name = i_market_name.Remove(i_market_name.Length - 1);
            }
            ui_price = int.Parse(item[mapping["c_price"]]);
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
    public class NewHistoryItem {
        public long i_classid;
        public long i_instanceid;
        public string i_market_name;
        public int price; //price is measured in kopeykas
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
}

public static class BinarySerialization {
    static ConcurrentDictionary<string, ReaderWriterLockSlim> fileLock = new ConcurrentDictionary<string, ReaderWriterLockSlim>();
    public static void WriteToBinaryFile<T>(string filePath, T objectToWrite, bool append = false) {
        fileLock.TryAdd(filePath, new ReaderWriterLockSlim());
        if (fileLock.TryGetValue(filePath, out ReaderWriterLockSlim rwlock)) {
            rwlock.EnterWriteLock();
            try {
                using (Stream stream = File.Open(filePath, append ? FileMode.Append : FileMode.Create)) {
                    var binaryFormatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                    binaryFormatter.Serialize(stream, objectToWrite);
                }
            } finally {
                rwlock.ExitWriteLock();
            }
        }
    }

    public static T ReadFromBinaryFile<T>(string filePath) {
        fileLock.TryAdd(filePath, new ReaderWriterLockSlim());
        if (fileLock.TryGetValue(filePath, out ReaderWriterLockSlim rwlock)) {
            rwlock.EnterReadLock();
            try {
                using (Stream stream = File.Open(filePath, FileMode.Open)) {
                    var binaryFormatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                    return (T)binaryFormatter.Deserialize(stream);
                }
            } finally {
                rwlock.ExitReadLock();
            }
        }
        return default(T);
    }
}


public static class JsonSerialization {
    public static void WriteToJsonFile<T>(string filePath, T objectToWrite, bool append = false) where T : new() {
        TextWriter writer = null;
        try {
            var contentsToWriteToFile =
                Newtonsoft.Json.JsonConvert.SerializeObject(objectToWrite, Newtonsoft.Json.Formatting.Indented);
            writer = new StreamWriter(filePath, append);
            writer.Write(contentsToWriteToFile);
        } finally {
            if (writer != null)
                writer.Close();
        }
    }

    public static T ReadFromJsonFile<T>(string filePath) where T : new() {
        TextReader reader = null;
        try {
            reader = new StreamReader(filePath);
            var fileContents = reader.ReadToEnd();
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(fileContents);
        } finally {
            if (reader != null)
                reader.Close();
        }
    }
}

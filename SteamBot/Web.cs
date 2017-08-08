using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;

namespace CSGOTM
{
    public class Pair<T, U>
    {
        public Pair() { }

        public Pair(T first, U second)
        {
            this.First = first;
            this.Second = second;
        }

        public T First { get; set; }
        public U Second { get; set; }
    }

    public class TMTrade
    {
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

    public class NewItem
    {
        public string i_quality;
        public string i_name_color;
        public string i_classid;
        public string i_instanceid;
        public string i_market_hash_name;
        public string i_market_name;
        public float ui_price;
        public string app;
        public string stickers;
    }

    public class Message
    {
        public string type;
        public string data;
    }

    public class TradeResult
    {
        public string result;
        public string id;
    }

    [Serializable]
    public class HistoryItem
    {
        public string i_classid;
        public string i_instanceid;
        public string i_market_hash_name;
        public string i_market_name;
        public int price; //price is measured in kopeykas
        public string timesold;
    }

    public class Auth
    {
        public string wsAuth;
        public string success;
    }

    public class Inventory
    {
        public class SteamItem
        {
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

public static class BinarySerialization
{
    public static void WriteToBinaryFile<T>(string filePath, T objectToWrite, bool append = false)
    {
        using (Stream stream = File.Open(filePath, append ? FileMode.Append : FileMode.Create))
        {
            var binaryFormatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
            binaryFormatter.Serialize(stream, objectToWrite);
        }
    }

    public static T ReadFromBinaryFile<T>(string filePath)
    {
        using (Stream stream = File.Open(filePath, FileMode.Open))
        {
            var binaryFormatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
            return (T)binaryFormatter.Deserialize(stream);
        }
    }
}

public static class JsonSerialization
{
    public static void WriteToJsonFile<T>(string filePath, T objectToWrite, bool append = false) where T : new()
    {
        TextWriter writer = null;
        try
        {
            var contentsToWriteToFile = Newtonsoft.Json.JsonConvert.SerializeObject(objectToWrite, Newtonsoft.Json.Formatting.Indented);
            writer = new StreamWriter(filePath, append);
            writer.Write(contentsToWriteToFile);
        }
        finally
        {
            if (writer != null)
                writer.Close();
        }
    }

    public static T ReadFromJsonFile<T>(string filePath) where T : new()
    {
        TextReader reader = null;
        try
        {
            reader = new StreamReader(filePath);
            var fileContents = reader.ReadToEnd();
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(fileContents);
        }
        finally
        {
            if (reader != null)
                reader.Close();
        }
    }
}
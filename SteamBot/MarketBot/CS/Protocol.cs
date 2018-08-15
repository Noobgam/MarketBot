using System;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Threading;
using System.Collections.Generic;

using SteamKit2;
using WebSocket4Net;

using SteamTrade.TradeOffer;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

using System.Linq;

namespace CSGOTM
{
    public class Protocol
    {
#if DEBUG
        public int totalwasted = 0;
#endif
        public Utility.MarketLogger Log;
        private Queue<TradeOffer> QueuedOffers;
        public Logic Logic;
        public SteamBot.Bot Bot;
        static Random Generator = new Random();
        static string Api = null;

        private string ExecuteApiRequest(string url)
        {
            return Utility.Request.Get("https://market.csgo.com" + url);
        }
        
        bool died = true;
        WebSocket socket = new WebSocket("wss://wsn.dota2.net/wsn/");
        public Protocol(SteamBot.Bot Bot, string api) {
            Api = api;
            this.Bot = Bot;
            Thread starter = new Thread(new ThreadStart(StartUp));
            starter.Start();
        }
        
        private void StartUp() {
            while (Logic == null || Bot.IsLoggedIn == false)
                Thread.Sleep(10);
            QueuedOffers = new Queue<TradeOffer>();
            socket.Opened += Open;
            socket.Closed += Error;
            socket.MessageReceived += Msg;
            socket.Open();
        }

        public readonly string[] search = { "<div class=\\\"price\\\"", "<div class=\\\"name\\\"" };

        public class Dummy
        {
            public string s;
        }

        string parse(string s)
        {
            var sb = new StringBuilder();
            sb.Append("{\"s\":\"" + s + "\"}");
            string t = sb.ToString();
            var dummy = JsonConvert.DeserializeObject<Dummy>(t);
            return dummy.s;
        }

        static string DecodeEncodedNonAsciiCharacters(string value)
        {
            return Regex.Replace(
                value,
                @"\\u(?<Value>[a-zA-Z0-9]{4})",
                m => {
                    return ((char)int.Parse(m.Groups["Value"].Value, NumberStyles.HexNumber)).ToString();
                });
        }

        void Msg(object sender, MessageReceivedEventArgs e)
        {
            if (e.Message == "pong")
                return;
            var message = e.Message;
            Message x = JsonConvert.DeserializeObject<Message>(message);
            //Console.WriteLine(x.type);
            switch (x.type)
            {
                case "newitems_go":
                    NewItem newItem = JsonConvert.DeserializeObject<NewItem>(x.data);
                    //getBestOrder(newItem.i_classid, newItem.i_instanceid);
                    newItem.ui_price = newItem.ui_price * 100 + 0.5f;
                    if (Logic.WantToBuy(newItem))
                    {
                        if (Buy(newItem))
                            Log.Success("Purchased: " + newItem.i_market_name + " " + newItem.ui_price);
                        else
                            Log.Warn("Couldn\'t purchase " + newItem.i_market_name + " " + newItem.ui_price);
                    }
                    break;
                case "history_go":
                    try
                    {
                        char[] trimming = { '[', ']' };
                        x.data = DecodeEncodedNonAsciiCharacters(x.data);
                        x.data = x.data.Replace("\\", "").Replace("\"", "").Trim(trimming);
                        string[] arr = x.data.Split(',');
                        HistoryItem historyItem = new HistoryItem();
                        if (arr.Length == 7)
                        {
                            historyItem.i_classid = arr[0];
                            historyItem.i_instanceid = arr[1];
                            historyItem.i_market_hash_name = arr[2];
                            historyItem.timesold = arr[3];
                            historyItem.price = Int32.Parse(arr[4]);
                            historyItem.i_market_name = arr[5];
                        }
                        else if (arr.Length == 8)
                        {
                            historyItem.i_classid = arr[0];
                            historyItem.i_instanceid = arr[1];
                            historyItem.i_market_hash_name = arr[2] + arr[3];
                            historyItem.timesold = arr[4];
                            historyItem.price = Int32.Parse(arr[5]);
                            historyItem.i_market_name = arr[6];
                        }
                        else
                        {
                            throw new Exception(x.data + " is not a valid history item.");
                        }
                        Logic.ProcessItem(historyItem);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex.Message);
                    }
                    break;

                case "invcache_go":
                    Log.Info("Inventory was cached");
                    break;
                case "money":
                    //Console.ForegroundColor = ConsoleColor.Yellow;
                    //Console.WriteLine("Current balance: %f", Double.Parse(x.data.Split('<')[0]));
                    //Console.ForegroundColor = ConsoleColor.White;
                    break;
                default:
                    //Console.WriteLine(x.type);
                    x.data = DecodeEncodedNonAsciiCharacters(x.data);
                    Log.Info(x.data);
                    try
                    {
                        JObject json = JObject.Parse(x.data);
                        if ((int)json["status"] == 5)
                            Logic.doNotSell = true;
                    }
                    catch (Exception ex)
                    {

                    }
                    break;
            }
        }

        void pinger()
        {
            while (!died)
            {
                socket.Send("ping");
                Thread.Sleep(30000);
            }
        }

        bool SendSoldItems()
        {
            Log.Info("Sending " +
                "items");
            JObject json = JObject.Parse(ExecuteApiRequest("/api/ItemRequest/in/1/?key=" + Api));
            if (json["success"] == null)
                return false;
            else if ((bool)json["success"])
            {
                if ((bool)json["manual"]) {
                    string profile = (string)json["profile"];
                    ulong id = ulong.Parse(profile.Split('/')[4]);

                    var offer = Bot.NewTradeOffer(new SteamID(id));
                    foreach (JObject item in json["request"]["items"]) {
                        offer.Items.AddMyItem(
                            (int)item["appid"],
                            (long)item["contextid"],
                            (long)item["assetid"],
                            (long)item["amount"]);
                    }
                    Log.Info("Partner: {0}\nToken: {1}\nTradeoffermessage: {2}\nProfile: {3}", (string)json["request"]["partner"], (string)json["request"]["token"], (string)json["request"]["tradeoffermessage"], (string)json["profile"]);
                    //Log.Info("Token: " + (string)json["request"]["token"]);
                    if (offer.Items.NewVersion) {
                        string newOfferId;
                        if (offer.SendWithToken(out newOfferId, (string)json["request"]["token"], (string)json["request"]["tradeoffermessage"])) {
                            Bot.AcceptAllMobileTradeConfirmations();
                            Log.Success("Trade offer sent : Offer ID " + newOfferId);
                        }
                        return true;
                    }
                    return false;
                } else {
                    return true; //UHHHHHHHHHHHHHHHHHHHH
                }
            }
            else
                return false;
        }
        
        bool RequestPurchasedItems(string botID)
        {
            Log.Info("Requesting items");
            JObject json = JObject.Parse(ExecuteApiRequest("/api/ItemRequest/out/" + botID + "/?key=" + Api));
            if (json["success"] == null)
                return false;
            else if ((bool)json["success"])
            {
                return true;
            }
            else
                return false;
        }

        void HandleTrades()
        {
            while (!died)
            {
                try
                {
                    TMTrade[] arr = GetTradeList();
                    UpdateInventory();
                    bool had = false;
                    bool gone = false;
                    for (int i = 0; i < arr.Length; ++i)
                    {
                        if (arr[i].ui_status == "4")
                        {
                            UpdateInventory();
                            RequestPurchasedItems(arr[i].ui_bid);
                            gone = true;
                            Logic.doNotSell = true;
                            break;
                        }
                        had |= arr[i].ui_status == "2";
                    }
                    if (had && !gone)
                    {
                        UpdateInventory();
                        SendSoldItems();
                    }
                }
                catch (Exception ex)
                {

                }
                //once per 30 seconds we check trade list
                Thread.Sleep(30000);
            }
        }

        void Subscribe()
        {
            socket.Send("newitems_go");
            socket.Send("history_go");
        }

        void Auth()
        {
            Auth q = JsonConvert.DeserializeObject<Auth>(ExecuteApiRequest("/api/GetWSAuth/?key=" + Api));
            socket.Send(q.wsAuth);
            Subscribe();
        }

        void Open(object sender, EventArgs e)
        {
            died = false;
            Log.Success("Connection opened!");
            Auth();
            Thread ping = new Thread(new ThreadStart(pinger));
            ping.Start();
            Thread tradeHandler = new Thread(new ThreadStart(HandleTrades));
            tradeHandler.Start();
            //andrew is gay
        }

        void Error(object sender, EventArgs e)
        {
            Log.Error("Connection error");
            died = true;
            ReOpen();
        }

        void ReOpen()
        {
            for (int i = 0; died && i < 10; ++i)
            {
                socket = new WebSocket("wss://wsn.dota2.net/wsn/");
                socket.Opened += Open;
                socket.Closed += Error;
                socket.MessageReceived += Msg;
                socket.Open();
                Thread.Sleep(5000);
                Log.Info("Trying to reconnect for the %d-th time", i + 1);
            }
        }


        public bool Buy(NewItem item)
        {
#if DEBUG
            totalwasted += (int)item.ui_price;
            Log.Debug("Purchased an item for {0}, total wasted {1}", ((int)item.ui_price + .0) / 100, (totalwasted + .0) / 100);
            return true;
#else
            string url = "/api/Buy/" + item.i_classid + "_" + item.i_instanceid + "/" + ((int)item.ui_price).ToString() + "/?key=" + Api;
            string a = ExecuteApiRequest(url);
            JObject parsed = JObject.Parse(a);
            if (parsed["result"] == null)
                return false;
            else if ((string)parsed["result"] == "ok")
                return true;
            else
                return false;
#endif
        }

        //Interface starts here:
        [System.Obsolete("Specify item, it will parce it by itself.")]
        public bool Buy(string ClasssId, string InstanceId, int price)
        {
#if DEBUG
            totalwasted += price;
            Log.Success("Purchased an item for {0}, total wasted {1}", (price + .0) / 100, (totalwasted + .0) / 100);
            return true;
#else
            string a = ExecuteApiRequest("/api/Buy/" + ClasssId + "_" + InstanceId + "/" + price.ToString() + "/?key=" + Api);
            JObject parsed = JObject.Parse(a);
            foreach (var pair in parsed)
            {
                Console.WriteLine("{0}: {1}", pair.Key, pair.Value);
            }
            if (parsed["result"] == null)
                return false;
            else if ((string)parsed["result"] == "ok")
                return true;
            else
                return false;
#endif
        }

        public bool SellNew(string ClasssId, string InstanceId, int price)
        {
#if DEBUG //sorry nothing is implemented there, I don't really know what to write as debug
            return false;

#else
            string a = ExecuteApiRequest("/api/SetPrice/new_" + ClasssId + "_" + InstanceId + "/" + price.ToString() + "/?key=" + Api);
            JObject parsed = JObject.Parse(a);
            foreach (var pair in parsed)
                Console.WriteLine("{0}: {1}", pair.Key, pair.Value);
            if (parsed["result"] == null)
                return false;
            else if ((string)parsed["result"] == "ok")
                return true;
            else
                return false;
#endif
        }
        
        public JObject MassInfo(List<Tuple<string, string>> items, int sell = 0, int buy = 0, int history = 0, int info = 0) {
            string uri = "https://market.csgo.com/api/MassInfo/" + sell + "/" + buy + "/" + history + "/" + info + "?key=" + Api;
            string data = "list=" + String.Join(",", items.Select(lr => lr.Item1 + "_" + lr.Item2).ToArray());
            string result = Utility.Request.Post(uri, data);
            try {
                JObject temp = JObject.Parse(result);
                return temp;
            } catch (Exception ex) {
                Log.Error(ex.Message);
            }
            return null;
        }
        
        public bool Sell(string item_id, int price) {
            string a = ExecuteApiRequest("/api/SetPrice/" + item_id +  "/" + price + "/?key=" + Api);
            JObject parsed = JObject.Parse(a);
            foreach (var pair in parsed)
                Console.WriteLine("{0}: {1}", pair.Key, pair.Value);
            if (parsed["result"] == null)
                return false;
            else if ((string)parsed["result"] == "ok")
                return true;
            else
                return false;
        }

        public Inventory GetSteamInventory()
        {
            string a = ExecuteApiRequest("/api/GetInv/?key=" + Api);
            JObject json = JObject.Parse(a);
            Inventory inventory = new Inventory();
            inventory.content = new List<Inventory.SteamItem>();
            if (json["ok"] != null && (bool)json["ok"] == false)
                return inventory;
            inventory.content = json["data"].ToObject<List<Inventory.SteamItem>>();
            return inventory;
        }

        public bool SetOrder(string classid, string instanceid, int price)
        {
#if DEBUG   
            return false;
#else
            string a = ExecuteApiRequest("/api/ProcessOrder/" + classid + "/" + instanceid + "/" + price.ToString() + "/?key=" + Api);
            JObject json = JObject.Parse(a);
            if (json["success"] == null)
                return false;
            else if ((bool)json["success"])
                return true;
            else
                 return false;
#endif
        }

        bool UpdateInventory()
        {
            try
            {
                string a = ExecuteApiRequest("/api/UpdateInventory/?key=" + Api);
                JObject json = JObject.Parse(a);
                if (json["success"] == null)
                    return false;
                else if ((bool)json["success"])
                    return true;
                else
                    return false;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        // If best offer is our's returning -1;
        public int getBestOrder(string classid, string instanceid)
        {
            try
            {
                string a = ExecuteApiRequest("/api/ItemInfo/" + classid + "_" + instanceid + "/ru/?key=" + Api);
                JObject x = JObject.Parse(a);
                JArray thing = (JArray)x["buy_offers"];
                if (thing == null || thing.Count == 0)
                    return 49;
                if (int.Parse((string) thing[0]["o_price"]) == -1)
                    return -1;                
                return int.Parse((string) thing[0]["o_price"]);
            }
            catch (Exception ex)
            {
                Log.Error(ex.Message);
                return -1;
            }
        }

        public JArray GetItemHistory(string classid, string instanceid) {
            try {
                string a = ExecuteApiRequest("/api/ItemHistory/" + classid + "_" + instanceid + "/ru/?key=" + Api);
                JObject x = JObject.Parse(a);
                return (JArray) x["history"];
            }
            catch (Exception ex) {
                Log.Error(ex.Message);
                return null;
            }
        }
        
        public TMTrade[] GetTradeList()
        {
            try
            {
                string a = ExecuteApiRequest("/api/Trades/?key=" + Api);
                JArray json = JArray.Parse(a);
                TMTrade[] arr = new TMTrade[json.Count];
                int iter = 0;
                foreach (var thing in json)
                {
                    //Console.WriteLine("{0}", thing);
                    TMTrade xx = JsonConvert.DeserializeObject<TMTrade>(thing.ToString());
                    arr[iter++] = xx;
                }
                return arr;
            }
            catch
            {
                return null;
            }
        }

        public float GetMoney() {
            string temp = ExecuteApiRequest("/api/GetMoney/?key=" + Api);
            JObject temp2 = JObject.Parse(temp);
            return float.Parse((string)temp2["money"]);
        }
    }
}
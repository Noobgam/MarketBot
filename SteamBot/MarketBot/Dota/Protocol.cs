using System;
using System.Threading.Tasks;
using System.Web;
using System.Net;
using Newtonsoft.Json.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.ComponentModel;
using SteamBot.SteamGroups;
using SteamKit2;
using WebSocket4Net;
using SteamTrade;
using SteamKit2.Internal;
using SteamTrade.TradeOffer;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using System.Collections.Specialized;

namespace NDota2Market
{
    public class Dota2Market
    {
#if DEBUG
        public int totalwasted = 0;
#endif
        public Logic Logic;

        private const int MINORCYCLETIMEINTERVAL = 30000;
        string Api = "rQrm3yrEI48044Q0jCv7l3M7KMo1Cjn";
        public Utility.MarketLogger Log;

        private string ExecuteApiRequest(string url)
        {
            using (WebClient myWebClient = new WebClient())
            {
                NameValueCollection myQueryStringCollection = new NameValueCollection();
                myQueryStringCollection.Add("q", "");
                myWebClient.QueryString = myQueryStringCollection;
                return myWebClient.DownloadString("https://market.dota2.net" + url);
            }
        }
        
        bool died = true;
        WebSocket socket = new WebSocket("wss://wsn.dota2.net/wsn/");
        public Dota2Market()
        {
            Thread starter = new Thread(new ThreadStart(StartUp));
            starter.Start();
        }
        
        private void StartUp()
        {
            while (Logic == null)
                Thread.Sleep(10);
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
                case "newitems_cs":
                    {
                        NewItem newItem = JsonConvert.DeserializeObject<NewItem>(x.data);
                        newItem.ui_price = newItem.ui_price * 100 + 0.5f;
                        if (Logic.WantToBuy(newItem))
                        {
                            if (Buy(newItem))
                                Log.Success("Purchased: " + newItem.i_market_name + " " + newItem.ui_price);
                            else
                                Log.Warn("Couldn\'t purchase " + newItem.i_market_name + " " + newItem.ui_price);
                        }
                        break;
                    }
                case "history_cs":
                    {
                        try
                        {
                            char[] trimming = { '[', ']' };
                            x.data = DecodeEncodedNonAsciiCharacters(x.data);
                            //Console.WriteLine(x.data);
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
                            else
                            {
                                throw new Exception(x.data + " is not a valid history item.");
                            }
                            Logic.ProcessItem(historyItem);
                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine(ex.Message);
                            Console.ForegroundColor = ConsoleColor.White;
                        }
                        break;
                    }
                default:
                    Console.WriteLine(x.type);
                    x.data = DecodeEncodedNonAsciiCharacters(x.data);
                    Console.WriteLine(x.data);
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

        bool TakeItems()
        {
            Console.WriteLine("Taking items");
            JObject json = JObject.Parse(ExecuteApiRequest("/api/ItemRequest/in/1/?key=" + Api));
            if (json["success"] == null)
                return false;
            else if ((bool)json["success"])
            {
                return true;
            }
            else
                return false;
        }

        bool GiveItems(string botID)
        {
            Console.WriteLine("Giving items");
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
                            GiveItems(arr[i].ui_bid);
                            gone = true;
                            Logic.doNotSell = true;
                            break;
                        }
                        had |= arr[i].ui_status == "2";
                    }
                    if (had && !gone)
                    {
                        UpdateInventory();
                        TakeItems();
                    }
                }
                catch (Exception ex)
                {

                }
                //once per 30 seconds we check trade list
                Thread.Sleep(MINORCYCLETIMEINTERVAL);
            }
        }

        void Subscribe()
        {
            socket.Send("newitems_cs");
            socket.Send("history_cs");
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
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Connection opened!");
            Console.ForegroundColor = ConsoleColor.White;
            Auth();
            Thread ping = new Thread(new ThreadStart(pinger));
            ping.Start();
            Thread tradeHandler = new Thread(new ThreadStart(HandleTrades));
            tradeHandler.Start();
        }

        void Error(object sender, EventArgs e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error");
            died = true;
            ReOpen();
            Console.ForegroundColor = ConsoleColor.White;
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
                Console.WriteLine("Trying to reconnect for the %d-th time", i + 1);
            }
        }


        public bool Buy(NewItem item)
        {
#if DEBUG
            totalwasted += (int)item.ui_price;
            Console.WriteLine("Purchased an item for {0}, total wasted {1}", ((int)item.ui_price + .0) / 100, (totalwasted + .0) / 100);
            return true;
#else
            string a = ExecuteApiRequest("/api/Buy/" + item.i_classid + "_" + item.i_instanceid + "/" + ((int)item.ui_price).ToString() + "/?key=" + Api);
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

        //Interface starts here:
        [System.Obsolete("Specify item, it will parce it by itself.")]
        public bool Buy(string ClasssId, string InstanceId, int price)
        {
#if DEBUG
            totalwasted += price;
            Console.WriteLine("Purchased an item for {0}, total wasted {1}", (price + .0) / 100, (totalwasted + .0) / 100);
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

        public bool Sell(string ClasssId, string InstanceId, int price)
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
            //foreach (var thing in json)
            //..Console.WriteLine("{0}: {1}", thing.Key, thing.Value);
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
                //foreach (var thing in json)
                //    Console.WriteLine("{0}: {1}", thing.Key, thing.Value);
                //cout<<;
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

        public int getBestOrder(string classid, string instanceid)
        {
            try
            {
                string a = ExecuteApiRequest("/api/ItemInfo/" + classid + "_" + instanceid + "/ru/?key=" + Api);
                JObject x = JObject.Parse(a);
                JArray thing = (JArray)x["buy_offers"];
                if (thing == null || thing.Count == 0)
                    return 49;
                else
                    return int.Parse(((string)thing[0]["o_price"]));
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        TMTrade[] GetTradeList()
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
    }
}
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

namespace CSGOTM
{
    public class CSGOTMProtocol
    {
#if DEBUG
        public int totalwasted = 0;
#endif
        public SortedSet<string> Codes;
        public Queue<TradeOffer> QueuedOffers;
        public SteamBot.Bot Parent;
        public Logic Logic;
        string Api = "5gget2u8B096IK48lJMyX6d91s2t05n";
        public CSGOTMProtocol()
        {

        }
        bool died = true;
        WebSocket socket = new WebSocket("wss://wsn.dota2.net/wsn/");
        public CSGOTMProtocol(SortedSet<string> temp)
        {
            Codes = temp;
            Thread starter = new Thread(new ThreadStart(StartUp));
            starter.Start();
        }
        public CSGOTMProtocol(SteamBot.Bot p, SortedSet<string> temp)
        {
            Parent = p;
            Codes = temp;
            Thread starter = new Thread(new ThreadStart(StartUp));
            starter.Start();
            QueuedOffers = new Queue<TradeOffer>();
            Thread offerHandler = new Thread(new ThreadStart(OfferHandlerMain));
            offerHandler.Start();
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

        public bool HandleOffer(TradeOffer offer)
        {
            try
            {
                Parent.TradeCount++;
                switch (offer.OfferState)
                {
                    case TradeOfferState.TradeOfferStateAccepted:
                        {
                            if (Parent.TradeCount <= 1000)
                                return false;
                            Parent.SecurityCodesForOffers.Remove(offer.Message);
                            return false;
                        }
                    case TradeOfferState.TradeOfferStateActive:
                        {
                            if (Parent.TradeCount <= 1000)
                            {
                                offer.Decline();
                                return false;
                            }
                            var their = offer.Items.GetTheirItems();
                            var my = offer.Items.GetMyItems();
                            if (my.Count > 0 && !Parent.CheckOffer(offer)) //if the offer is bad we decline it. 
                            {
                                offer.Decline();
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Offer failed.");
                                Console.WriteLine("[Reason]: Invalid trade request.");
                                Console.ForegroundColor = ConsoleColor.White;
                                return false;
                            }
                            else if (offer.Accept().Accepted)
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine("Offer completed.");
                                if (their.Count != 0)
                                    Console.WriteLine("Received: " + their.Count + " items.");
                                if (my.Count != 0)
                                    Console.WriteLine("Lost:     " + my.Count + " items.");
                                Console.ForegroundColor = ConsoleColor.White;
                                if (offer.Items.GetMyItems().Count != 0)
                                {
                                    Thread.Sleep(1000);
                                    Parent.AcceptAllMobileTradeConfirmations();
                                    return true;
                                }
                                return true;
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine("Offer failed.");
                                Console.WriteLine("[Reason]: Unknown error.");
                                Console.ForegroundColor = ConsoleColor.White;
                                return false;
                            }
                        }
                    case TradeOfferState.TradeOfferStateNeedsConfirmation:
                        Parent.SecurityCodesForOffers.Remove(offer.Message);
                        return false;
                    //Parent.AcceptAllMobileTradeConfirmations(); //will it work? I guess it is supposed to...
                    case TradeOfferState.TradeOfferStateInEscrow:
                        Parent.SecurityCodesForOffers.Remove(offer.Message);
                        return false;
                    //Parent.AcceptAllMobileTradeConfirmations(); //UHHHHHHHH????
                    //Trade is still active but incomplete
                    case TradeOfferState.TradeOfferStateCountered:
                        Parent.Log.Info($"Trade offer {offer.TradeOfferId} was countered");
                        return false;
                    case TradeOfferState.TradeOfferStateCanceled:
                        Parent.SecurityCodesForOffers.Remove(offer.Message);
                        return false;
                    case TradeOfferState.TradeOfferStateDeclined:
                        Parent.SecurityCodesForOffers.Remove(offer.Message);
                        return false;
                    default:
                        Parent.Log.Info($"Trade offer {offer.TradeOfferId} failed");
                        Parent.Log.Info($"Number of trades: {Parent.TradeCount}");
                        return false;
                }
            }
            catch (Exception e)
            {
                return false;
            }
        }

        public void EnqueueOffer(TradeOffer offer)
        {
            QueuedOffers.Enqueue(offer);
        }

        public void OfferHandlerMain()
        {
            Thread.Sleep(5000);
            while (true)
            {
                if (QueuedOffers.Count > 0)
                {
                    TradeOffer front = QueuedOffers.Dequeue();
                    //Console.WriteLine("Dequeued offer");
                    if (HandleOffer(front))
                        Thread.Sleep(10000);
                }
                else
                {
                    Thread.Sleep(100);
                }
            }
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
                        Buy(newItem.i_classid, newItem.i_instanceid, (int)newItem.ui_price);
                        Console.WriteLine(newItem.i_market_name + " " + newItem.ui_price);
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
                        historyItem.i_classid = arr[0];
                        historyItem.i_instanceid = arr[1];
                        historyItem.i_market_hash_name = arr[2];
                        historyItem.timesold = arr[3];
                        historyItem.price = Int32.Parse(arr[4]);
                        historyItem.i_market_name = arr[5];
                        Logic.ProcessItem(historyItem);
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(ex.Message);
                        Console.ForegroundColor = ConsoleColor.White;
                    }
                    break;

                case "invcache_go":
                    Console.WriteLine("Inventory was cached");
                    break;
                case "money":
                    //Console.ForegroundColor = ConsoleColor.Yellow;
                    //Console.WriteLine("Current balance: %f", Double.Parse(x.data.Split('<')[0]));
                    //Console.ForegroundColor = ConsoleColor.White;
                    break;
                default:
                    //Console.WriteLine(x.type);
                    x.data = DecodeEncodedNonAsciiCharacters(x.data);
                    Console.WriteLine(x.data);
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

        bool TakeItems()
        {
            Console.WriteLine("Taking items");
            using (WebClient myWebClient = new WebClient())
            {
                NameValueCollection myQueryStringCollection = new NameValueCollection();
                myQueryStringCollection.Add("q", "");
                myWebClient.QueryString = myQueryStringCollection;
                string a = myWebClient.DownloadString("https://csgo.tm/api/ItemRequest/in/1/?key=" + Api);
                JObject json = JObject.Parse(a);
                //foreach (var thing in json)
                //    Console.WriteLine("{0}: {1}", thing.Key, thing.Value);
                //cout<<;
                if (json["success"] == null)
                    return false;
                else if ((bool)json["success"])
                {
                    Codes.Add((string)json["secret"]);
                    return true;
                }
                else
                    return false;
            }
        }

        bool GiveItems(string botID)
        {
            Console.WriteLine("Giving items");
            using (WebClient myWebClient = new WebClient())
            {
                NameValueCollection myQueryStringCollection = new NameValueCollection();
                myQueryStringCollection.Add("q", "");
                myWebClient.QueryString = myQueryStringCollection;
                string a = myWebClient.DownloadString("https://csgo.tm/api/ItemRequest/out/" + botID + "/?key=" + Api);
                JObject json = JObject.Parse(a);
                //foreach (var thing in json)
                //    Console.WriteLine("{0}: {1}", thing.Key, thing.Value);
                //cout<<;
                if (json["success"] == null)
                    return false;
                else if ((bool)json["success"])
                {
                    Codes.Add((string)json["secret"]);
                    return true;
                }
                else
                    return false;
            }

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
            using (WebClient myWebClient = new WebClient())
            {
                NameValueCollection myQueryStringCollection = new NameValueCollection();
                myQueryStringCollection.Add("q", "");
                myWebClient.QueryString = myQueryStringCollection;
                string a = myWebClient.DownloadString("https://csgo.tm/api/GetWSAuth/?key=" + Api);
                Auth q = JsonConvert.DeserializeObject<Auth>(a);
                socket.Send(q.wsAuth);
                Subscribe();
            }
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
            //andrew is gay
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

        //Interface starts here:
        public bool Buy(string ClasssId, string InstanceId, int price)
        {
#if DEBUG
            totalwasted += price;
            Console.WriteLine("Purchased an item for {0}, total wasted {1}", (price + .0) / 100, (totalwasted + .0) / 100);
            return true;
#else
            using (WebClient myWebClient = new WebClient())
            {
                NameValueCollection myQueryStringCollection = new NameValueCollection();
                myQueryStringCollection.Add("q", "");
                myWebClient.QueryString = myQueryStringCollection;
                string a = myWebClient.DownloadString("https://csgo.tm/api/Buy/" + ClasssId + "_" + InstanceId + "/" + price.ToString() + "/?key=" + Api);
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
            }
#endif
        }

        public bool Sell(string ClasssId, string InstanceId, int price)
        {
#if DEBUG //sorry nothing is implemented there, I don't really know what to write as debug
            return false;

#else
            using (WebClient myWebClient = new WebClient())
            {
                NameValueCollection myQueryStringCollection = new NameValueCollection();
                myQueryStringCollection.Add("q", "");
                myWebClient.QueryString = myQueryStringCollection;
                string a = myWebClient.DownloadString("https://csgo.tm/api/SetPrice/new_" + ClasssId + "_" + InstanceId + "/" + price.ToString() + "/?key=" + Api);
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
#endif
        }

        public Inventory GetSteamInventory()
        {
            using (WebClient myWebClient = new WebClient())
            {
                NameValueCollection myQueryStringCollection = new NameValueCollection();
                myQueryStringCollection.Add("q", "");
                myWebClient.QueryString = myQueryStringCollection;
                string a = myWebClient.DownloadString("https://market.csgo.com/api/GetInv/?key=" + Api);
                JObject json = JObject.Parse(a);
                Inventory inventory = new Inventory();
                inventory.content = new List<Inventory.SteamItem>();
                if (json["ok"] != null && (bool)json["ok"] == false)
                    return inventory;
                inventory.content = json["data"].ToObject<List<Inventory.SteamItem>>();
                return inventory;
            }
        }

        public bool SetOrder(string classid, string instanceid, int price)
        {
#if DEBUG   
            return false;
#else
            using (WebClient myWebClient = new WebClient())
            {
                NameValueCollection myQueryStringCollection = new NameValueCollection();
                myQueryStringCollection.Add("q", "");
                myWebClient.QueryString = myQueryStringCollection;
                string a = myWebClient.DownloadString("https://market.csgo.com/api/ProcessOrder/" + classid + "/" + instanceid + "/" + price.ToString() + "/?key=" + Api);
                JObject json = JObject.Parse(a);
                //foreach (var thing in json)
                //..Console.WriteLine("{0}: {1}", thing.Key, thing.Value);
                if (json["success"] == null)
                    return false;
                else if ((bool)json["success"])
                    return true;
                else
                    return false;
            }
#endif
        }

        bool UpdateInventory()
        {
            try
            {
                using (WebClient myWebClient = new WebClient())
                {
                    NameValueCollection myQueryStringCollection = new NameValueCollection();
                    myQueryStringCollection.Add("q", "");
                    myWebClient.QueryString = myQueryStringCollection;
                    string a = myWebClient.DownloadString("https://csgo.tm/api/UpdateInventory/?key=" + Api);
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
                using (WebClient myWebClient = new WebClient())
                {
                    NameValueCollection myQueryStringCollection = new NameValueCollection();
                    myQueryStringCollection.Add("q", "");
                    myWebClient.QueryString = myQueryStringCollection;
                    string a = myWebClient.DownloadString("https://csgo.tm/api/ItemInfo/" + classid + "_" + instanceid + "/ru/?key=" + Api);
                    JObject x = JObject.Parse(a);
                    JArray thing = (JArray)x["buy_offers"];
                    if (thing == null || thing.Count == 0)
                        return 49;
                    else
                        return int.Parse(((string)thing[0]["o_price"]));
                }
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
                using (WebClient myWebClient = new WebClient())
                {
                    NameValueCollection myQueryStringCollection = new NameValueCollection();
                    myQueryStringCollection.Add("q", "");
                    myWebClient.QueryString = myQueryStringCollection;
                    string a = myWebClient.DownloadString("https://csgo.tm/api/Trades/?key=" + Api);
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
            }
            catch
            {
                return null;
            }
        }
    }
}
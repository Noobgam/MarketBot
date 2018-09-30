﻿using System;
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
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using SteamBot.MarketBot.Utility;
using System.Diagnostics;
using System.IO;
using System.Net;
using Utility;

namespace CSGOTM {
    public class Protocol {
#if CAREFUL
        public int totalwasted = 0;
#endif
        public MarketLogger Log;
        private Queue<TradeOffer> QueuedOffers;
        public Logic Logic;
        public SteamBot.Bot Bot;
        private int money = 0;
        private readonly Random Generator = new Random();
        private readonly string Api;
        private SemaphoreSlim ApiSemaphore = new SemaphoreSlim(10);
        private TMBot parent;
        private string CurrentToken = "";


        //TODO(noobgam): make it great again, probably some of them can be united.
        public enum ApiMethod {
            GetTradeList,
            GetBestOrder,
            GenericMassInfo,
            GenericMassSetPriceById,
            Buy,
            Sell,
            UnstickeredMassInfo,
            UnstickeredMassSetPriceById,
            SetOrder,
            MinPrice,
            GetSteamInventory,
            GenericCall,
            UpdateInventory,
            GetMoney,
            PingPong = GetMoney,
            ItemRequest = UpdateInventory,
        }

        readonly Dictionary<ApiMethod, double> rpsLimit = new Dictionary<ApiMethod, double> {
            { ApiMethod.UnstickeredMassInfo, 1.5 },
            { ApiMethod.UnstickeredMassSetPriceById, 1.5 },
            { ApiMethod.Sell, 3 },
            { ApiMethod.Buy, 3 },
        };

        private Dictionary<ApiMethod, SemaphoreSlim> rpsRestricter = new Dictionary<ApiMethod, SemaphoreSlim>();
        private Dictionary<ApiMethod, int> rpsDelay = new Dictionary<ApiMethod, int>();

        private void ObtainApiSemaphore(ApiMethod method) {
            rpsRestricter[method].Wait();
            ApiSemaphore.Wait();
        }

        private void ReleaseApiSemaphore(ApiMethod method) {
            Task.Delay(rpsDelay[method])
                .ContinueWith(tks => rpsRestricter[method].Release());
            Task.Delay(Consts.SECOND)
                .ContinueWith(tsk => ApiSemaphore.Release());
        }

        double ALP = 2.0 / 80;
        double EMA = 0.0;
        void ShiftEma(double x) {
            EMA = EMA * (1 - ALP) + x * ALP;
        }

        private string ExecuteApiRequest(string url, ApiMethod method = ApiMethod.GenericCall) {
            string response = null;
            Stopwatch temp = new Stopwatch();
            try {
                ObtainApiSemaphore(method);
                //Log.Success("Executing api call " + url);
                temp.Start();
                response = Utility.Request.Get(Consts.MARKETENDPOINT + url);
                temp.Stop();
                ShiftEma(0);
                //File.AppendAllText($"get_log{Logic.botName}", $"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss")} ,{url} : {temp.ElapsedMilliseconds}\n");
            } catch (Exception ex) {
                Log.ApiError(TMBot.RestartPriority.UnknownError, $"GET call to {Consts.MARKETENDPOINT}{url} failed");
                if (ex is WebException webex) {
                    if (webex.Status == WebExceptionStatus.ProtocolError) {
                        if (webex.Response is HttpWebResponse resp) {
                            if ((int)resp.StatusCode == 500 || (int)resp.StatusCode == 520 || (int)resp.StatusCode == 521) {
                                Log.ApiError(TMBot.RestartPriority.MediumError, $"Status code: {(int)resp.StatusCode}");
                            }
                        }
                    }
                } else {
                    Log.ApiError(TMBot.RestartPriority.BigError, $"Message: {ex.Message}\nTrace: {ex.StackTrace}");
                }
                ShiftEma(1);
            } finally {
                ReleaseApiSemaphore(method);
            }
            Console.WriteLine((100 * EMA).ToString("n2"));
            if (response == "{\"error\":\"Bad KEY\"}") {
                Log.ApiError(TMBot.RestartPriority.CriticalError, "Bad key");
                return null;
            }
            return response;
        }

        /// <summary>
        /// [Leaked resource from csgotm network tab]
        /// https://market.csgo.com/ajax/i_popularity/all/all/all/1/56/0;100000/all/all/all --- sample query
        /// Returns popular items, format: [[cid, iid, market_name, lowest_price (RUB), color (or false if doesn't apply), CHEAPER_THAN_STEAM, unknown array (usually empty)]]
        /// </summary>
        /// <param name="page">page to return</param>
        /// <param name="amount">amount of items (can't be above 96)</param>
        /// <returns></returns>
        public JArray PopularItems(int page = 1, int amount = 56, int lowest_price = 0, int highest_price = 100000, bool any_stickers = false) {
            if (any_stickers) {
                return JArray.Parse(Utility.Request.Get($"{Consts.MARKETENDPOINT}/ajax/i_popularity/all/all/all/{page}/{amount}/{lowest_price};{highest_price}/all/all/-1"));
            } else {
                return JArray.Parse(Utility.Request.Get($"{Consts.MARKETENDPOINT}/ajax/i_popularity/all/all/all/{page}/{amount}/{lowest_price};{highest_price}/all/all/all"));
            }
        }

        private string ExecuteApiPostRequest(string url, string data, ApiMethod method = ApiMethod.GenericCall) {
            Stopwatch temp = new Stopwatch();
            string response = null;
            try {
                ObtainApiSemaphore(method);
                //Log.Success("Executing api call " + url);
                temp.Start();
                response = Utility.Request.Post(Consts.MARKETENDPOINT + url, data);
                temp.Stop();
                ShiftEma(0);
                //File.AppendAllText($"post_log{Logic.botName}", $"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss")} ,{url} : {temp.ElapsedMilliseconds}\n");
            } catch (Exception ex) {
                Log.ApiError(TMBot.RestartPriority.UnknownError, $"POST call to {Consts.MARKETENDPOINT}{url} failed");
                if (ex is WebException webex) {
                    if (webex.Status == WebExceptionStatus.ProtocolError) {
                        if (webex.Response is HttpWebResponse resp) {
                            if ((int)resp.StatusCode == 500 || (int)resp.StatusCode == 520 || (int)resp.StatusCode == 521) {
                                Log.ApiError(TMBot.RestartPriority.MediumError, $"Status code: {(int)resp.StatusCode}");
                            }
                        }
                    }
                } else {
                    Log.ApiError(TMBot.RestartPriority.BigError, $"Message: {ex.Message}\nTrace: {ex.StackTrace}");
                }
                ShiftEma(1);
            } finally {
                ReleaseApiSemaphore(method);
            }
            if (response == "{\"error\":\"Bad KEY\"}") {
                Log.ApiError(TMBot.RestartPriority.CriticalError, "Bad key");
                return null;
            }
            return response;
        }

        bool opening = false;
        bool died = true;
        WebSocket socket;
        public Protocol(TMBot bot) {
            parent = bot;
            Api = bot.config.Api;
            this.Bot = bot.bot;
            InitializeRPSSemaphores();
            Task.Run((Action)StartUp);
        }

        //I'm going to use different semaphores to allow some requests to have higher RPS than others, without bothering one another.

        private void GenerateSemaphore(ApiMethod method, double rps) {
            rpsRestricter[method] = new SemaphoreSlim(Consts.GLOBALRPSLIMIT);
            rpsDelay[method] = (int)Math.Ceiling(1000.0 / rps);
        }

        private void InitializeRPSSemaphores() {
            double totalrps = 0;
            foreach (ApiMethod method in ((ApiMethod[])Enum.GetValues(typeof(ApiMethod))).Distinct()) {
                bool temp = rpsLimit.TryGetValue(method, out double limit);
                if (temp) {
                    GenerateSemaphore(method, limit);
                } else {
                    limit = Consts.DEFAULTRPS;
                    GenerateSemaphore(method, limit);
                }
                totalrps += limit;
            }
            if (totalrps > Consts.GLOBALRPSLIMIT) {
                //Log.Info($"Total RPS of {totalrps} exceeds {Consts.GLOBALRPSLIMIT}.");
            }
        }

        private void StartUp() {
            while (!parent.ReadyToRun) {
                Thread.Sleep(10);
            }
            while (Logic == null || Bot.IsLoggedIn == false)
                Thread.Sleep(10);
            QueuedOffers = new Queue<TradeOffer>();
            money = GetMoney();
            Task.Run((Action)PingPong);
            Task.Run((Action)ReOpener);
            Task.Run((Action)HandleTrades);
            Task.Run(() => {
                OrdersCall(order => {
                    lock (ordersLock)
                        orders[$"{order.i_classid}_{order.i_instanceid}"] = int.Parse(order.o_price);
                });
            });
            AllocSocket();
            OpenSocket();
        }

        private void AllocSocket() {
            if (socket != null) {
                socket.Dispose();
            }
            socket = new WebSocket("wss://wsn.dota2.net/wsn/", receiveBufferSize: 65536);
        }

        private void OpenSocket() {
            opening = true;
            socket.Opened += Open;
            socket.Error += Error;
            socket.Closed += Close;
            socket.MessageReceived += Msg;
            socket.Open();
        }

        public readonly string[] search = { "<div class=\\\"price\\\"", "<div class=\\\"name\\\"" };

        public class Dummy {
            public string s;
        }

        string parse(string s) {
            var sb = new StringBuilder();
            sb.Append("{\"s\":\"" + s + "\"}");
            string t = sb.ToString();
            var dummy = JsonConvert.DeserializeObject<Dummy>(t);
            return dummy.s;
        }

        //DateTime start = DateTime.Now;
        //double counter = 0;

        void Msg(object sender, MessageReceivedEventArgs e) {
            try {
                if (e.Message == "pong")
                    return;
                string type = string.Empty;
                string data = string.Empty;
                JsonTextReader reader = new JsonTextReader(new StringReader(e.Message));
                string currentProperty = string.Empty;
                while (reader.Read()) {
                    if (reader.Value != null) {
                        if (reader.TokenType == JsonToken.PropertyName)
                            currentProperty = reader.Value.ToString();
                        else if (reader.TokenType == JsonToken.String) {
                            if (currentProperty == "type")
                                type = reader.Value.ToString();
                            else
                                data = reader.Value.ToString();
                        }
                    }
                }
                //Console.WriteLine(x.type);
                switch (type) {
                    case "newitems_go":
                        //++counter;
                        //double rps = counter / DateTime.Now.Subtract(start).TotalSeconds;
                        //Console.WriteLine(rps);
                        reader = new JsonTextReader(new StringReader(data));
                        currentProperty = string.Empty;
                        NewItem newItem = new NewItem();
                        while (reader.Read()) {
                            if (reader.Value != null) {
                                if (reader.TokenType == JsonToken.PropertyName)
                                    currentProperty = reader.Value.ToString();
                                else if (reader.TokenType == JsonToken.String) {
                                    switch (currentProperty) {
                                        case "i_classid":
                                            newItem.i_classid = long.Parse(reader.Value.ToString());
                                            break;
                                        case "i_instanceid":
                                            newItem.i_instanceid = long.Parse(reader.Value.ToString());
                                            break;
                                        case "i_market_name":
                                            newItem.i_market_name = reader.Value.ToString();
                                            break;
                                        default:
                                            break;
                                    }
                                } else if (currentProperty == "ui_price") {
                                    newItem.ui_price = (int)(float.Parse(reader.Value.ToString()) * 100);

                                }
                            }
                        }
                        if (newItem.i_market_name == "") {
                            Log.Warn("Socket item has no market name");
                            break;
                        }
                        //getBestOrder(newItem.i_classid, newItem.i_instanceid);
                        if (!Logic.sellOnly && Logic.WantToBuy(newItem)) {
                            if (Buy(newItem))
                                Log.Success("Purchased: " + newItem.i_market_name + " " + newItem.ui_price);
                            else
                                Log.Warn("Couldn\'t purchase " + newItem.i_market_name + " " + newItem.ui_price);
                        }
                        break;
                    case "history_go":
                        try {
                            char[] trimming = { '[', ']' };
                            data = Encode.DecodeEncodedNonAsciiCharacters(data);
                            data = data.Replace("\\", "").Replace("\"", "").Trim(trimming);
                            string[] arr = data.Split(',');
                            NewHistoryItem historyItem = new NewHistoryItem();
                            if (arr.Length == 7) {
                                historyItem.i_classid = long.Parse(arr[0]);
                                historyItem.i_instanceid = long.Parse(arr[1]);
                                historyItem.price = Int32.Parse(arr[4]);
                                historyItem.i_market_name = arr[5];
                            } else if (arr.Length == 8) {
                                historyItem.i_classid = long.Parse(arr[0]);
                                historyItem.i_instanceid = long.Parse(arr[1]);
                                historyItem.price = Int32.Parse(arr[5]);
                                historyItem.i_market_name = arr[6];
                            } else {
                                historyItem.i_classid = long.Parse(arr[0]);
                                historyItem.i_instanceid = long.Parse(arr[1]);
                                historyItem.price = Int32.Parse(arr[5]);
                                historyItem.i_market_name = arr[6] + "," + arr[7];
                            }
                            Logic.ProcessItem(historyItem);
                        } catch (Exception ex) {
                            Log.Error(ex.Message);
                        }
                        break;

                    case "invcache_go":
                        Log.Info("Inventory was cached");
                        break;
                    case "money":
                        try {
                            money = (int)(double.Parse(data.Split('\"')[1].Split('<')[0], new CultureInfo("en")) * 100);
                        } catch {
                            Log.Error($"Can't parse money from {data} [{data.Split('\"')[1].Split('<')[0]}]");
                            money = 90000;
                        }
                        break;
                    case "additem_go":
                        break;
                    case "itemstatus_go":
                        JObject json = JObject.Parse(data);
                        if ((int)json["status"] == 5)
                            Logic.doNotSell = true;
                        break;
                    default:
                        //Log.Info(JObject.Parse(e.Message).ToString(Formatting.Indented));
                        //Console.WriteLine(x.type);
                        //data = DecodeEncodedNonAsciiCharacters(data);
                        //Log.Info(data);
                        break;
                }
            } catch (Exception ex) {
                Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
            } finally {
            }
        }

        void SocketPinger() {
            while (!died) {
                try {
                    socket.Send("ping");
                } catch (Exception ex) {
                    Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                }
                Thread.Sleep(30000);
            }
        }

        void RequestPurchasedItems(IEnumerable<TMTrade> trades) {
            Log.Info("Requesting items");
            foreach (TMTrade trade in trades.OrderBy(trade => trade.offer_live_time)) {
                string resp = ExecuteApiRequest($"/api/ItemRequest/in/{trade.ui_bid}/?key=" + Api, ApiMethod.ItemRequest);
                if (resp == null)
                    continue;
                JObject json = JObject.Parse(resp);
                if (json["success"] == null)
                    continue;
                if ((bool)json["manual"] == true) {
                    Log.Info(json.ToString(Formatting.None));
                } else if ((bool)json["success"]) {
                    continue;
                } else
                    continue;
            }
        }

        private Dictionary<string, DateTime> sentTrades = new Dictionary<string, DateTime>();

        void SendSoldItems(IEnumerable<TMTrade> trades) {
            int sent = 0;
            List<Pair<string, string>> list = new List<Pair<string, string>>();
            foreach (TMTrade trade in trades.OrderBy(trade => trade.offer_live_time)) {
                if (sentTrades.TryGetValue(trade.ui_bid, out DateTime lastTradeTime)) {
                    DateTime tmp = DateTime.Now;
                    if (tmp.Subtract(lastTradeTime).Seconds < 50) //no reason to, chances are it's either fine anyway or is a scam.
                        continue;
                }
                Debug.Assert(trade.ui_status == "2");
                string resp = ExecuteApiRequest($"/api/ItemRequest/in/{trade.ui_bid}/?key=" + Api, ApiMethod.ItemRequest);
                if (resp == null)
                    continue;
                JObject json = JObject.Parse(resp);
                Thread.Sleep(1000);
                if (json["success"] == null)
                    continue;
                if ((bool)json["success"] == false) {
                    Log.ApiError(TMBot.RestartPriority.SmallError, (string)json["error"]);
                } else if ((bool)json["success"]) {
                    if ((bool)json["manual"]) {
                        string requestId = "";
                        try {
                            requestId = (string)json["requestId"];
                        } catch {
                            Log.Error("Could not parse request id");
                            Log.Error("Extra info: " + json.ToString(Formatting.None));
                        }
                        string profile = (string)json["profile"];
                        ulong id = ulong.Parse(profile.Split('/')[4]);

                        Log.Info(json.ToString(Formatting.None));
                        var offer = Bot.NewTradeOffer(new SteamID(id));
                        try {
                            foreach (JObject item in json["request"]["items"]) {
                                offer.Items.AddMyItem(
                                    (int)item["appid"],
                                    (long)item["contextid"],
                                    (long)item["assetid"],
                                    (long)item["amount"]);
                            }
                            Log.Info("Partner: {0}\nToken: {1}\nTradeoffermessage: {2}\nProfile: {3}", (string)json["request"]["partner"], (string)json["request"]["token"], (string)json["request"]["tradeoffermessage"], (string)json["profile"]);
                            if (offer.Items.NewVersion) {
                                if (offer.SendWithToken(out string newOfferId, (string)json["request"]["token"], (string)json["request"]["tradeoffermessage"])) {
                                    Log.Success("Trade offer sent : Offer ID " + newOfferId);
                                    list.Add(new Pair<string, string>(requestId, newOfferId));
                                    ++sent;
                                    sentTrades[trade.ui_bid] = DateTime.Now;
                                    Thread.Sleep(1000);
                                } else {
                                    Log.Error("Trade offer was not sent!"); //TODO(noobgam): don't accept confirmations if no offers were sent
                                }
                            } else {
                                Log.Error("Items.NewVersion = 0! Still trying to send.");
                                if (offer.SendWithToken(out string newOfferId, (string)json["request"]["token"], (string)json["request"]["tradeoffermessage"])) {
                                    Log.Success("Trade offer sent : Offer ID " + newOfferId);
                                    list.Add(new Pair<string, string>(requestId, newOfferId));
                                    ++sent;
                                    sentTrades[trade.ui_bid] = DateTime.Now;
                                    Thread.Sleep(2000);
                                } else {
                                    Log.Error(TMBot.RestartPriority.CriticalError, "Trade offer was not sent!"); //TODO(noobgam): don't accept confirmations if no offers were sent
                                }
                            }
                        } catch (Exception ex) {
                            Log.Error(ex.Message);
                            Thread.Sleep(5000); //sleep tight, steam probably went 500
                        }
                    }
                    else {
                        sent = 1; //?
                    }
                }
            }
            if (sent > 0)
                Task.Delay(3000) //the delay might fix #35
                    .ContinueWith(tsk => {
                        Bot.AcceptAllMobileTradeConfirmations();
                        foreach (Pair<string, string> pr in list) {
                            Thread.Sleep(1000);
                            ReportCreatedTrade(pr.First, pr.Second);
                        }
                    });
        }

        bool ReportCreatedTrade(string requestId, string tradeOfferId) {
            if (requestId == "") {
                return false;
            }
            try {
                string resp = ExecuteApiRequest($"/api/ReportCreatedTrade/{requestId}/{tradeOfferId}?key={Api}");
                if (resp == null)
                    return false;
                JObject json = JObject.Parse(resp);
                if (json["success"] == null) {
                    Log.Error("TM thinks that I did not send the offer.");
                    return false;
                } else if ((bool)json["success"]) {
                    Log.Success("TM knows about my offer");
                    return true;
                } else {
                    Log.Error("TM thinks that I did not send the offer.");
                    return false;
                }
            } catch (Exception ex) {
                Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                return false;
            }
        }

        bool RequestPurchasedItems(string botID) {
            Log.Info("Requesting items");
            try {
                string resp = ExecuteApiRequest("/api/ItemRequest/out/" + botID + "/?key=" + Api, ApiMethod.ItemRequest);
                if (resp == null)
                    return false;
                JObject json = JObject.Parse(resp);
                if (json["success"] == null)
                    return false;
                else if ((bool)json["success"]) {
                    return true;
                } else
                    return false;
            } catch (Exception ex) {
                Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                return false;
            }
        }

        enum ETradesStatus {
            Unhandled = 0,
            SellHandled = 1,
            BuyHandled = 2,
            Handled = 3
        }

        readonly object TradeCacheLock = new object();
        TMTrade[] CachedTrades = new TMTrade[0];
        AutoResetEvent waitHandle = new AutoResetEvent(false);
        ETradesStatus status = ETradesStatus.Handled;


        private void RefreshToken() {
            while (parent.IsRunning()) {
                JObject temp;
                try {
                    temp = LocalRequest.GetBestToken(parent.config.Username);
                    if ((bool)temp["success"])
                        CurrentToken = (string)temp["token"];
                } catch {
                    Log.Error("Could not get a response from local server");
                }
                Tasking.WaitForFalseOrTimeout(parent.IsRunning, timeout: 30000).Wait();
            }
        }

        private void HandleSoldTrades() {
            while (parent.IsRunning()) {
                waitHandle.WaitOne();
                if ((status & ETradesStatus.SellHandled) == 0) {
                    TMTrade[] soldTrades;
                    lock (TradeCacheLock) {
                        soldTrades = CachedTrades;
                    }
                    soldTrades = soldTrades.Where(t => t.ui_status == "2").ToArray();
                    if (soldTrades.Length != 0) {
                        SendSoldItems(soldTrades);
                        lock (TradeCacheLock) {
                            status |= ETradesStatus.SellHandled;
                        }
                    } else {
                        sentTrades.Clear();
                    }
                }
                if ((status & ETradesStatus.SellHandled) != 0) {
                    Thread.Sleep(30000); //sorry this revokes sessions too often, because TM is retarded.
                    status ^= ETradesStatus.SellHandled;
                }
            }
        }

        void HandlePurchasedTrades() {
            while (parent.IsRunning()) {
                if ((status & ETradesStatus.BuyHandled) == 0) {
                    TMTrade[] boughtTrades;
                    lock (TradeCacheLock) {
                        boughtTrades = CachedTrades;
                        status |= ETradesStatus.BuyHandled;
                    }
                    boughtTrades = boughtTrades.Where(t => t.ui_status == "4").ToArray();
                    if (boughtTrades.Length != 0)
                        RequestPurchasedItems(boughtTrades);
                }
                Thread.Sleep(10000);
            }
        }

        void HandleTrades() {
            Task.Run((Action)HandleSoldTrades);
            //Task.Run((Action)HandlePurchasedTrades);
            while (parent.IsRunning()) {
                try {
                    TMTrade[] arr = GetTradeList();
                    lock (TradeCacheLock) {
                        CachedTrades = arr;
                        waitHandle.Set();
                    }
                    Thread.Sleep(10000);
                    UpdateInventory();
                } catch (Exception ex) {
                    Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                }
                Thread.Sleep(10000);
            }
        }

        void Subscribe() {
            socket.Send("newitems_go");
            socket.Send("history_go");
        }

        bool Auth() {
            string resp = ExecuteApiRequest("/api/GetWSAuth/?key=" + Api);
            if (resp == null)
                return false;
            Auth q = JsonConvert.DeserializeObject<Auth>(resp);
            socket.Send(q.wsAuth);
            Subscribe();
            return true;
        }

        void Open(object sender, EventArgs e) {
            died = false;
            opening = false;
            Log.Success("Connection opened!");
            if (Auth())
                Task.Run((Action)SocketPinger);
            //start = DateTime.Now;
        }

        void Error(object sender, EventArgs e) {
            //Log.Error($"Connection error: " + e.ToString());
        }

        void Close(object sender, EventArgs e) {
            //Log.Error($"Connection closed: " + e.ToString());
            if (!died) {
                died = true;
                socket.Dispose();
                socket = null;
            }
        }

        void ReOpener() {
            int i = 1;
            while (parent.IsRunning()) {
                Thread.Sleep(10000);
                if (died) {
                    if (!opening) {
                        try {
                            Log.ApiError(TMBot.RestartPriority.MediumError, $"Trying to reconnect for the {i++}-th time");
                            AllocSocket();
                            OpenSocket();
                        } catch (Exception ex) {
                            Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                        }
                    } else {
                        AllocSocket();
                        OpenSocket();
                    }
                }
            }
        }


        public bool Buy(NewItem item) {
#if CAREFUL
            totalwasted += (int)item.ui_price;
            Log.Debug("Purchased an item for {0}, total wasted {1}", ((int)item.ui_price + .0) / 100, (totalwasted + .0) / 100);
            return true;
#else
            string url = "/api/Buy/" + item.i_classid + "_" + item.i_instanceid + "/" + ((int)item.ui_price).ToString() + "/?key=" + Api;
            if (CurrentToken != "")
                url += "&" + CurrentToken; //ugly hack, but nothing else I can do for now
            string a = ExecuteApiRequest(url, ApiMethod.Buy);
            if (a == null)
                return false;
            JObject parsed = JObject.Parse(a);
            bool badTrade = false;
            try {
                badTrade = parsed.ContainsKey("id") && (bool)parsed["id"] == false && (string)parsed["result"] == "Недостаточно средств на счету";
            } catch {

            }
            if (badTrade) {
                Log.ApiError(TMBot.RestartPriority.UnknownError, $"Missed an item {item.i_market_name} costing {item.ui_price}");
                return false;
            }
            if (parsed["result"] == null) {
                Log.ApiError("Some huge server sided error happened during buy. " + parsed.ToString(Formatting.None));
                return false;
            } else if ((string)parsed["result"] == "ok") {
                return true;
            } else {
                Log.ApiError("Could not buy an item." + parsed.ToString(Formatting.None));
                return false;
            }
#endif
        }

        public bool SellNew(long classId, long instanceId, int price) {
#if CAREFUL //sorry nothing is implemented there, I don't really know what to write as debug
            return false;


#else
            string resp = ExecuteApiRequest("/api/SetPrice/new_" + classId + "_" + instanceId + "/" + price.ToString() + "/?key=" + Api, ApiMethod.Sell);
            if (resp == null)
                return false;
            JObject parsed = JObject.Parse(resp);
            if (parsed["result"] == null)
                return false;
            else if ((bool)parsed["result"] == true) {
                return true;
            } else
                return false;
#endif
        }

        public void PingPong() {
            while (parent.IsRunning()) {
                string uri = $"/api/PingPong/direct/?key={Api}";
                string resp = ExecuteApiRequest(uri);
                LocalRequest.Ping(parent.config.Username);
                Thread.Sleep(30000);
            }
        }

        public JObject MassInfo(List<Tuple<string, string>> items, int sell = 0, int buy = 0, int history = 0, int info = 0, ApiMethod method = ApiMethod.GenericMassInfo) {
            string uri = $"/api/MassInfo/{sell}/{buy}/{history}/{info}?key={Api}";
            string data = "list=" + String.Join(",", items.Select(lr => lr.Item1 + "_" + lr.Item2).ToArray());
            try {
                string result = ExecuteApiPostRequest(uri, data);
                if (result == null)
                    return null;
                JObject temp = JObject.Parse(result);
                return temp;
            } catch (Exception ex) {
                Log.ApiError(TMBot.RestartPriority.UnknownError, $"Tried to call {uri} with such data: {data}");
                Log.ApiError(TMBot.RestartPriority.UnknownError, ex.Message);
            }
            return null;
        }

        /// <summary>
        /// Provides server response by given list{name : price}
        /// Beware, server completely ignores failing items, doesn't include them in JObject and completely silently deletes them from answer
        /// </summary>
        /// <param name="items"></param>
        /// <returns></returns>
        public JObject MassSetPriceById(List<Tuple<string, int>> items, ApiMethod method = ApiMethod.GenericMassSetPriceById) {
            string uri = $"/api/MassSetPriceById/?key={Api}";
            string data = String.Join("&", items.Select(lr => $"list[{lr.Item1}]={lr.Item2}"));
            try {
                string result = ExecuteApiPostRequest(uri, data, method);
                if (result == null)
                    return null;
                JObject temp = JObject.Parse(result);
                return temp;
            } catch (Exception ex) {
                Log.Error("Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
            }
            return null;
        }

        public Inventory GetSteamInventory() {
            string a = ExecuteApiRequest("/api/GetInv/?key=" + Api, ApiMethod.GetSteamInventory);
            Inventory inventory = new Inventory();
            inventory.content = new List<Inventory.SteamItem>();
            if (a == null) {
                return inventory;
            }
            JObject json = JObject.Parse(a);
            if (json["ok"] != null && (bool)json["ok"] == false)
                return inventory;
            inventory.content = json["data"].ToObject<List<Inventory.SteamItem>>();
            return inventory;
        }

        private object ordersLock = new object();
        private Dictionary<string, int> orders = new Dictionary<string, int>();

        public bool SetOrder(long classid, long instanceid, int price) {
            try {
#if CAREFUL
            return false;
#else
                string uri = "/api/ProcessOrder/" + classid + "/" + instanceid + "/" + price.ToString() + "/?key=" + Api;
                if (money < price) {
                    Log.Info("No money to set order, call to url was optimized :" + uri);
                    return false;
                }
                lock (ordersLock)
                    if (orders.ContainsKey($"{classid}_{instanceid}") && orders[$"{classid}_{instanceid}"] == price) {
                        Log.Info("Already have same order, call to url was optimized :" + uri);
                        return false;
                    }
                string resp = ExecuteApiRequest(uri, ApiMethod.SetOrder);
                if (resp == null)
                    return false;
                JObject json = JObject.Parse(resp);
                if (json["success"] == null) {
                    Log.ApiError(TMBot.RestartPriority.MediumError, "Was unable to set order, url is :" + uri + " | " + json.ToString(Formatting.None));
                    return false;
                } else if ((bool)json["success"]) {
                    lock (ordersLock)
                        orders[$"{classid}_{instanceid}"] = price;
                    return true;
                } else {
                    if (json.ContainsKey("error")) {
                        if ((string)json["error"] == "same_price")
                            lock (ordersLock)
                                orders[$"{classid}_{instanceid}"] = price;
                        Log.ApiError(TMBot.RestartPriority.MediumError, $"Was unable to set order: url is {uri}, error message: {(string)json["error"]}");
                    } else
                        Log.ApiError(TMBot.RestartPriority.MediumError, "Was unable to set order, url is :" + uri);
                    return false;
                }
#endif
            } catch (Exception ex) {
                Log.ApiError(TMBot.RestartPriority.MediumError, "Unknown error happened. Message: " + ex.Message + "\nTrace:" + ex.StackTrace);
                return false;
            }
        }

        bool UpdateInventory() {
            try {
                string resp = ExecuteApiRequest("/api/UpdateInventory/?key=" + Api, ApiMethod.UpdateInventory);
                if (resp == null)
                    return false;
                JObject json = JObject.Parse(resp);
                if (json["success"] == null) {
                    Log.ApiError(TMBot.RestartPriority.SmallError, "Was unable to update inventory");
                    return false;
                } else if ((bool)json["success"])
                    return true;
                else {
                    Log.ApiError(TMBot.RestartPriority.SmallError, "Was unable to update inventory");
                    return false;
                }
            } catch (Exception ex) {
                Log.ApiError(TMBot.RestartPriority.SmallError, "Was unable to update inventory. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                return false;
            }
        }

        // If best offer is our's returning -1;
        public int getBestOrder(long classid, long instanceid) {
            string resp = "";
            try {
                resp = ExecuteApiRequest("/api/ItemInfo/" + classid + "_" + instanceid + "/ru/?key=" + Api, ApiMethod.GetBestOrder);
                if (resp == null)
                    return -1;
                JObject x = JObject.Parse(resp);
                JArray thing = (JArray)x["buy_offers"];
                if (thing == null || thing.Count == 0)
                    return 49;
                if (int.Parse((string)thing[0]["o_price"]) == -1)
                    return -1;
                return int.Parse((string)thing[0]["o_price"]);
            } catch (Exception ex) {
                Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                Log.Error(resp);
                return -1;
            }
        }

        DateTime lastRefresh = DateTime.MinValue;

        public TMTrade[] GetTradeList() {
            try {
                string resp = ExecuteApiRequest("/api/Trades/?key=" + Api, ApiMethod.GetTradeList);
                if (resp == null)
                    return new TMTrade[0];
                JArray json = JArray.Parse(resp);
                TMTrade[] arr = new TMTrade[json.Count];


                int iter = 0;
                foreach (var thing in json) {
                    //Console.WriteLine("{0}", thing);
                    TMTrade xx = JsonConvert.DeserializeObject<TMTrade>(thing.ToString());
                    arr[iter++] = xx;
                }
                if (DateTime.Now.Subtract(lastRefresh).TotalMilliseconds > Consts.REFRESHINTERVAL) {
                    lastRefresh = DateTime.Now;
                    Task.Run(() => Logic.RefreshPrices(arr));
                }
                return arr;
            } catch (Exception ex) {
                Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                return new TMTrade[0];
            }
        }

        public int GetMoney() {
            string resp = ExecuteApiRequest("/api/GetMoney/?key=" + Api, ApiMethod.GetMoney);
            if (resp == null)
                return 0;
            JObject temp2 = JObject.Parse(resp);
            return (int)temp2["money"];
        }

        public List<Order> GetOrderPage(int pageNumber) {
            string resp = ExecuteApiRequest($"/api/GetOrders/{pageNumber}/?key=" + Api, ApiMethod.GetMoney);
            if (resp == null)
                return null;
            JObject temp2 = JObject.Parse(resp);
            if (temp2["Orders"].Type == JTokenType.String)
                if ((string)temp2["Orders"] == "No orders")
                    return null;
            return temp2["Orders"].ToObject<List<Order>>();
        }

        public string RemoveAll() {
            return ExecuteApiRequest("/api/RemoveAll/?key=" + Api);
        }

        //TODO(noobgam): reuse OrdersCall
        public List<Order> GetOrders() {
            int page = 1;
            List<Order> temp = new List<Order>();
            while (parent.IsRunning()) {
                Thread.Sleep(1000);
                List<Order> temp2 = GetOrderPage(page);
                if (temp2 == null)
                    return temp;
                ++page;
                temp.AddRange(temp2);
            }
            return temp;
        }

        public void OrdersCall(Action<Order> callback) {
            int page = 1;
            List<Order> temp = new List<Order>();
            while (parent.IsRunning()) {
                Thread.Sleep(1000);
                List<Order> temp2 = GetOrderPage(page);
                if (temp2 == null)
                    return;
                ++page;
                foreach (Order order in temp2) {
                    callback(order);
                }
            }
        }
    }
}
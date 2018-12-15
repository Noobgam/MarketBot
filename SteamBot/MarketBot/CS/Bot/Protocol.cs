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
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using SteamBot.MarketBot.Utility;
using System.Diagnostics;
using System.IO;
using System.Net;
using Utility;
using SteamBot.MarketBot.CS;
using SteamBot.MarketBot.CS.Bot;
using SteamBot.MarketBot.Utility.VK;
using SteamBot.MarketBot.Utility.MongoApi;

namespace CSGOTM {
    public class Protocol {
#if CAREFUL
        public int totalwasted = 0;
#endif
        bool subscribed = false;
        public NewMarketLogger Log;
        private Queue<TradeOffer> QueuedOffers;
        private MongoOperationHistory operationHistory;
        public Logic Logic;
        public SteamBot.Bot Bot;
        private int money = 0;
        private readonly Random Generator = new Random();
        private readonly string Api;
        private SemaphoreSlim ApiSemaphore = new SemaphoreSlim(10);
        private TMBot parent;
        private string CurrentToken = "";
        private bool StopBuy = false;
        private MongoBannedUsers mongoBannedUsers = new MongoBannedUsers();

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

        public enum ApiLogLevel {
            DoNotLog = 0,
            LogAll = 1,
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

        private async Task<string> ExecuteApiRequestAsync(string url, ApiMethod method = ApiMethod.GenericCall, ApiLogLevel logLevel = ApiLogLevel.DoNotLog) {
            try {
                ObtainApiSemaphore(method);
                if (logLevel == ApiLogLevel.LogAll) {
                    Log.Info("<Async> Executing " + url);
                }
                return await Request.GetAsync(Consts.MARKETENDPOINT + url);
            } catch (Exception e) {
                Log.ApiError(TMBot.RestartPriority.UnknownError, $"<Async> GET call to {Consts.MARKETENDPOINT}{url} failed");
                return null;
            } finally {
                ReleaseApiSemaphore(method);
            }
        }

        private string ExecuteApiRequest(string url, ApiMethod method = ApiMethod.GenericCall, ApiLogLevel logLevel = ApiLogLevel.DoNotLog) {
            if (logLevel == ApiLogLevel.LogAll) {
                Log.Info("Executing " + url);
            }
            string response = null;
            Stopwatch temp = new Stopwatch();
            try {
                ObtainApiSemaphore(method);
                //Log.Success("Executing api call " + url);
                temp.Start();
                response = Request.Get(Consts.MARKETENDPOINT + url);
                temp.Stop();
                ShiftEma(0);
                Log.Nothing($"GET {url} : {temp.ElapsedMilliseconds}");
            } catch (Exception ex) {
                Log.ApiError(TMBot.RestartPriority.UnknownError, $"GET call to {Consts.MARKETENDPOINT}{url} failed");
                bool flagged = false;
                if (ex is WebException webex) {
                    if (webex.Status == WebExceptionStatus.ProtocolError) {
                        if (webex.Response is HttpWebResponse resp) {
                            if ((int)resp.StatusCode == 500 || (int)resp.StatusCode == 520 || (int)resp.StatusCode == 521) {
                                Log.ApiError(TMBot.RestartPriority.MediumError, $"Status code: {(int)resp.StatusCode}");
                                flagged = true;
                            }
                        }
                    }
                }
                if (!flagged) {
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
                return JArray.Parse(Request.Get($"{Consts.MARKETENDPOINT}/ajax/i_popularity/all/all/all/{page}/{amount}/{lowest_price};{highest_price}/all/all/-1"));
            } else {
                return JArray.Parse(Request.Get($"{Consts.MARKETENDPOINT}/ajax/i_popularity/all/all/all/{page}/{amount}/{lowest_price};{highest_price}/all/all/all"));
            }
        }

        private string ExecuteApiPostRequest(string url, string data, ApiMethod method = ApiMethod.GenericCall, ApiLogLevel logLevel = ApiLogLevel.DoNotLog) {
            if (logLevel == ApiLogLevel.LogAll) {
                Log.Info("Executing " + url);
            }
            Stopwatch temp = new Stopwatch();
            string response = null;
            try {
                ObtainApiSemaphore(method);
                //Log.Success("Executing api call " + url);
                temp.Start();
                response = Utility.Request.Post(Consts.MARKETENDPOINT + url, data);
                temp.Stop();
                ShiftEma(0);
                Log.Nothing($"POST {url} : {temp.ElapsedMilliseconds}");
            } catch (Exception ex) {
                Log.Nothing(TMBot.RestartPriority.UnknownError, $"POST call to {Consts.MARKETENDPOINT}{url} failed");
                bool flagged = false;
                if (ex is WebException webex) {
                    if (webex.Status == WebExceptionStatus.ProtocolError) {
                        if (webex.Response is HttpWebResponse resp) {
                            if ((int)resp.StatusCode == 500 || (int)resp.StatusCode == 520 || (int)resp.StatusCode == 521) {
                                Log.ApiError(TMBot.RestartPriority.MediumError, $"Status code: {(int)resp.StatusCode}");
                                flagged = true;
                            }
                        }
                    }
                }
                if (!flagged) {
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

        public IEnumerable<HistoricalOperation> OperationHistory(DateTime start, DateTime end) {
            JsonSerializerSettings settings = new JsonSerializerSettings {
                MissingMemberHandling = MissingMemberHandling.Error
            };
            string response = ExecuteApiRequest($"/api/OperationHistory/{((DateTimeOffset)start).ToUnixTimeSeconds()}/{((DateTimeOffset)end).ToUnixTimeSeconds()}/?key={Api}");
            if (response == null)
                return new List<HistoricalOperation>();
            try {
                JObject resp = JObject.Parse(response);
                if ((bool) resp["success"]) {
                    return resp["history"].ToObject<List<HistoricalOperation>>();
                } else {
                    Log.Error($"\"{(string)resp["error"]}\" occured while executing /api/OperationHistory/{((DateTimeOffset)start).ToUnixTimeSeconds()}/{((DateTimeOffset)end).ToUnixTimeSeconds()}/?key=<...>");
                    return new List<HistoricalOperation>();
                }
            }
            catch (Exception e) {
                Log.Error($"Unexpected error happened while executing /api/OperationHistory/{((DateTimeOffset)start).ToUnixTimeSeconds()}/{((DateTimeOffset)end).ToUnixTimeSeconds()}/?key=<...> Error is: {e.Message}");
                return new List<HistoricalOperation>();
            }
        }

        bool opening = false;
        bool died = true;
        WebSocket socket;
        private string botName;
        public Protocol(TMBot bot) {
            var lmao = mongoBannedUsers.GetBannedUsers();
            parent = bot;
            Api = bot.config.Api;
            this.botName = bot.config.Username;
            this.Bot = bot.bot;
            InitializeRPSSemaphores();
            operationHistory = new MongoOperationHistory(bot.config.Username);
            Tasking.Run((Action)StartUp, botName);
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
            SetSteamAPIKey(parent.bot.botConfig.ApiKey);
            while (Logic == null || Bot.IsLoggedIn == false)
                Thread.Sleep(10);
            QueuedOffers = new Queue<TradeOffer>();
            GetMoney();
            Tasking.Run(PingPongMarket, botName);
            Tasking.Run(PingPongLocal, botName);
            Tasking.Run(ReOpener, botName);
            Tasking.Run(RefreshToken, botName);
            Tasking.Run(HandleTrades, botName);
            Tasking.Run(() => {
                OrdersCall(order => {
                    lock (ordersLock)
                        orders[$"{order.i_classid}_{order.i_instanceid}"] = int.Parse(order.o_price);
                });
            }, botName);
            AllocSocket();
            OpenSocket();            
            SubscribeToBalancer();
            Tasking.Run(OperationHistoryThread, botName);
        }

        private void OperationHistoryThread() {
            const int DELAY = 1000 * 60 * 8;
            TimeSpan TS_DELAY = new TimeSpan(0, 0, 0, 0, DELAY);
            DateTime startStamp = DateTime.Now.Subtract(new TimeSpan(7, 0, 0, 0));
            DateTime curStamp =   DateTime.Now;
            LogOperationHistory(OperationHistory(startStamp, curStamp));
            while (parent.IsRunning()) {
                if (Tasking.WaitForFalseOrTimeout(parent.IsRunning, DELAY).Result)
                    return;
                LogOperationHistory(OperationHistory(curStamp.Subtract(new TimeSpan(0, 10, 0)), curStamp));
                curStamp = curStamp.Add(TS_DELAY);
            }
        }

        private void LogOperationHistory(IEnumerable<HistoricalOperation> historicalOperations) {
            try {
                foreach (var item in historicalOperations) {
                    operationHistory.InsertOrReplace(item);
                }
                Log.Info($"{historicalOperations.Count()} history operations added");
            } catch (Exception ex) {
                Log.Error($"Error happened during LogOperationHistory: {ex.Message}");
            }
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

        public void ProcessNewItem(object sender, NewItem newItem) {
            if (!parent.IsRunning())
                return;
            if (newItem.i_market_name == "") {
                Log.Error("Socket item has no market name");
            } else if (!Logic.sellOnly && Logic.WantToBuy(newItem)) {
                _ = BuyAsync(newItem);
            }
        }

        void Msg(object sender, MessageReceivedEventArgs e) {
            if (!parent.IsRunning()) {
                if (socket.State == WebSocketState.Open)
                    socket.Close();
                return;
            }
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
                    case "history_go":
                        try {
                            NewHistoryItem historyItem = new NewHistoryItem(data);
                            Logic.ProcessItem(historyItem);
                        } catch (Exception ex) {
                            Log.Error($"Some error occured during history parse. [{data}] Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                        }
                        break;

                    case "invcache_go":
                        Log.Info("Inventory was cached");
                        break;
                    case "money":
                        try {
                            string splitted = data.Split('\"')[1].Split('<')[0].Replace(" ", "");
                            if (splitted.EndsWith("\\u00a0")) {
                                money = (int)(double.Parse(splitted.Substring(0, splitted.Length - "\\u00a0".Length), new CultureInfo("en")) * 100);
                            } else {
                                money = (int)(double.Parse(splitted, new CultureInfo("en")) * 100);
                            }
                        } catch {
                            Log.Error($"Can't parse money from {data} [{data.Split('\"')[1].Split('<')[0]}]");
                            money = 90000;
                        }
                        break;
                    case "additem_go":
                        break;
                    case "itemstatus_go":
                        //JObject json = JObject.Parse(data);
                        //if ((int)json["status"] == 5)
                        //    Logic.doNotSell = true;
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
        
        bool Alive() {
            return !died && parent.IsRunning();
        }

        void SocketPinger() {
            while (Alive()) {
                try {
                    socket.Send("ping");
                } catch (Exception ex) {
                    Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                }
                Tasking.WaitForFalseOrTimeout(Alive, 25000).Wait();
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
                if ((bool)json["manual"]) {
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
                string resp = ExecuteApiRequest($"/api/ItemRequest/in/{trade.ui_bid}/?key=" + Api, ApiMethod.ItemRequest);
                if (resp == null)
                    continue;
                JObject json = JObject.Parse(resp);
                if (Tasking.WaitForFalseOrTimeout(parent.IsRunning, 1000).Result)
                    return;
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
                            continue;
                        }
                        string profile = (string)json["profile"];
                        ulong id = ulong.Parse(profile.Split('/')[4]);
                        ISet<long> blacklistedUsers = new HashSet<long>(mongoBannedUsers.GetBannedUsers().Select(bannedUser => bannedUser.SteamID64));
                        if (blacklistedUsers.Contains((long)id /* can afford the cast here */)) {
                            Log.Warn($"Not sending a request, user {(string)json["profile"]} is blacklisted. (Mongo)");
                            continue;
                        }

                        //Log.Info(json.ToString(Formatting.None));
                        var offer = Bot.NewTradeOffer(new SteamID(id));
                        try {
                            foreach (JToken item in json["request"]["items"]) {
                                offer.Items.AddMyItem(
                                    (int)item["appid"],
                                    (long)item["contextid"],
                                    (long)item["assetid"],
                                    (long)item["amount"]);
                            }
                            Log.Info(parent.config.Username, "Partner: {0} Token: {1} Tradeoffermessage: {2} Profile: {3}. Tradelink: https://steamcommunity.com/tradeoffer/new/?partner={0}&token={1}", (string)json["request"]["partner"], (string)json["request"]["token"], (string)json["request"]["tradeoffermessage"], (string)json["profile"]);
                            if (offer.Items.NewVersion) {
                                if (offer.SendWithToken(out string newOfferId, (string)json["request"]["token"], (string)json["request"]["tradeoffermessage"])) {
                                    Log.Success("Trade offer sent : Offer ID " + newOfferId);
                                    list.Add(new Pair<string, string>(requestId, newOfferId));
                                    ++sent;
                                    sentTrades[trade.ui_bid] = DateTime.Now;
                                    Thread.Sleep(1000);
                                } else {
                                    if (newOfferId == "null") {
                                        VK.Alert("Трейд не отправлен. Ответ: \"null\".\nПроверьте профиль на всякий: " + (string)json["profile"], VK.AlertLevel.All);
                                        VK.Alert($"Tradelink: https://steamcommunity.com/tradeoffer/new/?partner={(string)json["request"]["partner"]}&token={(string)json["request"]["token"]}", VK.AlertLevel.All);
                                        Log.Error(TMBot.RestartPriority.CriticalError, $"Trade offer was not sent!");
                                    } else {
                                        try {
                                            string err = (string)JObject.Parse(newOfferId)["strError"];
                                            Log.Error(TMBot.RestartPriority.UnknownError, $"Trade offer not sent. Error: [{err}]");

                                            if (err.Contains("(15)")) {
                                                VK.Alert("Трейд не отправлен по ошибке 15.\nПроверьте профиль ручками: " + (string)json["profile"], VK.AlertLevel.All);
                                            } else {
                                                VK.Alert($"Трейд не отправлен по причине [{err}].\nПроверьте профиль ручками: " + (string)json["profile"], VK.AlertLevel.All);
                                            }
                                            VK.Alert($"Tradelink: https://steamcommunity.com/tradeoffer/new/?partner={(string)json["request"]["partner"]}&token={(string)json["request"]["token"]}", VK.AlertLevel.All);

                                        } catch {

                                        }
                                    }
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
                                    if (newOfferId == "null") {
                                        VK.Alert("Трейд не отправлен. Ответ: \"null\".\nПроверьте профиль на всякий: " + (string)json["profile"]);
                                        Log.Error(TMBot.RestartPriority.CriticalError, $"Trade offer was not sent!");
                                    } else {
                                        try {
                                            string err = (string)JObject.Parse(newOfferId)["strError"];
                                            if (err != "There was an error sending your trade offer. Please try again later. (15)") {
                                                VK.Alert("Трейд не отправлен по неожиданной причине.\nПроверьте профиль ручками: " + (string)json["profile"]);
                                                //Log.Error(TMBot.RestartPriority.CriticalError, $"Trade offer was not sent!");
                                            } else {
                                                VK.Alert("Трейд не отправлен по ошибке 15.\nПроверьте профиль ручками: " + (string)json["profile"]);
                                            }

                                        } catch {

                                        }
                                    }
                                }
                            }
                        } catch (Exception ex) {
                            Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
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

        bool ReportFailedTrade(string requestId) {
            if (requestId == "") {
                return false;
            }
            try {
                string resp = ExecuteApiRequest($"/api/ReportFailedTrade/{requestId}/?key={Api}");
                if (resp == null)
                    return false;
                JObject json = JObject.Parse(resp);
                if (json["success"] == null) {
                    Log.Error("TM thinks offer did not fail.");
                    return false;
                } else if ((bool)json["success"]) {
                    Log.Success("TM knows about failed offer");
                    return true;
                } else {
                    Log.Error("TM thinks offer did not fail.");
                    return false;
                }
            } catch (Exception ex) {
                Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                return false;
            }
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
                bool flag = false;
                try {
                    temp = LocalRequest.GetBestToken(parent.config.Username);
                    if ((bool)temp["success"]) {
                        CurrentToken = (string)temp["token"];
                        StopBuy = false;
                    }  else {
                        if ((string)temp["error"] == "All bots are overflowing!")
                            StopBuy = true;
                        else
                            flag = true;
                    }
                } catch (Exception e) {
                    flag = true;
                    Log.Error("Could not get a response from local server");
                }
                if (!flag)
                    Tasking.WaitForFalseOrTimeout(parent.IsRunning, timeout: 30000).Wait();
                if (flag)
                    Tasking.WaitForFalseOrTimeout(parent.IsRunning, timeout: 1000).Wait();

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
                    Tasking.WaitForFalseOrTimeout(parent.IsRunning, 30000).Wait();
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
                Tasking.WaitForFalseOrTimeout(parent.IsRunning, 10000).Wait();
            }
        }

        void HandleTrades() {
            Tasking.Run((Action)HandleSoldTrades, botName);
            //Tasking.Run((Action)HandlePurchasedTrades);
            while (parent.IsRunning()) {
                try {
                    TMTrade[] arr = GetTradeList();
                    lock (TradeCacheLock) {
                        CachedTrades = arr;
                        waitHandle.Set();
                    }
                    Tasking.WaitForFalseOrTimeout(parent.IsRunning, 10000).Wait();
                    UpdateInventory();
                } catch (Exception ex) {
                    Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                }
                Tasking.WaitForFalseOrTimeout(parent.IsRunning, 4000).Wait();
            }
        }

        void Subscribe() {
            //socket.Send("newitems_go");
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
                Tasking.Run((Action)SocketPinger, botName);
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
                if (Tasking.WaitForFalseOrTimeout(parent.IsRunning, 10000).Result)
                    continue;
                if (died) {
                    if (!opening) {
                        try {
                            int index = i++;
                            Log.Info($"Trying to reconnect for the {index}-th time");
                            Task.Delay(5000).ContinueWith(_ => {
                                if (died == true) {
                                    Log.ApiError(TMBot.RestartPriority.MediumError, $"{index}-th time");
                                }
                            });
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

        public async Task<bool> BuyAsync(NewItem item) {
            string url = "/api/Buy/" + item.i_classid + "_" + item.i_instanceid + "/" + item.ui_price.ToString() + "/?key=" + Api;
            if (StopBuy) {
                Log.Error($"Skipping purchase request, all bots are overflowing.");
                return false;
            }
            if (CurrentToken != "")
                url += "&" + CurrentToken; //ugly hack, but nothing else I can do for now
            string response = await ExecuteApiRequestAsync(url, ApiMethod.Buy, ApiLogLevel.LogAll);
            if (response == null) {
                return false;
            }
            JObject parsed = JObject.Parse(response);
            bool badTrade = false;
            try {
                badTrade = parsed.ContainsKey("id") && (bool)parsed["id"] == false && (string)parsed["result"] == "Недостаточно средств на счету";
            } catch {

            }
            if (badTrade) {
                Log.ApiError($"<Async> Missed an item {item.i_market_name} costing {item.ui_price}");
                return false;
            }
            if (parsed["result"] == null) {
                Log.ApiError("<Async> Some huge server sided error happened during buy. " + parsed.ToString(Formatting.None));
                return false;
            } else if ((string)parsed["result"] == "ok") {
                Log.Success("Purchased: " + item.i_market_name + " " + item.ui_price);
                return true;
            } else {
                Log.ApiError($"<Async> Could not buy an item. {item.i_market_name} costing {item.ui_price}" + parsed.ToString(Formatting.None));
                return false;
            }
        }

        public bool SetSteamAPIKey(string apiKey) {
            string url = $"/api/SetSteamAPIKey/{apiKey}/?key={Api}";
            string response = ExecuteApiRequest(url, ApiMethod.GenericCall, ApiLogLevel.LogAll);
            if (response == null) {
                return false;
            }
            JObject parsed = JObject.Parse(response);
            if (parsed["success"] == null || parsed["success"].Type != JTokenType.Boolean || !(bool)parsed["success"])
                return false;
            return true;
        }

        public bool Buy(NewItem item) {
#if CAREFUL
            totalwasted += (int)item.ui_price;
            Log.Debug("Purchased an item for {0}, total wasted {1}", ((int)item.ui_price + .0) / 100, (totalwasted + .0) / 100);
            return true;
#else
            string url = "/api/Buy/" + item.i_classid + "_" + item.i_instanceid + "/" + item.ui_price.ToString() + "/?key=" + Api;
            if (StopBuy) {
                Log.Error($"Skipping purchase request, all bots are overflowing.");
                return false;
            }
            if (CurrentToken != "")
                url += "&" + CurrentToken; //ugly hack, but nothing else I can do for now
            string response = ExecuteApiRequest(url, ApiMethod.Buy, ApiLogLevel.LogAll);
            if (response == null) {
                return false;
            }
            JObject parsed = JObject.Parse(response);
            bool badTrade = false;
            try {
                badTrade = parsed.ContainsKey("id") && (bool)parsed["id"] == false && (string)parsed["result"] == "Недостаточно средств на счету";
            } catch {

            }
            if (badTrade) {
                Log.ApiError($"Missed an item {item.i_market_name} costing {item.ui_price}");
                return false;
            }
            if (parsed["result"] == null) {
                Log.ApiError("Some huge server sided error happened during buy. " + parsed.ToString(Formatting.None));
                return false;
            } else if ((string)parsed["result"] == "ok") {
                return true;
            } else {
                Log.ApiError($"Could not buy an item. {item.i_market_name} costing {item.ui_price}" + parsed.ToString(Formatting.None));
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

        public void PingPongMarket() {
            while (parent.IsRunning()) {
                string uri = $"/api/PingPong/direct/?key={Api}";
                ExecuteApiRequest(uri);
                Tasking.WaitForFalseOrTimeout(parent.IsRunning, 30000).Wait();
            }
        }

        public void PingPongLocal() {
            while (parent.IsRunning()) {
                LocalRequest.Ping(parent.config.Username);
                Tasking.WaitForFalseOrTimeout(parent.IsRunning, 30000).Wait();
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
                        if ((string)json["error"] == "same_price") {
                            lock (ordersLock)
                                orders[$"{classid}_{instanceid}"] = price;
                            return true;
                        }
                        if ((string)json["error"] == "money") {
                            Log.ApiError(TMBot.RestartPriority.UnknownError, $"Was unable to set order: url is {uri}, error message: {(string)json["error"]}");
                        } else {
                            Log.ApiError(TMBot.RestartPriority.MediumError, $"Was unable to set order: url is {uri}, error message: {(string)json["error"]}");
                        }
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
                    Log.ApiError("Was unable to update inventory");
                    return false;
                } else if ((bool)json["success"])
                    return true;
                else {
                    Log.ApiError("Was unable to update inventory");
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
                    Tasking.Run(() => Logic.RefreshPrices(arr), botName);
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
                return money;
            JObject temp2 = JObject.Parse(resp);
            money = (int)temp2["money"];
            return money;
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
                List<Order> temp2 = GetOrderPage(page);
                if (temp2 == null)
                    return temp;
                ++page;
                temp.AddRange(temp2);
                Tasking.WaitForFalseOrTimeout(parent.IsRunning, 1000).Wait();
            }
            return temp;
        }

        public void OrdersCall(Action<Order> callback) {
            int page = 1;
            List<Order> temp = new List<Order>();
            while (parent.IsRunning()) {
                List<Order> temp2 = GetOrderPage(page);
                if (temp2 == null)
                    return;
                ++page;
                foreach (Order order in temp2) {
                    callback(order);
                }
                Tasking.WaitForFalseOrTimeout(parent.IsRunning, 1000).Wait();
            }
        }

        void SubscribeToBalancer() {
            if (!subscribed) {
                Balancer.NewItemAppeared += ProcessNewItem;
                subscribed = true;
            }
        }

        void UnsubscribeFromBalancer() {
            if (subscribed) {
                Balancer.NewItemAppeared -= ProcessNewItem;
                subscribed = false;
            }
        }

        ~Protocol() {
            UnsubscribeFromBalancer();
        }
    }
}
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

namespace CSGOTM
{
    public class Protocol
    {
#if CAREFUL
        public int totalwasted = 0;
#endif
        public Utility.MarketLogger Log;
        private Queue<TradeOffer> QueuedOffers;
        public Logic Logic;
        public SteamBot.Bot Bot;
        int money = 0;
        Random Generator = new Random();
        string Api = null;
        SemaphoreSlim ApiSemaphore = new SemaphoreSlim(10);

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

        private void ObtainApiSemaphore(ApiMethod method)
        {
            rpsRestricter[method].Wait();
            ApiSemaphore.Wait();
        }

        private void ReleaseApiSemaphore(ApiMethod method)
        {
            Task.Delay(rpsDelay[method])
                .ContinueWith(tks => rpsRestricter[method].Release());
            Task.Delay(Consts.SECOND)
                .ContinueWith(tsk => ApiSemaphore.Release());
        }

        private string ExecuteApiRequest(string url, ApiMethod method = ApiMethod.GenericCall)
        {
            string response = null;
            try
            {
                ObtainApiSemaphore(method);
                //Log.Success("Executing api call " + url);
                response = Utility.Request.Get("https://market.csgo.com" + url);
            }
            catch (Exception ex)
            {
                Log.ApiError($"GET call to https://market.csgo.com{url} failed");
                Log.ApiError($"Message: {ex.Message}\nTrace: {ex.StackTrace}");
            }
            finally
            {
                ReleaseApiSemaphore(method);
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
        public JArray PopularItems(int page = 1, int amount = 56, int lowest_price = 0, int highest_price = 100000, bool any_stickers = false)
        {
            if (any_stickers)
            {
                return JArray.Parse(Utility.Request.Get($"https://market.csgo.com/ajax/i_popularity/all/all/all/{page}/{amount}/{lowest_price};{highest_price}/all/all/-1"));
            }
            else
            {
                return JArray.Parse(Utility.Request.Get($"https://market.csgo.com/ajax/i_popularity/all/all/all/{page}/{amount}/{lowest_price};{highest_price}/all/all/all"));
            }
        }

        private string ExecuteApiPostRequest(string url, string data, ApiMethod method = ApiMethod.GenericCall)
        {
            string response = null;
            try
            {
                ObtainApiSemaphore(method);
                //Log.Success("Executing api call " + url);
                response = Utility.Request.Post("https://market.csgo.com" + url, data);
            }
            catch (Exception ex)
            {
                Log.ApiError($"POST call to https://market.csgo.com{url} with data: {data} failed");
                Log.ApiError($"Message: {ex.Message}\nTrace: {ex.StackTrace}");
            }
            finally
            {
                ReleaseApiSemaphore(method);
            }
            return response;
        }

        bool died = true;
        WebSocket socket = new WebSocket("wss://wsn.dota2.net/wsn/");
        public Protocol(SteamBot.Bot Bot, string api) {
            JObject x = new JObject();
            x["bool"] = false;
            Api = api;
            this.Bot = Bot;
            InitializeRPSSemaphores();
            Task.Run((Action)StartUp);
        }

        //I'm going to use different semaphores to allow some requests to have higher RPS than others, without bothering one another.

        private void GenerateSemaphore(ApiMethod method, double rps)
        {
            rpsRestricter[method] = new SemaphoreSlim(Consts.GLOBALRPSLIMIT);
            rpsDelay[method] = (int)Math.Ceiling(1000.0 / rps);
        }

        private void InitializeRPSSemaphores()
        {
            double totalrps = 0;
            foreach (ApiMethod method in ((ApiMethod[]) Enum.GetValues(typeof(ApiMethod))).Distinct())
            {
                double limit;
                bool temp = rpsLimit.TryGetValue(method, out limit);
                if (temp)
                {
                    GenerateSemaphore(method, limit);
                }
                else
                {
                    limit = Consts.DEFAULTRPS;
                    GenerateSemaphore(method, limit);
                }
                totalrps += limit;
            }      
            if (totalrps > Consts.GLOBALRPSLIMIT)
            {
                //Log.Info($"Total RPS of {totalrps} exceeds {Consts.GLOBALRPSLIMIT}.");
            }
        }
        
        private void StartUp() {
            while (Logic == null || Bot.IsLoggedIn == false)
                Thread.Sleep(10);
            QueuedOffers = new Queue<TradeOffer>();
            money = GetMoney();
            Task.Run((Action)PingPong);
            Task.Run((Action)ReOpener);
            Task.Run((Action)HandleTrades);
            socket.Opened += Open;
            socket.Error += Error;
            socket.Closed += Close;
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
        
        void Msg(object sender, MessageReceivedEventArgs e)
        {
            try
            {
                if (e.Message == "pong")
                    return;
                string type = string.Empty;
                string data = string.Empty;
                JsonTextReader reader = new JsonTextReader(new StringReader(e.Message));
                string currentProperty = string.Empty;
                while (reader.Read())
                {
                    if (reader.Value != null)
                    {
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
                switch (type)
                {
                    case "newitems_go":
                        reader = new JsonTextReader(new StringReader(data));
                        currentProperty = string.Empty;
                        NewItem newItem = new NewItem();
                        while (reader.Read())
                        {
                            if (reader.Value != null)
                            {
                                if (reader.TokenType == JsonToken.PropertyName)
                                    currentProperty = reader.Value.ToString();
                                else if (reader.TokenType == JsonToken.String)
                                {
                                    switch (currentProperty)
                                    {
                                        case "i_classid":
                                            newItem.i_classid = reader.Value.ToString();
                                            break;
                                        case "i_instanceid":
                                            newItem.i_instanceid = reader.Value.ToString();
                                            break;
                                        case "i_market_name":
                                            newItem.i_market_name = reader.Value.ToString();
                                            break;
                                        default:
                                            break;
                                         }
                                }
                                else if (currentProperty == "ui_price")
                                {
                                    newItem.ui_price = (int)(float.Parse(reader.Value.ToString()) * 100);

                                }
                            }
                        }
                        //getBestOrder(newItem.i_classid, newItem.i_instanceid);
                        if (!Logic.sellOnly && Logic.WantToBuy(newItem))
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
                            data = Encode.DecodeEncodedNonAsciiCharacters(data);
                            data = data.Replace("\\", "").Replace("\"", "").Trim(trimming);
                            string[] arr = data.Split(',');
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
                                historyItem.i_classid = arr[0];
                                historyItem.i_instanceid = arr[1];
                                historyItem.i_market_hash_name = arr[2] + "," + arr[3];
                                historyItem.timesold = arr[4];
                                historyItem.price = Int32.Parse(arr[5]);
                                historyItem.i_market_name = arr[6] + "," + arr[7];
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
                        try
                        {
                            money = (int)(double.Parse(data.Split('\"')[1].Split('<')[0], new CultureInfo("en")) * 100);
                        } catch
                        {
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
            }
            catch (Exception ex)
            {
                Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
            }
            finally
            {
            }
        }

        void SocketPinger()
        {
            while (!died)
            {
                try
                {
                    socket.Send("ping");
                }
                catch (Exception ex)
                {
                    Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                }
                Thread.Sleep(30000);
            }
        }

        void SendSoldItems(IEnumerable<TMTrade> trades) {
            foreach (TMTrade trade in trades.OrderBy(trade => trade.offer_live_time))
            {
                Debug.Assert(trade.ui_status == "2");
                string resp = ExecuteApiRequest($"/api/ItemRequest/in/{trade.ui_bid}/?key=" + Api, ApiMethod.ItemRequest);
                if (resp == null)
                    continue;
                JObject json = JObject.Parse(resp);
                Thread.Sleep(1000);
                if (json["success"] == null)
                    continue;
                else if ((bool)json["success"])
                {
                    if ((bool)json["manual"])
                    {
                        string profile = (string)json["profile"];
                        ulong id = ulong.Parse(profile.Split('/')[4]);

                        Log.Info(json.ToString(Formatting.None));
                        var offer = Bot.NewTradeOffer(new SteamID(id));
                        try
                        {
                            foreach (JObject item in json["request"]["items"])
                            {
                                offer.Items.AddMyItem(
                                    (int)item["appid"],
                                    (long)item["contextid"],
                                    (long)item["assetid"],
                                    (long)item["amount"]);
                            }
                            Log.Info("Partner: {0}\nToken: {1}\nTradeoffermessage: {2}\nProfile: {3}", (string)json["request"]["partner"], (string)json["request"]["token"], (string)json["request"]["tradeoffermessage"], (string)json["profile"]);
                            if (offer.Items.NewVersion)
                            {
                                if (offer.SendWithToken(out string newOfferId, (string)json["request"]["token"], (string)json["request"]["tradeoffermessage"]))
                                {
                                    Log.Success("Trade offer sent : Offer ID " + newOfferId);
                                    Thread.Sleep(1000);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(ex.Message);
                            Thread.Sleep(5000); //sleep tight, steam probably went 500
                        }
                    }
                }
            }
            Task.Delay(5000) //the delay might fix #35
                .ContinueWith(tsk => Bot.AcceptAllMobileTradeConfirmations());
        }

        bool SendSoldItems()
        {
            try
            {
                JObject json = JObject.Parse(ExecuteApiRequest("/api/ItemRequest/in/1/?key=" + Api, ApiMethod.ItemRequest));
                if (json["success"] == null)
                    return false;
                else if ((bool)json["success"])
                {
                    if ((bool)json["manual"]) {
                        string profile = (string)json["profile"];
                        ulong id = ulong.Parse(profile.Split('/')[4]);

                        Log.Info(json.ToString(Formatting.None));
                        for (int triesLeft = 3; triesLeft > 0; triesLeft--)
                        {
                            var offer = Bot.NewTradeOffer(new SteamID(id));
                            try
                            {
                                foreach (JObject item in json["request"]["items"])
                                {
                                    offer.Items.AddMyItem(
                                        (int)item["appid"],
                                        (long)item["contextid"],
                                        (long)item["assetid"],
                                        (long)item["amount"]);
                                }
                                Log.Info("Partner: {0}\nToken: {1}\nTradeoffermessage: {2}\nProfile: {3}", (string)json["request"]["partner"], (string)json["request"]["token"], (string)json["request"]["tradeoffermessage"], (string)json["profile"]);
                                if (offer.Items.NewVersion)
                                {
                                    string newOfferId;
                                    if (offer.SendWithToken(out newOfferId, (string)json["request"]["token"], (string)json["request"]["tradeoffermessage"]))
                                    {
                                        Task.Delay(5000) //the delay might fix #35
                                            .ContinueWith(tsk => Bot.AcceptAllMobileTradeConfirmations());
                                        Log.Success("Trade offer sent : Offer ID " + newOfferId);
                                        return true;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Debug(ex.Message);
                                if (ex.InnerException is WebException wex)
                                {
                                    string resp = new StreamReader(wex.Response.GetResponseStream()).ReadToEnd();
                                    Log.Debug(resp);
                                }
                                string response = Utility.Request.Get($"http://steamcommunity.com/inventory/{Bot.SteamUser.SteamID.ConvertToUInt64()}/730/2?l=russian&count=5000");
                                JObject parsed = JObject.Parse(response);
                                Log.Debug("Could not send trade. Additional information:");
                                Log.Debug(parsed.ToString(Formatting.None));
                                Log.Debug(json.ToString(Formatting.None));
                                Thread.Sleep(5000); //sleep tight, steam probably went 500
                            }
                        }
                        return false;
                    } else {
                        return true; //UHHHHHHHHHHHHHHHHHHHH
                    }
                }
                else
                    return false;
            }
            catch (Exception ex)
            {
                Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                return false;
            }
        }
        
        bool RequestPurchasedItems(string botID)
        {
            Log.Info("Requesting items");
            try
            {
                string resp = ExecuteApiRequest("/api/ItemRequest/out/" + botID + "/?key=" + Api, ApiMethod.ItemRequest);
                if (resp == null)
                    return false;                    
                JObject json = JObject.Parse(resp);
                if (json["success"] == null)
                    return false;
                else if ((bool)json["success"])
                {
                    return true;
                }
                else
                    return false;
            }
            catch (Exception ex)
            {
                Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                return false;
            }
        }
        
        void HandleTrades()
        {
            while (true)
            {
                int sleep = 10000;
                try
                {
                    TMTrade[] arr = GetTradeList();
                    bool had = false;
                    bool gone = false;
                    for (int i = 0; i < arr.Length; ++i)
                    {
                        if (arr[i].ui_status == "4")
                        {
                            if (UpdateInventory())
                            {
                                Thread.Sleep(10000); //should wait some time if inventory was updated
                            }
                            RequestPurchasedItems(arr[i].ui_bid);
                            gone = true;
                            Logic.doNotSell = true; 
                            break;
                        }
                        had |= arr[i].ui_status == "2";
                    }
                    if (had && !gone)
                    {
                        if (UpdateInventory())
                        {
                            Thread.Sleep(10000); //should wait some time if inventory was updated
                        }
                        SendSoldItems(arr.Where(t => t.ui_status == "2"));
                        sleep += 25000;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                }
                Thread.Sleep(sleep);
            }
        }

        void Subscribe()
        {
            socket.Send("newitems_go");
            socket.Send("history_go");
        }

        void OpenSocket()
        {

        }

        bool Auth()
        {
            string resp = ExecuteApiRequest("/api/GetWSAuth/?key=" + Api);
            if (resp == null)
                return false;
            Auth q = JsonConvert.DeserializeObject<Auth>(resp);
            socket.Send(q.wsAuth);
            Subscribe();
            return true;
        }

        void Open(object sender, EventArgs e)
        {
            died = false;
            Log.Success("Connection opened!");
            if (Auth())
                Task.Run((Action)SocketPinger);
            //andrew is gay
        }

        void Error(object sender, EventArgs e)
        {
            //Log.Error($"Connection error: " + e.ToString());
        }

        void Close(object sender, EventArgs e)
        {
            //Log.Error($"Connection closed: " + e.ToString());
            if (!died)
            {
                died = true;
                var temp = socket; //think it might help but I don't have time to check whether it does.
                if (temp.State == WebSocketState.Open)
                    temp.Close();
            }
        }

        void ReOpener()
        {
            int i = 1;
            while (true)
            {
                Thread.Sleep(10000);
                if (died)
                {
                    try
                    {
                        Log.ApiError($"Trying to reconnect for the {i++}-th time");
                        socket = new WebSocket("wss://wsn.dota2.net/wsn/");
                        socket.Opened += Open;
                        socket.Error += Error;
                        socket.Closed += Close;
                        socket.MessageReceived += Msg;
                        socket.Open();
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                    }
                }
            }
        }


        public bool Buy(NewItem item)
        {
#if CAREFUL
            totalwasted += (int)item.ui_price;
            Log.Debug("Purchased an item for {0}, total wasted {1}", ((int)item.ui_price + .0) / 100, (totalwasted + .0) / 100);
            return true;
#else
            string url = "/api/Buy/" + item.i_classid + "_" + item.i_instanceid + "/" + ((int)item.ui_price).ToString() + "/?key=" + Api;
            string a = ExecuteApiRequest(url, ApiMethod.Buy);
            if (a == null)
                return false;
            JObject parsed = JObject.Parse(a);
            bool badTrade = false;
			try {
				badTrade = parsed.ContainsKey("id") && (bool)parsed["id"] == false && (string)parsed["result"] == "Недостаточно средств на счету";
			} catch {
				
			}
            if (badTrade)
            {
                Log.ApiError($"Missed an item {item.i_market_name} costing {item.ui_price}");
                return false;
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
#if CAREFUL //sorry nothing is implemented there, I don't really know what to write as debug
            return false;


#else
            string resp = ExecuteApiRequest("/api/SetPrice/new_" + ClasssId + "_" + InstanceId + "/" + price.ToString() + "/?key=" + Api, ApiMethod.Sell);
            if (resp == null)
                return false;
            JObject parsed = JObject.Parse(resp);
            if (parsed["result"] == null)
                return false;
            else if ((bool)parsed["result"] == true) {
                return true; 
            }
            else
                return false;
#endif
        }

        public void PingPong()
        {
            while (true)
            {
                string uri = $"/api/PingPong/direct/?key={Api}";
                string resp = ExecuteApiRequest(uri);
                Thread.Sleep(30000);
            }
        }
        
        public JObject MassInfo(List<Tuple<string, string>> items, int sell = 0, int buy = 0, int history = 0, int info = 0, ApiMethod method = ApiMethod.GenericMassInfo) {
            string uri = $"/api/MassInfo/{sell}/{buy}/{history}/{info}?key={Api}";
            string data = "list=" + String.Join(",", items.Select(lr => lr.Item1 + "_" + lr.Item2).ToArray());
            try
            {
                string result = ExecuteApiPostRequest(uri, data);
                if (result == null)
                    return null;
                JObject temp = JObject.Parse(result);
                return temp;
            } catch (Exception ex) {
                Log.ApiError($"Tried to call {uri} with such data: {data}");
                Log.ApiError(ex.Message);
            }
            return null;
        }

        /// <summary>
        /// Provides server response by given list{name : price}
        /// Beware, server completely ignores failing items, doesn't include them in JObject and completely silently deletes them from answer
        /// </summary>
        /// <param name="items"></param>
        /// <returns></returns>
        public JObject MassSetPriceById(List<Tuple<string, int>> items, ApiMethod method = ApiMethod.GenericMassSetPriceById)
        {
            string uri = $"/api/MassSetPriceById/?key={Api}";
            string data = String.Join("&", items.Select(lr => $"list[{lr.Item1}]={lr.Item2}"));
            try
            {
                string result = ExecuteApiPostRequest(uri, data, method);
                if (result == null)
                    return null;
                JObject temp = JObject.Parse(result);
                return temp;
            }
            catch (Exception ex)
            {
                Log.Error("Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
            }
            return null;
        }

        public Inventory GetSteamInventory()
        {
            string a = ExecuteApiRequest("/api/GetInv/?key=" + Api, ApiMethod.GetSteamInventory);
            Inventory inventory = new Inventory();
            inventory.content = new List<Inventory.SteamItem>();
            if (a == null)
            {
                return inventory;
            }
            JObject json = JObject.Parse(a);
            if (json["ok"] != null && (bool)json["ok"] == false)
                return inventory;
            inventory.content = json["data"].ToObject<List<Inventory.SteamItem>>();
            return inventory;
        }

        private Dictionary<string, int> orders = new Dictionary<string, int>();

        public bool SetOrder(string classid, string instanceid, int price)
        {
            try
            {
#if CAREFUL
            return false;
#else
                string uri = "/api/ProcessOrder/" + classid + "/" + instanceid + "/" + price.ToString() + "/?key=" + Api;
                if (money < price)
                {
                    Log.ApiError("No money to set order, call to url was optimized :" + uri);
                    return false;
                }
                if (orders.ContainsKey($"{classid}_{instanceid}") && orders[$"{classid}_{instanceid}"] == price)
                {
                    Log.ApiError("Already have same order, call to url was optimized :" + uri);
                    return false;
                }
                string resp = ExecuteApiRequest(uri, ApiMethod.SetOrder);
                if (resp == null)
                    return false;
                JObject json = JObject.Parse(ExecuteApiRequest(uri, ApiMethod.SetOrder));
                if (json["success"] == null)
                {
                    Log.ApiError("Was unable to set order, url is :" + uri);
                    Log.ApiError(json.ToString());
                    return false;
                }
                else if ((bool)json["success"])
                {
                    orders[$"{classid}_{instanceid}"] = price;
                    return true;
                }
                else
                {
                    if (json.ContainsKey("error"))
                    {
                        if ((string)json["error"] == "same_price")
                            orders[$"{classid}_{instanceid}"] = price;
                        Log.ApiError($"Was unable to set order: url is {uri}, error message: {(string)json["error"]}");
                    }
                    else
                        Log.ApiError("Was unable to set order, url is :" + uri);
                    return false;
                }
#endif
            }
            catch (Exception ex)
            {
                Log.ApiError("Unknown error happened. Message: " + ex.Message + "\nTrace:" + ex.StackTrace);
                return false;
            }
        }

        bool UpdateInventory()
        {
            try
            {
                string resp = ExecuteApiRequest("/api/UpdateInventory/?key=" + Api, ApiMethod.UpdateInventory);
                if (resp == null)
                    return false;
                JObject json = JObject.Parse(resp);
                if (json["success"] == null)
                {
                    Log.ApiError("Was unable to update inventory");
                    return false;
                }
                else if ((bool)json["success"])
                    return true;
                else
                {
                    Log.ApiError("Was unable to update inventory");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.ApiError("Was unable to update inventory. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                return false;
            }
        }

        // If best offer is our's returning -1;
        public int getBestOrder(string classid, string instanceid)
        {
            try
            {
                string resp = ExecuteApiRequest("/api/ItemInfo/" + classid + "_" + instanceid + "/ru/?key=" + Api, ApiMethod.GetBestOrder);
                if (resp == null)
                    return -1;
                JObject x = JObject.Parse(resp);
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

        DateTime lastRefresh = DateTime.MinValue;
        
        public TMTrade[] GetTradeList()
        {
            try
            {
                string resp = ExecuteApiRequest("/api/Trades/?key=" + Api, ApiMethod.GetTradeList);
                if (resp == null)
                    return new TMTrade[0];
                JArray json = JArray.Parse(resp);
                TMTrade[] arr = new TMTrade[json.Count];

                
                int iter = 0;
                foreach (var thing in json)
                {
                    //Console.WriteLine("{0}", thing);
                    TMTrade xx = JsonConvert.DeserializeObject<TMTrade>(thing.ToString());
                    arr[iter++] = xx;
                }
                if (DateTime.Now.Subtract(lastRefresh).TotalMilliseconds > Consts.REFRESHINTERVAL)
                {
                    lastRefresh = DateTime.Now;
                    Task.Run(() => Logic.RefreshPrices(arr));
                }
                return arr;
            }
            catch (Exception ex)
            {
                Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                return new TMTrade[0];
            }
        }

        public int GetMoney() {
            string resp = ExecuteApiRequest("/api/GetMoney/?key=" + Api, ApiMethod.GetMoney);
            if (resp == null)
                return 0;
            JObject temp2 = JObject.Parse(ExecuteApiRequest("/api/GetMoney/?key=" + Api, ApiMethod.GetMoney));
            return (int)temp2["money"];
        }
    }
}
using System;
using System.Net;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Specialized;
using Newtonsoft.Json.Linq;
using System.Text;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Concurrent;
using SteamBot.MarketBot.CS;
using SteamTrade;
using Utility;
using SteamBot.MarketBot.CS.Bot;
using MongoDB.Driver;
using System.Diagnostics;
using Utility.MongoApi;

namespace CSGOTM {
    public class Logic {
        public NewMarketLogger Log;
        private readonly ReaderWriterLockSlim CurrentItemsLock = new ReaderWriterLockSlim();
        private readonly object RefreshItemsLock = new object();
        private readonly object UnstickeredRefreshItemsLock = new object();
        private static double MAXFROMMEDIAN = 0.78;
        private static double WANT_TO_BUY = 0.8;
        private static double UNSTICKERED_ORDER = 0.78;
        NewBuyFormula newBuyFormula = null;
        SellMultiplier sellMultiplier = null;
        TMBot parent;         
        public GenericInventory cachedInventory = null;
        public int cachedTradableCount = 0;
        private int stopsell = -1;
        public bool stopbuy = false;
        public bool obsolete_bot = false;

        public Logic(TMBot bot) {
            botName = bot.config.Username;
            parent = bot;
            PREFIXPATH = Path.Combine("CS", botName);
            UNSTICKEREDPATH = Path.Combine(PREFIXPATH, "emptystickered.txt");

            __database__ = new SalesDatabase(PREFIXPATH);
            __emptystickered__ = new EmptyStickeredDatabase();

            DATABASEJSONPATH = Path.Combine(PREFIXPATH, "database.json");
            BLACKLISTPATH = Path.Combine(PREFIXPATH, "blackList.txt");
            MONEYTPATH = Path.Combine(PREFIXPATH, "money.txt");
            if (!Directory.Exists(PREFIXPATH))
                Directory.CreateDirectory(PREFIXPATH);
            Tasking.Run(StartUp, botName);
        }

        ~Logic() {
            __database__.Save();
        }
        private void StartUp() {
            while (!parent.ReadyToRun) {
                Thread.Sleep(10);
            }
            FulfillBlackList();
            __emptystickered__.Load();
            __database__.Load();
            Tasking.Run(parent.InventoryFetcher, botName);
            RefreshConfig();
            Tasking.Run((Action)ParsingCycle, botName);
            Tasking.Run((Action)SaveDataBaseCycle, botName);
            Tasking.Run((Action)SellFromQueue, botName);
            Tasking.Run((Action)AddNewItems, botName);
            Tasking.Run((Action)UnstickeredRefresh, botName);
            Tasking.Run((Action)SetNewOrder, botName);
            if (!sellOnly) {
                Tasking.Run((Action)SetOrderForUnstickered, botName);
            }
            Tasking.Run((Action)RefreshConfigThread, botName);
        }

        void RefreshConfig() {

            JObject data = JObject.Parse(Utility.Request.Get(Consts.Endpoints.BotConfig));
            if (!data.TryGetValue(botName, out JToken Jtoken)) {
                Log.Error("Gist config contains no bot definition.");
            } else {
                JObject token = Jtoken as JObject;
                if (token.ContainsKey("obsolete_bot") && token["obsolete_bot"].Type == JTokenType.Boolean && (bool)token["obsolete_bot"])
                {
                    if (obsolete_bot != (bool)token["obsolete_bot"])
                    {
                        obsolete_bot = (bool)token["obsolete_bot"];
                        if (obsolete_bot)
                        {
                            Log.Info("Bot became obsolete.");
                        } else
                        {
                            Log.Warn("Bot became non-obsolete");
                        }
                    }
                }
                if (token["stopsell"].Type != JTokenType.Integer)
                    Log.Error("Have no idea when to stop selling");
                else {
                    if (stopsell != (int)token["stopsell"]) {
                        stopsell = (int)token["stopsell"];
                    }
                }
                if (token["stopbuy"].Type != JTokenType.Boolean)
                    Log.Error("Have no idea when to stop buying");
                else {
                    if (stopbuy != (bool)token["stopbuy"]) {
                        stopbuy = (bool)token["stopbuy"];
                    }
                }

                if (token["sell_only"].Type != JTokenType.Boolean)
                    Log.Error($"Sell only is not a boolean for {botName}");
                else {
                    if (sellOnly != (bool)token["sell_only"]) {
                        Log.Info("Sellonly was changed from {0} to {1}", sellOnly, (bool)token["sell_only"]);
                        sellOnly = (bool)token["sell_only"];
                    }
                }

                if (token["want_to_buy"].Type != JTokenType.Float)
                    Log.Error($"Want to buy is not a float for {botName}");
                else {
                    if (WANT_TO_BUY != (double)token["want_to_buy"]) {
                        Log.Info("Want to buy was changed from {0} to {1}", WANT_TO_BUY, (double)token["want_to_buy"]);
                        WANT_TO_BUY = (double)token["want_to_buy"];
                    }
                }

                if (token["max_from_median"].Type != JTokenType.Float)
                    Log.Error($"Max from median is not a float for {botName}");
                else {
                    if (MAXFROMMEDIAN != (double)token["max_from_median"]) {
                        Log.Info("Max from median was changed from {0} to {1}", WANT_TO_BUY, (double)token["max_from_median"]);
                        MAXFROMMEDIAN = (double)token["max_from_median"];
                    }
                }

                if (token["unstickered_order"].Type != JTokenType.Float)
                    Log.Error($"Unstickered order is not a float for {botName}");
                else {
                    if (UNSTICKERED_ORDER != (double)token["unstickered_order"]) {
                        Log.Info("Unsctickered order was changed from {0} to {1}", UNSTICKERED_ORDER, (double)token["unstickered_order"]);
                        UNSTICKERED_ORDER = (double)token["unstickered_order"];
                    }
                }
                if (token["experiments"] != null) {
                    if (token["experiments"]["new_buy_formula"] is JToken new_buy_formula && new_buy_formula != null) {
                        try {
                            DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                            DateTime start = DateTime.MinValue;
                            if (new_buy_formula["start"] != null)
                                start = dtDateTime.AddSeconds((double)new_buy_formula["start"]).ToLocalTime();
                            DateTime end = dtDateTime.AddSeconds((double)new_buy_formula["end"]).ToLocalTime();
                            if (DateTime.Now < end) {
                                double want_to_buy = (double)new_buy_formula["want_to_buy"];
                                NewBuyFormula temp = new NewBuyFormula(start, end, want_to_buy);
                                if (newBuyFormula != temp) {
                                    newBuyFormula = temp;
                                    Log.Info("New newBuyFormula applied:");
                                    Log.Info(new_buy_formula.ToString(Formatting.None));
                                }
                            }
                        } catch {
                            Log.Error("Incorrect experiment");
                        }
                    }
                    if (token["experiments"]["sell_multiplier"] is JToken sell_multiplier && sell_multiplier != null) {
                        try {
                            DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                            DateTime start = DateTime.MinValue;
                            if (sell_multiplier["start"] != null)
                                start = dtDateTime.AddSeconds((double)sell_multiplier["start"]).ToLocalTime();
                            DateTime end = dtDateTime.AddSeconds((double)sell_multiplier["end"]).ToLocalTime();
                            if (DateTime.Now < end) {
                                double sellmultiplier = (double)sell_multiplier["multiplier"];
                                SellMultiplier temp = new SellMultiplier(start, end, sellmultiplier);
                                if (sellMultiplier != temp) {
                                    sellMultiplier = temp;
                                    Log.Info("New sellmultiplier applied:");
                                    Log.Info(sell_multiplier.ToString(Formatting.None));
                                }
                            }
                        } catch {
                            Log.Error("Incorrect experiment");
                        }
                    }
                }
            }
        }

        void RefreshConfigThread() {
            while (parent.IsRunning()) {
                Utility.Tasking.WaitForFalseOrTimeout(parent.IsRunning, Consts.MINORCYCLETIMEINTERVAL).Wait();
                try {
                    RefreshConfig();

                } catch (Exception ex) {
                    Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                }
            }
        }

        private void SetOrderForUnstickered() {
            while (parent.IsRunning()) {
                if (Tasking.WaitForFalseOrTimeout(parent.IsRunning, 1000).Result)
                    continue;
                if (needOrderUnstickered.Count > 0) {
                    var top = needOrderUnstickered.Peek();
                    var info = Protocol.MassInfo(
                        new List<Tuple<string, string>> { new Tuple<string, string>(top.i_classid.ToString(), top.i_instanceid.ToString()) },
                        buy: 2, history: 1);
                    if (info == null || (string)info["success"] == "false") {
                        needOrderUnstickered.Dequeue();
                        Log.Warn("MassInfo failed, could not place order.");
                        continue;
                    }

                    var res = info["results"][0];
                    JArray history = (JArray)res["history"]["history"];

                    double sum = 0;
                    int cnt = 0;
                    long time = long.Parse((string)history[0][0]);
                    for (int i = 0; i < history.Count && time - long.Parse((string)history[i][0]) < 10 * Consts.DAY; i++) {
                        sum += int.Parse((string)history[i][1]);
                        cnt++;
                    }

                    double price = sum / cnt;
                    if (cnt < 15) {
                        needOrderUnstickered.Dequeue();
                        continue;
                    }

                    int curPrice = 50;
                    try {
                        if (res["buy_offers"] != null && res["buy_offers"].Type != JTokenType.Boolean
                            && res["buy_offers"]["best_offer"] != null) {
                            curPrice = int.Parse((string)res["buy_offers"]["best_offer"]);
                        }
                    } catch (Exception ex) {
                        Log.Info(res.ToString(Formatting.None));
                        Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                    }

                    if (price > 9000 && curPrice < price * UNSTICKERED_ORDER && !blackList.Contains(top.i_market_name)) {
                        Protocol.SetOrder(top.i_classid, top.i_instanceid, curPrice + 1);
                    }
                    if (needOrderUnstickered.Count < Consts.MINORDERQUEUESIZE) {
                        needOrderUnstickered.Enqueue(top);
                    }
                    needOrderUnstickered.Dequeue();
                }
            }
        }

        public void RefreshPrices(TMTrade[] trades) {
            lock (RefreshItemsLock) lock (UnstickeredRefreshItemsLock) {
                    unstickeredRefresh.Clear();
                    for (int i = 1; i <= trades.Length; i++) {
                        var cur = trades[trades.Length - i];
                        if (!hasStickers(cur)) {
                            unstickeredRefresh.Enqueue(cur);
                        }
                        if (i <= 7 && cur.ui_status == "1") {
                            if (GetMySellPriceByName(cur.i_market_name) != -1)
                                refreshPrice.Enqueue(cur);
                        }
                    }
                }
        }

        void FulfillBlackList() {
            if (!File.Exists(BLACKLISTPATH)) {
                Log.Warn("Blacklist doesnt exist");
                return;
            }
            string[] lines = File.ReadAllLines(BLACKLISTPATH, Encoding.UTF8);
            foreach (var line in lines) {
                blackList.Add(line);
            }
        }

        void SetNewOrder() {
            while (parent.IsRunning()) {
                if (Tasking.WaitForFalseOrTimeout(parent.IsRunning, 1000).Result)
                    return;
                if (needOrder.Count != 0) {
                    NewHistoryItem item = needOrder.Dequeue();
                    bool took = false;
                    try {
                        int price = Protocol.getBestOrder(item.i_classid, item.i_instanceid);
                        if (price != -1 && price < 30000) {
                            __database__.EnterReadLock();
                            took = true;
                            if (__database__.newDataBase[item.i_market_name].GetMedian() * MAXFROMMEDIAN - price > 30) {
                                Protocol.SetOrder(item.i_classid, item.i_instanceid, ++price);
                            }
                        }
                    } catch (Exception ex) {
                        Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                    } finally {
                        if (took)
                            __database__.ExitReadLock();
                    }
                }

            }
        }

        void AddNewItems() {
            while (parent.IsRunning()) {
                if (Tasking.WaitForFalseOrTimeout(parent.IsRunning, 3000).Result)
                    continue;
                while (!toBeSold.IsEmpty) {
                    Thread.Sleep(1000);
                }
                SpinWait.SpinUntil(() => (doNotSell || toBeSold.IsEmpty));
                if (doNotSell) {
                    //doNotSell = false;
                    //Thread.Sleep(1000 * 60 * 2); //can't lower it due to some weird things in protocol, requires testing
                } else {
                    try {
                        Inventory inventory = Protocol.GetSteamInventory();
                        foreach (Inventory.SteamItem item in inventory.content) {
                            toBeSold.Enqueue(item);
                        }
                    } catch (Exception ex) {
                        Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                    }
                }
            }
        }

        int GetMySellPriceByName(string name) {
            CurrentItemsLock.EnterReadLock();
            if (currentItems.TryGetValue(name, out List<int> prices) && prices.Count > 2) {
                int price = -1;
                if (DateTime.Now.Hour > 10 && DateTime.Now.Hour < 12
                    && prices.Count > 4
                    && prices[4] / prices[2] <= 1.3) {
                    price = prices[4] - 30;
                } else if (DateTime.Now.Hour > 10 && DateTime.Now.Hour < 15
                        && prices.Count > 3
                        && prices[3] / prices[2] <= 1.2) {
                    price = prices[3] - 30;
                } else {
                    price = prices[2] - 30;
                }
                __database__.EnterReadLock();
                if (__database__.newDataBase.TryGetValue(name, out BasicSalesHistory saleHistory) && (price > 2 * saleHistory.GetMedian() || price == -1)) {
                    price = 2 * saleHistory.GetMedian();
                }
                __database__.ExitReadLock();
                CurrentItemsLock.ExitReadLock();
                return price;
            }
            CurrentItemsLock.ExitReadLock();
            __database__.EnterReadLock();
            if (__database__.newDataBase.TryGetValue(name, out BasicSalesHistory salesHistory)) {
                __database__.ExitReadLock();
                return salesHistory.GetMedian();
            }
            __database__.ExitReadLock();
            return -1;
        }

        /// <summary>
        /// Returns price to sell for or -1
        /// </summary>
        /// <returns></returns>
        int GetMySellPrice(Inventory.SteamItem item) {
            if (!hasStickers(item))
                return GetMyUnstickeredSellPrice(item);

            if (ManipulatedItems.TryGetValue(item.i_classid + "_" + item.i_instanceid, out int ret)) {
                return ret;
            } else {
                int temp = GetMySellPriceByName(item.i_market_name);
                if (temp != -1)
                    if (sellMultiplier != null && sellMultiplier.IsRunning()) {
                        temp = (int)(temp * sellMultiplier.Multiplier);
                    }
                return temp;
            }
        }


        /// <summary>
        /// Returns price to sell for or -1
        /// </summary>
        /// <returns></returns>
        int GetMyUnstickeredSellPrice(Inventory.SteamItem item) {
            if (ManipulatedItems.TryGetValue(item.i_classid + "_" + item.i_instanceid, out int ret)) {
                return ret;
            } else {
                __database__.EnterReadLock();
                int price = -1;
                if (__database__.newDataBase.TryGetValue(item.i_market_name, out BasicSalesHistory salesHistory))
                    price = salesHistory.GetMedian();
                __database__.ExitReadLock();
                return price;
            }
        }

        void UnstickeredRefresh() {
            while (parent.IsRunning()) {
                if (Tasking.WaitForFalseOrTimeout(parent.IsRunning, 1000).Result)
                    continue;
                try {
                    Queue<TMTrade> unstickeredTemp;
                    lock (UnstickeredRefreshItemsLock) {
                        unstickeredTemp = new Queue<TMTrade>(unstickeredRefresh);
                    }
                    while (unstickeredTemp.Count > 0) {
                        Queue<TMTrade> unstickeredChunk = new Queue<TMTrade>(unstickeredTemp.Take(100));
                        for (int i = 0; i < unstickeredChunk.Count; ++i)
                            unstickeredTemp.Dequeue();
                        List<Tuple<string, string>> tpls = new List<Tuple<string, string>>();
                        foreach (var x in unstickeredChunk) {
                            tpls.Add(new Tuple<string, string>(x.i_classid, x.i_instanceid));
                        }
                        JObject info = Protocol.MassInfo(tpls, history: 1, sell: 1, method: Protocol.ApiMethod.UnstickeredMassInfo);
                        if (info == null)
                            break;
                        List<Tuple<string, int>> items = new List<Tuple<string, int>>();
                        Dictionary<string, Tuple<int, int>[]> marketOffers = new Dictionary<string, Tuple<int, int>[]>();
                        Dictionary<string, int> myOffer = new Dictionary<string, int>();
                        foreach (JToken token in info["results"]) {
                            string cid = (string)token["classid"];
                            string iid = (string)token["instanceid"];
                            if (token["sell_offers"].Type == JTokenType.Null || token["sell_offers"].Type == JTokenType.Boolean)
                                continue;
                            Tuple<int, int>[] arr = new Tuple<int, int>[0];
                            try {
                                arr = token["sell_offers"]["offers"].Select(x => new Tuple<int, int>((int)x[0], (int)x[1])).ToArray();
                            } catch {
                                throw;
                                //Log.Info(token.ToString(Formatting.Indented));
                            }
                            marketOffers[$"{cid}_{iid}"] = arr;
                            //think it cant be empty because we have at least one order placed.
                            try {
                                myOffer[$"{cid}_{iid}"] = (int)token["sell_offers"]["my_offers"].Min();
                            } catch {
                                myOffer[$"{cid}_{iid}"] = arr[0].Item1 + 1;
                            }
                        }
                        foreach (TMTrade trade in unstickeredChunk) {
                            try {
                                //think it cant be empty because we have at least one order placed.
                                if (marketOffers[$"{trade.i_classid}_{trade.i_instanceid}"][0].Item1 <= myOffer[$"{trade.i_classid}_{trade.i_instanceid}"]) {
                                    int coolPrice = marketOffers[$"{trade.i_classid}_{trade.i_instanceid}"][0].Item1 - 1;
                                    int careful = (int)info["results"].First(x => (string)x["classid"] == trade.i_classid && (string)x["instanceid"] == trade.i_instanceid)["history"]["average"];
                                    if (coolPrice < careful * 0.9)
                                        coolPrice = 0;
                                    items.Add(new Tuple<string, int>(trade.ui_id, coolPrice));
                                } else {
                                    int coolPrice = marketOffers[$"{trade.i_classid}_{trade.i_instanceid}"][1].Item1 - 1;
                                    int careful = (int)info["results"].First(x => (string)x["classid"] == trade.i_classid && (string)x["instanceid"] == trade.i_instanceid)["history"]["average"];
                                    if (coolPrice < careful * 0.9)
                                        coolPrice = 0;
                                    items.Add(new Tuple<string, int>(trade.ui_id, coolPrice));
                                }
                            } catch { }
                        }
                        /*JOBject obj = */
                        Protocol.MassSetPriceById(items, method: Protocol.ApiMethod.UnstickeredMassSetPriceById);
                    }
                } catch (Exception ex) {
                    Log.Error($"Some error happened. Message: {ex.Message} \nTrace: {ex.StackTrace}");
                }
            }
        }

        void SellFromQueue() {
            while (parent.IsRunning()) {
                if (Tasking.WaitForFalseOrTimeout(parent.IsRunning, 1000).Result)
                    continue;
                while (refreshPrice.IsEmpty && toBeSold.IsEmpty)
                    Thread.Sleep(1000);
                if (!refreshPrice.IsEmpty) {
                    lock (RefreshItemsLock) {
                        List<Tuple<string, int>> items = new List<Tuple<string, int>>();
                        items = new List<Tuple<string, int>>();
                        while (refreshPrice.TryDequeue(out TMTrade trade)) {
                            items.Add(new Tuple<string, int>(trade.ui_id, 0));
                        }
                        /*JOBject obj = */
                        Protocol.MassSetPriceById(items);
                    }
                } else if (toBeSold.TryDequeue(out Inventory.SteamItem item)) {
                    if (cachedInventory != null && cachedTradableCount < stopsell) {
                        while (toBeSold.TryDequeue(out item)); //clear whole queue
                        Protocol.RemoveAll();
                        continue;
                    }
                    int price = GetMySellPrice(item);
                    if (price != -1) {
                        try {
                            string[] ui_id = item.ui_id.Split('_');
                            if (Protocol.SellNew(long.Parse(ui_id[1]), long.Parse(ui_id[2]), price)) {
                                Log.Success($"New {item.i_market_name} is on sale for {price}");
                            } else {
                                Log.ApiError(TMBot.RestartPriority.SmallError, "Could not sell new item, enqueuing it again.");
                            }
                        } catch {
                        }
                    }
                }
            }
        }

        void ParsingCycle() {
            while (parent.IsRunning()) {
                if (ParseNewDatabase()) {
                    Log.Success("Finished parsing new DB");
                } else {
                    Log.Error("Couldn\'t parse new DB");
                }
                Tasking.WaitForFalseOrTimeout(parent.IsRunning, Consts.PARSEDATABASEINTERVAL).Wait();
            }
        }

        void SaveDataBaseCycle() {
            while (parent.IsRunning()) {
                __database__.Save();
                Tasking.WaitForFalseOrTimeout(parent.IsRunning, Consts.MINORCYCLETIMEINTERVAL).Wait();
            }
        }

        public void LoadDataBaseFromMongo() {
            MongoHistoryCSGO mongoHistory = new MongoHistoryCSGO();
            List<MongoHistoryItem> items = mongoHistory.FindAll().ToEnumerable().OrderBy(x => x.id.Timestamp).ToList();
            __database__.EnterWriteLock();
            try {
                foreach (MongoHistoryItem item in items) {
                    if (!__database__.newDataBase.TryGetValue(item.i_market_name, out BasicSalesHistory temp)) {
                        temp = new BasicSalesHistory();
                        __database__.newDataBase.Add(item.i_market_name, temp);
                    }
                    temp.Add(item);
                }
                foreach (var kv in __database__.newDataBase) {
                    kv.Value.Recalculate();
                }
            } 
            finally {
                __database__.ExitWriteLock();
            }
        }

#if CAREFUL
        public void SaveJSONDataBase()
        {
            JsonSerialization.WriteToJsonFile(DATABASEJSONPATH, dataBase);
        }
#endif

        public void AddEmptyStickered(long cid, long uid) {
            __emptystickered__.Add(cid, uid);
        }

        bool ParseNewDatabase() {
            try {
                try {
                    string[] lines;
                    {
                        JObject things =
                            JObject.Parse(
                                Utility.Request.Get("https://market.csgo.com/itemdb/current_730.json"));
                        string db = (string)things["db"];
                        lines = Utility.Request.Get("https://market.csgo.com/itemdb/" + db).Split('\n');
                    }
                    string[] indexes = lines[0].Split(';');
                    int id = 0;

                    if (NewItem.mapping.Count == 0)
                        foreach (var str in indexes)
                            NewItem.mapping[str] = id++;
                    Dictionary<string, List<int>> currentItemsCache = new Dictionary<string, List<int>>();

                    for (id = 1; id < lines.Length - 1; ++id) {
                        string[] item = lines[id].Split(';');
                        if (item[NewItem.mapping["c_stickers"]] == "0")

                            AddEmptyStickered(long.Parse(item[NewItem.mapping["c_classid"]]), long.Parse(item[NewItem.mapping["c_instanceid"]]));
                        // new logic
                        else {
                            String name = item[NewItem.mapping["c_market_name"]];
                            if (name.Length >= 2) {
                                name = name.Remove(0, 1);
                                name = name.Remove(name.Length - 1);
                            } else {
                                continue;
                            }

                            if (!currentItemsCache.ContainsKey(name))
                                currentItemsCache.Add(name, new List<int>());
                            if (int.TryParse(item[NewItem.mapping["c_price"]], out int val)) {
                                currentItemsCache[name].Add(val);
                            } else {
                                Log.Warn($"{item[NewItem.mapping["c_price"]]} doesnt seem like a valid price");
                            }
                        }
                    }
                    CurrentItemsLock.EnterWriteLock();
                    this.currentItems = currentItemsCache;
                    SortCurrentItems();
                    CurrentItemsLock.ExitWriteLock();

                    // Calling WantToBuy function for all items. 
                    indexes = lines[0].Split(';');
                    id = 0;
                    for (id = 1; id < lines.Length - 1; ++id) {
                        string[] itemInString = lines[id].Split(';');
                        NewItem newItem = new NewItem(itemInString);
                        //if (newItem.i_market_name == "") {
                        //    Log.Info("Item has no name");
                        //}
                        if (WantToBuy(newItem)) {
                            Protocol.Buy(newItem);
                        }
                    }
                } catch (Exception ex) {
                    Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                }
                return true;
            } catch (Exception e) {
                Log.Error(e.Message);
                return false;
            }
        }

        public void SortCurrentItems() {
            try {
                foreach (String name in currentItems.Keys)
                    currentItems[name].Sort();

#if CAREFUL
                String[] data = new String[currentItems.Count];
                int i = 0;
                foreach (String name in currentItems.Keys)
                {
                    if (dataBase.ContainsKey(name) && currentItems[name].Count >= 4)
                        data[i++] =
 String.Format("{0:0.00}", ((double)dataBase[name].median / currentItems[name][3] - 1) * 100) + "%   " +
                           name + " median: " + dataBase[name].median + "  new value: " + currentItems[name][3];
                }
                //data[i++] = name + currentItems[name][0];
                File.WriteAllLines("stat.txt", data);
#endif
            } catch (Exception ex) {
                Log.Error("Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
            }
        }

        [Serializable]
        public class BasicSalesHistory {
            private List<BasicHistoryItem> sales = new List<BasicHistoryItem>();
            private int median;
            [NonSerialized]
            private bool fresh = true;

            public BasicSalesHistory() { }
            public BasicSalesHistory(SalesHistory sales) {
                median = sales.GetMedian();
                this.sales = sales.sales.Select(sale => new BasicHistoryItem(sale)).ToList();
            }

            public int GetCnt() {
                return sales.Count;
            }

            public void Recalculate() {
                int[] a = new int[sales.Count];
                for (int i = 0; i < sales.Count; i++)
                    a[i] = sales[i].price;
                Array.Sort(a);
                median = a[sales.Count / 2];
                fresh = true;
            }

            public int GetMedian() {
                if (!fresh) {
                    Recalculate();
                }
                return median;
            }

            public void Add(NewHistoryItem item) {
                median = item.price;
                fresh = false;
            }
        }

        [Serializable]
        public class SalesHistory {
            public List<NewHistoryItem> sales = new List<NewHistoryItem>();
            private int median;
            private int cnt;
            [NonSerialized]
            private bool fresh = true;

            public int GetCnt() {
                return cnt;
            }

            public void Recalculate() {
                cnt = Math.Min(cnt, sales.Count);
                if (cnt != 0) {
                    int[] a = new int[cnt];
                    for (int i = 0; i < cnt; i++)
                        a[i] = sales[i].price;
                    Array.Sort(a);
                    median = a[cnt / 2];
                } else {
                    median = 0;
                }
                fresh = true;
            }

            public int GetMedian() {
                if (!fresh) {
                    Recalculate();
                }
                return median;
            }

            public SalesHistory() {
                cnt = 0;
                median = 0;
            }

            public SalesHistory(NewHistoryItem item) : this() {
                Add(item);
            }

            public void Add(NewHistoryItem item) {
                sales.Add(item);
                median = item.price;
                fresh = false;
            }
        }

        bool isSouvenir(string s) {
            string lowerName = s.ToLower();
            return lowerName.Contains("souvenir") || lowerName.Contains("сувенир");
        }

        bool hasStickers(Inventory.SteamItem item) {
            if (isSouvenir(item.i_market_name) || isSouvenir(item.i_market_hash_name))
                return false;
            return !__emptystickered__.NoStickers(item.i_classid, item.i_instanceid);
        }

        bool hasStickers(TMTrade trade) {
            if (isSouvenir(trade.i_market_name) || isSouvenir(trade.i_market_hash_name))
                return false;
            return !__emptystickered__.NoStickers(trade.i_classid, trade.i_instanceid);
        }

        bool hasStickers(NewItem item) {
            if (isSouvenir(item.i_market_name))
                return false;
            return !__emptystickered__.NoStickers(item.i_classid, item.i_instanceid);
        }

        bool hasStickers(NewHistoryItem item) {
            if (isSouvenir(item.i_market_name))
                return false;
            return !__emptystickered__.NoStickers(item.i_classid, item.i_instanceid);
        }

        public void ProcessItem(NewHistoryItem item) {
            if (!hasStickers(item)) {
                if (needOrderUnstickered.Count < Consts.MAXORDERQUEUESIZE) {
                    needOrderUnstickered.Enqueue(item);
                }
                return;
            }

            //Console.WriteLine(item.i_market_name);
            __database__.EnterWriteLock();
            try {
                if (!__database__.newDataBase.TryGetValue(item.i_market_name, out BasicSalesHistory salesHistory)) {
                    salesHistory = new BasicSalesHistory();
                    __database__.newDataBase.Add(item.i_market_name, salesHistory);
                }
                salesHistory.Add(item);
                salesHistory.Recalculate();

                if (salesHistory.GetCnt() >= Consts.MINSIZE && !blackList.Contains(item.i_market_name)) {
                    if (needOrder.Count <= Consts.MAXORDERQUEUESIZE) {
                        needOrder.Enqueue(item);
                    }
                }
            } finally {
                __database__.ExitWriteLock();
            }
        }

        public bool L1(NewItem item) {
            return item.ui_price < 400000L && !blackList.Contains(item.i_market_name);
        }

        public bool WantToBuy(NewItem item) {
            if (stopbuy)
                return false;
            if (!hasStickers(item)) {
                //we might want to manipulate it.
                string id = item.i_classid + "_" + item.i_instanceid;
                if (!ManipulatedItems.TryGetValue(id, out int price))
                    return false;
                return price < item.ui_price + 10;
            }

            if (!L1(item)) {
                return false;
            }
            __database__.EnterReadLock();
            CurrentItemsLock.EnterReadLock();
            try { 
                if (!__database__.newDataBase.TryGetValue(item.i_market_name, out BasicSalesHistory salesHistory)) {
                    return false;
                }
                if (newBuyFormula != null && newBuyFormula.IsRunning()) {
                    if (item.ui_price < newBuyFormula.WantToBuy * salesHistory.GetMedian()
                        && salesHistory.GetMedian() - item.ui_price > 1000
                        && salesHistory.GetCnt() >= Consts.MINSIZE) {
                        return true; //back to good ol' dayz
                    }
                }

                if (!currentItems.TryGetValue(item.i_market_name, out List<int> prices)) {
                    return false;
                }
                //if (item.ui_price < 40000 && salesHistory.cnt >= MINSIZE && item.ui_price < 0.8 * salesHistory.median && salesHistory.median - item.ui_price > 600 && !blackList.Contains(item.i_market_name))

                //else
                {
                    if (prices.Count >= 6
                        && item.ui_price < WANT_TO_BUY * prices[2]
                        && salesHistory.GetCnt() >= Consts.MINSIZE
                        && prices[2] < salesHistory.GetMedian() * 1.2
                        && prices[2] - item.ui_price > 1000) {
                        Log.Info("Going to buy " + item.i_market_name + ". Expected profit " +
                                    (salesHistory.GetMedian() - item.ui_price));
                        return true;
                    }
                }
            } finally {
                __database__.ExitReadLock();
                CurrentItemsLock.ExitReadLock();
            }

            return false;
        }

        public bool doNotSell = false; // True when we don`t want to sell.
        public bool sellOnly = false;
        public Protocol Protocol;

        public string botName;

        private string PREFIXPATH;

        private string UNSTICKEREDPATH;
        private string DATABASEJSONPATH;
        private string BLACKLISTPATH;
        private string MONEYTPATH;

        private ConcurrentQueue<Inventory.SteamItem> toBeSold = new ConcurrentQueue<Inventory.SteamItem>();
        private ConcurrentQueue<TMTrade> refreshPrice = new ConcurrentQueue<TMTrade>();
        private Queue<TMTrade> unstickeredRefresh = new Queue<TMTrade>();

        private Queue<NewHistoryItem> needOrder = new Queue<NewHistoryItem>();
        private Queue<NewHistoryItem> needOrderUnstickered = new Queue<NewHistoryItem>();
        private HashSet<string> blackList = new HashSet<string>();
        public SalesDatabase __database__;
        public EmptyStickeredDatabase __emptystickered__;

        private Dictionary<string, List<int>> currentItems = new Dictionary<string, List<int>>();

        private Dictionary<String, int> ManipulatedItems = new Dictionary<string, int>(); // [cid_iid] -> price
    }
}

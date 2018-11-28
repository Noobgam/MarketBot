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

namespace CSGOTM {
    public class Logic {
        public NewMarketLogger Log;
        public readonly ReaderWriterLockSlim _DatabaseLock = new ReaderWriterLockSlim();
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

        public Logic(TMBot bot) {
            this.botName = bot.config.Username;
            parent = bot;
            PREFIXPATH = Path.Combine("CS", botName);
            UNSTICKEREDPATH = Path.Combine(PREFIXPATH, "emptystickered.txt");
            DATABASEPATH = Path.Combine(PREFIXPATH, "database.txt");
            DATABASETEMPPATH = Path.Combine(PREFIXPATH, "databaseTemp.txt");
            DATABASEJSONPATH = Path.Combine(PREFIXPATH, "database.json");
            BLACKLISTPATH = Path.Combine(PREFIXPATH, "blackList.txt");
            MONEYTPATH = Path.Combine(PREFIXPATH, "money.txt");
            Thread starter = new Thread(new ThreadStart(StartUp));
            if (!Directory.Exists(PREFIXPATH))
                Directory.CreateDirectory(PREFIXPATH);
            starter.Start();
        }

        ~Logic() {
            SaveDataBase();
            _DatabaseLock.Dispose();
        }

        private void StartUp() {
            while (!parent.ReadyToRun) {
                Thread.Sleep(10);
            }

            LoadNonStickeredBase();
            FulfillBlackList();
            LoadDataBase();
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
            if (!data.TryGetValue(botName, out JToken token)) {
                Log.Error("Gist config contains no bot definition.");
            } else {
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
                    if (info == null)
                        continue; //unlucky
                    if (info == null || (string)info["success"] == "false") {
                        needOrderUnstickered.Dequeue();
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
                    needOrderUnstickered.Dequeue();
                }
            }
        }

        public void RefreshPrices(TMTrade[] trades) {
            lock (RefreshItemsLock) lock (UnstickeredRefreshItemsLock) {
                    unstickeredRefresh.Clear();
                    for (int i = 1; i <= trades.Length; i++) {
                        var cur = trades[trades.Length - i];
                        if (!hasStickers(cur.i_classid, cur.i_instanceid)) {
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
                            _DatabaseLock.EnterReadLock();
                            took = true;
                            if (dataBase[item.i_market_name].median * MAXFROMMEDIAN - price > 30) {
                                Protocol.SetOrder(item.i_classid, item.i_instanceid, ++price);
                            }
                        }
                    } catch (Exception ex) {
                        Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                    } finally {
                        if (took)
                            _DatabaseLock.ExitReadLock();
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
                _DatabaseLock.EnterReadLock();
                if (dataBase.TryGetValue(name, out SalesHistory saleHistory) && (price > 2 * saleHistory.median || price == -1)) {
                    price = 2 * saleHistory.median;
                }
                _DatabaseLock.ExitReadLock();
                CurrentItemsLock.ExitReadLock();
                return price;
            }
            CurrentItemsLock.ExitReadLock();
            _DatabaseLock.EnterReadLock();
            if (dataBase.TryGetValue(name, out SalesHistory salesHistory)) {
                _DatabaseLock.ExitReadLock();
                return salesHistory.median;
            }
            _DatabaseLock.ExitReadLock();
            return -1;
        }

        /// <summary>
        /// Returns price to sell for or -1
        /// </summary>
        /// <returns></returns>
        int GetMySellPrice(Inventory.SteamItem item) {
            if (!hasStickers(item.i_classid, item.i_instanceid))
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
                _DatabaseLock.EnterReadLock();
                int price = -1;
                if (dataBase.TryGetValue(item.i_market_name, out SalesHistory salesHistory))
                    price = salesHistory.median;
                _DatabaseLock.ExitReadLock();
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
                SaveDataBase();
                Tasking.WaitForFalseOrTimeout(parent.IsRunning, Consts.MINORCYCLETIMEINTERVAL).Wait();
            }
        }


        public void LoadDataBase() {
            _DatabaseLock.EnterWriteLock();
            if (!File.Exists(DATABASEPATH) && !File.Exists(DATABASETEMPPATH)) {
                _DatabaseLock.ExitWriteLock();
                return;
            }
            try {
                dataBase = BinarySerialization.ReadFromBinaryFile<Dictionary<string, SalesHistory>>(DATABASEPATH);
                if (File.Exists(DATABASETEMPPATH))
                    File.Delete(DATABASETEMPPATH);
                Log.Success("Loaded new DB. Total item count: " + dataBase.Count);
                _DatabaseLock.ExitWriteLock();
            } catch (Exception ex) {
                Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                if (File.Exists(DATABASEPATH))
                    File.Delete(DATABASEPATH);
                if (File.Exists(DATABASETEMPPATH))
                    File.Move(DATABASETEMPPATH, DATABASEPATH);
                _DatabaseLock.ExitWriteLock();
                LoadDataBase();
            }
        }

        public void SaveDataBase() {
            if (File.Exists(DATABASEPATH))
                File.Copy(DATABASEPATH, DATABASETEMPPATH, true);
            Log.Info($"Size of db is {dataBase.Count}");
            _DatabaseLock.EnterReadLock();
            BinarySerialization.WriteToBinaryFile(DATABASEPATH, dataBase);
            _DatabaseLock.ExitReadLock();
        }

#if CAREFUL
        public void SaveJSONDataBase()
        {
            JsonSerialization.WriteToJsonFile(DATABASEJSONPATH, dataBase);
        }
#endif

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

                            unstickeredCache.Add(new Tuple<long, long>(long.Parse(item[NewItem.mapping["c_classid"]]), long.Parse(item[NewItem.mapping["c_instanceid"]])));
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
                    SaveNonStickeredBase();
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
                    Log.Error(ex.Message);
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

        bool LoadNonStickeredBase() {
            try {
                string[] lines = File.ReadAllLines(UNSTICKEREDPATH);
                foreach (var line in lines) {
                    string[] item = line.Split('_');
                    unstickeredCache.Add(new Tuple<long, long>(long.Parse(item[0]), long.Parse(item[1])));
                }
                return true;
            } catch (Exception e) {
                Log.Warn("Could not load unstickered DB, check whether DB name is correct (\'" + UNSTICKEREDPATH +
                         "\'):\n" + e.Message);
                return false;
            }
        }

        bool SaveNonStickeredBase() {
            try {
                if (File.Exists(UNSTICKEREDPATH))
                    File.Delete(UNSTICKEREDPATH);
                string[] lines = new string[unstickeredCache.Count];
                int id = 0;
                foreach (var line in unstickeredCache) {
                    lines[id++] = string.Format("{0}_{1}", line.Item1, line.Item2);
                }
                File.WriteAllLines(UNSTICKEREDPATH, lines);
                return true;
            } catch (Exception e) {
                Log.Info(
                    "Could not save unstickered DB, check whether DB name is correct (\'emptystickered.txt\'). Maybe this file is write-protected?:\n" +
                    e.Message);
                return false;
            }
        }

        [Serializable]
        public class SalesHistory {
            public List<NewHistoryItem> sales = new List<NewHistoryItem>();
            public int median;
            public int cnt;

            public SalesHistory(NewHistoryItem item) {
                cnt = 1;
                sales.Add(item);
            }
        }

        bool hasStickers(string classId, string instanceId) {
            return !unstickeredCache.Contains(new Tuple<long, long>(long.Parse(classId), long.Parse(instanceId)));
        }

        bool hasStickers(NewItem item) {
            return !unstickeredCache.Contains(new Tuple<long, long>(item.i_classid, item.i_instanceid));
        }

        bool hasStickers(NewHistoryItem item) {
            return !unstickeredCache.Contains(new Tuple<long, long>(item.i_classid, item.i_instanceid));
        }

        public void ProcessItem(NewHistoryItem item) {
            if (!hasStickers(item)) {
                needOrderUnstickered.Enqueue(item);
                return;
            }

            //Console.WriteLine(item.i_market_name);
            _DatabaseLock.EnterWriteLock();
            try {
                if (dataBase.TryGetValue(item.i_market_name, out SalesHistory salesHistory)) {
                    if (salesHistory.cnt == MAXSIZE)
                        salesHistory.sales.RemoveAt(0);
                    else
                        salesHistory.cnt++;
                    salesHistory.sales.Add(item);
                } else {
                    salesHistory = new SalesHistory(item);
                    dataBase.Add(item.i_market_name, salesHistory);
                }

                int[] a = new int[salesHistory.cnt];
                for (int i = 0; i < salesHistory.cnt; i++)
                    a[i] = salesHistory.sales[i].price;
                Array.Sort(a);
                salesHistory.median = a[salesHistory.cnt / 2];

                if (salesHistory.cnt >= Consts.MINSIZE && !blackList.Contains(item.i_market_name)) {
                    needOrder.Enqueue(item);
                }
            } finally {
                _DatabaseLock.ExitWriteLock();
            }
        }

        public bool L1(NewItem item) {
            return item.ui_price < 100000 && !blackList.Contains(item.i_market_name);
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
            _DatabaseLock.EnterReadLock();
            CurrentItemsLock.EnterReadLock();
            try { 
                if (!dataBase.TryGetValue(item.i_market_name, out SalesHistory salesHistory)) {
                    return false;
                }
                if (newBuyFormula != null && newBuyFormula.IsRunning()) {
                    if (item.ui_price < newBuyFormula.WantToBuy * salesHistory.median
                        && salesHistory.median - item.ui_price > 1000
                        && salesHistory.cnt >= Consts.MINSIZE) {
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
                        && salesHistory.cnt >= Consts.MINSIZE
                        && prices[2] < salesHistory.median * 1.2
                        && prices[2] - item.ui_price > 1000) {
                        Log.Info("Going to buy " + item.i_market_name + ". Expected profit " +
                                    (salesHistory.median - item.ui_price));
                        return true;
                    }
                }
            } finally {
                _DatabaseLock.ExitReadLock();
                CurrentItemsLock.ExitReadLock();
            }

            return false;
        }

        public bool doNotSell = false; // True when we don`t want to sell.
        public bool sellOnly = false;
        public Protocol Protocol;

        public string botName;

        private const int MAXSIZE = 12000;

        private string PREFIXPATH;
        private HashSet<Tuple<long, long>> unstickeredCache = new HashSet<Tuple<long, long>>();

        private string UNSTICKEREDPATH;
        private string DATABASEPATH;
        private string DATABASETEMPPATH;
        private string DATABASEJSONPATH;
        private string BLACKLISTPATH;
        private string MONEYTPATH;

        private ConcurrentQueue<Inventory.SteamItem> toBeSold = new ConcurrentQueue<Inventory.SteamItem>();
        private ConcurrentQueue<TMTrade> refreshPrice = new ConcurrentQueue<TMTrade>();
        private Queue<TMTrade> unstickeredRefresh = new Queue<TMTrade>();

        private Queue<NewHistoryItem> needOrder = new Queue<NewHistoryItem>();
        private Queue<NewHistoryItem> needOrderUnstickered = new Queue<NewHistoryItem>();
        private HashSet<string> blackList = new HashSet<string>();
        public Dictionary<string, SalesHistory> dataBase = new Dictionary<string, SalesHistory>();

        private Dictionary<string, List<int>> currentItems = new Dictionary<string, List<int>>();

        private Dictionary<String, int> ManipulatedItems = new Dictionary<string, int>(); // [cid_iid] -> price
    }
}

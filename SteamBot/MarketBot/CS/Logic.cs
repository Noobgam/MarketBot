﻿using System;
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

namespace CSGOTM {
    public class Logic {
        public Utility.MarketLogger Log;
        private static Mutex DatabaseLock = new Mutex();
        private static Mutex CurrentItemsLock = new Mutex();
        private static Mutex RefreshItemsLock = new Mutex();
        private static Mutex UnstickeredRefreshItemsLock = new Mutex();

        public Logic(String botName)
        {
            this.botName = botName;
            Thread starter = new Thread(new ThreadStart(StartUp));
            if (!Directory.Exists(PREFIXPATH))
                Directory.CreateDirectory(PREFIXPATH);
            starter.Start();
        }

        private void StartUp()
        {
            while (Protocol == null)
            {
                Thread.Sleep(10);
            }

            LoadNonStickeredBase();
            FulfillBlackList();
            LoadDataBase();
            Task.Run((Action)ParsingCycle);
            Task.Run((Action)SaveDataBaseCycle);
            Task.Run((Action)SellFromQueue);
            Task.Run((Action)AddNewItems);
            //Task.Run((Action)UnstickeredRefresh);
            Task.Run((Action)SetNewOrder);
            if (!sellOnly)
            {
                Task.Run((Action)SetOrderForUnstickered);
            }
            Task.Run((Action)AddGraphData);
            Task.Run((Action)CheckName);
        }

        void CheckName()
        {
            while (true)
            {
                try
                {
                    JObject data = JObject.Parse(Utility.Request.Get(
                        "https://gist.githubusercontent.com/AndreySmirdin/b93a53b37dd1fa62976f28c7b54cae61/raw/set_true_if_want_to_sell_only.txt"));
                    sellOnly = Boolean.Parse((string) data[botName]["sell_only"]);
                    Consts.WANT_TO_BUY = (double) data[botName]["want_to_buy"];
                    Consts.MAXFROMMEDIAN = (double) data[botName]["max_from_median"];
                    Consts.UNSTICKERED_ORDER = (double) data[botName]["unstickered_order"];
                }
                catch (Exception ex)
                {
                    Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                }
                Thread.Sleep(Consts.MINORCYCLETIMEINTERVAL);
            }
        }

        private void AddGraphData() {
            Thread.Sleep(5000);
            string response = Utility.Request.Get("http://steamcommunity.com/inventory/76561198321472965/730/2?l=russian&count=5000");
            JObject parsed = JObject.Parse(response);
            JArray items = (JArray) parsed["descriptions"];
            float price = 0;
            foreach (var item in items) {
                try {
                    string name = (string) item["market_name"];
                    price += currentItems[name][0];
                } catch
                {

                }
            }
            price += Protocol.GetMoney();
            string line = DateTime.Now + ";" + (price / 100) + "\n";
            File.AppendAllText(MONEYTPATH, line);
        }

        private void SetOrderForUnstickered() {
            while (true) {
                Thread.Sleep(1000);
                if (needOrderUnstickered.Count > 0) {
                    var top = needOrderUnstickered.Peek();
                    var info = Protocol.MassInfo(
                        new List<Tuple<string, string>> {new Tuple<string, string>(top.i_classid, top.i_instanceid)},
                        buy: 2, history: 1);
                    if (info == null)
                        continue; //unlucky
                    if (info == null || (string) info["success"] == "false") {
                        needOrderUnstickered.Dequeue();
                        continue;
                    }

                    var res = info["results"][0];
                    JArray history = (JArray) res["history"]["history"];

                    double sum = 0;
                    int cnt = 0;
                    long time = long.Parse((string) history[0][0]);
                    for (int i = 0; i < history.Count && time - long.Parse((string) history[i][0]) < 10 * Consts.DAY; i++) {
                        sum += int.Parse((string) history[i][1]);
                        cnt++;
                    }

                    double price = sum / cnt;
                    if (cnt < 15) {
                        needOrderUnstickered.Dequeue();
                        continue;
                    }

                    Log.Info(res.ToString(Formatting.None));
                    int curPrice = 50;
                    try {
                        if (res["buy_offers"]?["best_offer"] != null) {
                            curPrice = int.Parse((string) res["buy_offers"]["best_offer"]);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                    }
                    
                    if (price > 9000 && curPrice < price * Consts.UNSTICKERED_ORDER && !blackList.Contains(top.i_market_hash_name)) {
                        Protocol.SetOrder(top.i_classid, top.i_instanceid, curPrice + 1);
                    }
                    needOrderUnstickered.Dequeue();
                }
            }
        }

        public void RefreshPrices(TMTrade[] trades) {
            lock (RefreshItemsLock) //lock (UnstickeredRefreshItemsLock)
            {
                for (int i = 1; i <= trades.Length; i++)
                {
                    var cur = trades[trades.Length - i];
                    //if (!hasStickers(cur.i_classid, cur.i_instanceid))
                    //{
                    //    unstickeredRefresh.Enqueue(cur);
                    //}
                    //else
                    if (i <= 7 && cur.ui_status == "1")
                    {
                        refreshPrice.Enqueue(cur);
                    }
                }
            }
        }

        void FulfillBlackList() {
            try {
                string[] lines = File.ReadAllLines(BLACKLISTPATH, Encoding.UTF8);
                foreach (var line in lines) {
                    blackList.Add(line);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
            }
        }

        void SetNewOrder() {
            while (true) {
                Thread.Sleep(1000);
                if (needOrder.Count != 0) {
                    HistoryItem item = needOrder.Dequeue();
                    try {
                        int price = Protocol.getBestOrder(item.i_classid, item.i_instanceid);
                        if (price != -1 && price < 30000)
                        {
                            lock (DatabaseLock)
                            {
                                if (dataBase[item.i_market_name].median * Consts.MAXFROMMEDIAN - price > 30)
                                {
                                    Protocol.SetOrder(item.i_classid, item.i_instanceid, ++price);
                                }
                            }
                        }
                    }
                    catch (Exception ex) {
                        Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                    }
                }
                
            }
        }

        void AddNewItems() {
            while (true) {
                Thread.Sleep(3000); //dont want to spin nonstop
                SpinWait.SpinUntil(() => (doNotSell || toBeSold.IsEmpty));
                if (doNotSell)
                {
                    doNotSell = false;
                    Thread.Sleep(1000 * 60 * 2); //can't lower it due to some weird things in protocol, requires testing
                }
                else
                {    
                    try
                    {
                        Inventory inventory = Protocol.GetSteamInventory();
                        foreach (Inventory.SteamItem item in inventory.content)
                        {
                            toBeSold.Enqueue(item);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                    }
                }
            }
        }

        int GetMySellPriceByName(string name)
        {
            lock (CurrentItemsLock)
            {
                if (currentItems.ContainsKey(name) && currentItems[name].Count > 2)
                    return currentItems[name][2] - 30;
            }
            return -1;
        }

        /// <summary>
        /// Returns price to sell for or -1
        /// </summary>
        /// <returns></returns>
        int GetMySellPrice(Inventory.SteamItem item)
        {
            if (ManipulatedItems.ContainsKey(item.i_classid + "_" + item.i_instanceid))
            {
                return ManipulatedItems[item.i_classid + "_" + item.i_instanceid];
            }
            else
            {
                return GetMySellPriceByName(item.i_market_name);
            }
        }

        void UnstickeredRefresh()
        {
            while (true)
            {
                Thread.Sleep(1000);
                Queue<TMTrade> unstickeredTemp;
                lock (UnstickeredRefreshItemsLock)
                {
                    unstickeredTemp = new Queue<TMTrade>(unstickeredRefresh);
                }
                while (unstickeredTemp.Count > 0)
                {
                    Queue<TMTrade> unstickeredChunk = new Queue<TMTrade>(unstickeredTemp.Take(100));
                    for (int i = 0; i < unstickeredChunk.Count; ++i)
                        unstickeredTemp.Dequeue();
                    List<Tuple<string, string>> tpls = new List<Tuple<string, string>>();
                    foreach (var x in unstickeredChunk)
                    {
                        tpls.Add(new Tuple<string, string>(x.i_classid, x.i_instanceid));
                    }
                    JObject info = Protocol.MassInfo(tpls, sell: 1, method: Protocol.ApiMethod.UnstickeredMassInfo);
                    List<Tuple<string, int>> items = new List<Tuple<string, int>>();
                    Dictionary<string, Tuple<int, int>[]> marketOffers = new Dictionary<string, Tuple<int, int>[]>();
                    Dictionary<string, int> myOffer = new Dictionary<string, int>();
                    foreach (JToken token in info["results"])
                    {
                        string cid = (string)token["classid"];
                        string iid = (string)token["instanceid"];
                        Tuple<int, int>[] arr = token["sell_offers"]["offers"].Select(x => new Tuple<int, int>((int)x[0], (int)x[1])).ToArray();
                        marketOffers[$"{cid}_{iid}"] = arr;
                        //think it cant be empty because we have at least one order placed.
                        try
                        {
                            myOffer[$"{cid}_{iid}"] = (int)token["sell_offers"]["my_offers"].Min();
                        }
                        catch
                        {
                            Log.ApiError($"TM refused to return me my order for {cid}_{iid}, using stickered price");
                            try
                            {
                                //int val = GetMySellPriceByName((string)token["name"]);
                            }
                            catch
                            {
                                Log.ApiError("No stickered price detected");
                            }
                        };
                    }
                    foreach (TMTrade trade in unstickeredChunk)
                    {
                        try
                        {
                            //think it cant be empty because we have at least one order placed.
                            if (marketOffers[$"{trade.i_classid}_{trade.i_instanceid}"][0].Item1 < myOffer[$"{trade.i_classid}_{trade.i_instanceid}"])
                            {
                                int coolPrice = marketOffers[$"{trade.i_classid}_{trade.i_instanceid}"][0].Item1 - 1;
                                if (coolPrice > GetMySellPriceByName(trade.i_market_name) * 0.8)
                                {
                                    items.Add(new Tuple<string, int>(trade.ui_id, coolPrice));
                                }
                            }
                            else
                            {
                                //TODO(noobgam): either don't update the price or change price to minprice - 1 if our price is currently lower, or don't change at all.
                            }
                        }
                        catch { }
                    }
                    /*JOBject obj = */Protocol.MassSetPriceById(items, method: Protocol.ApiMethod.UnstickeredMassSetPriceById);
                }
            }
        }

        void SellFromQueue() {
            while (true)
            {
                Thread.Sleep(1000); //dont want to spin nonstop
                SpinWait.SpinUntil(() => (!refreshPrice.IsEmpty || !toBeSold.IsEmpty));
                if (!refreshPrice.IsEmpty)
                {
                    lock (RefreshItemsLock)
                    {
                        List<Tuple<string, int>> items = new List<Tuple<string, int>>();
                        items = new List<Tuple<string, int>>();
                        while (refreshPrice.TryDequeue(out TMTrade trade))
                        {
                            items.Add(new Tuple<string, int>(trade.ui_id, 0));
                        }
                        /*JOBject obj = */Protocol.MassSetPriceById(items);
                    }
                }
                else if (toBeSold.TryDequeue(out Inventory.SteamItem item))
                {
                    int price = GetMySellPrice(item);
                    if (price != -1)
                    {
                        try
                        {
                            string[] ui_id = item.ui_id.Split('_');
                            if (!Protocol.SellNew(ui_id[1], ui_id[2], price))
                            {
                                Log.ApiError("Could not sell new item, enqueuing it again.");
                            } else
                            {
                                Log.Success($"New {item.i_market_name} is on sale for {price}");
                            }
                        }
                        catch
                        {
                        }
                    }                
                }
            }
        }

        void ParsingCycle() {
            while (true) {
                if (ParseNewDatabase()) {
                    Log.Success("Finished parsing new DB");
                }
                else {
                    Log.Error("Couldn\'t parse new DB");
                }
                Thread.Sleep(Consts.PARSEDATABASEINTERVAL);
            }
        }

        void SaveDataBaseCycle() {
            while (true) {
                SaveDataBase();
                Thread.Sleep(Consts.MINORCYCLETIMEINTERVAL);
            }
        }


        public void LoadDataBase() {
            lock (DatabaseLock)
            {
                if (!File.Exists(DATABASEPATH) && !File.Exists(DATABASETEMPPATH))
                    return;
                try
                {
                    dataBase = BinarySerialization.ReadFromBinaryFile<Dictionary<string, SalesHistory>>(DATABASEPATH);
                    if (File.Exists(DATABASETEMPPATH))
                        File.Delete(DATABASETEMPPATH);
                }
                catch (Exception ex)
                {
                    Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                    if (File.Exists(DATABASEPATH))
                        File.Delete(DATABASEPATH);
                    if (File.Exists(DATABASETEMPPATH))
                        File.Move(DATABASETEMPPATH, DATABASEPATH);
                    LoadDataBase();
                }
            }

            Log.Success("Loaded new DB. Total item count: " + dataBase.Count);
        }

        public void SaveDataBase() {
            lock (DatabaseLock) {
                if (File.Exists(DATABASEPATH))
                    File.Copy(DATABASEPATH, DATABASETEMPPATH, true);
                BinarySerialization.WriteToBinaryFile(DATABASEPATH, dataBase);
            }
        }

#if CAREFUL
        public void SaveJSONDataBase()
        {
            JsonSerialization.WriteToJsonFile(DATABASEJSONPATH, dataBase);
        }
#endif

        bool ParseNewDatabase() {
            try
            {
                try
                {
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
                    Dictionary<string, List<int>> currentItems = new Dictionary<string, List<int>>();

                    for (id = 1; id < lines.Length - 1; ++id)
                    {
                        string[] item = lines[id].Split(';');
                        if (item[NewItem.mapping["c_stickers"]] == "0")

                            unStickered.Add(item[NewItem.mapping["c_classid"]] + "_" +
                                            item[NewItem.mapping["c_instanceid"]]);
                        // new logic
                        else
                        {
                            String name = item[NewItem.mapping["c_market_name"]];
                            if (name.Length >= 2)
                            {
                                name = name.Remove(0, 1);
                                name = name.Remove(name.Length - 1);
                            }

                            if (!currentItems.ContainsKey(name))
                                currentItems[name] = new List<int>();
                            currentItems[name].Add(int.Parse(item[NewItem.mapping["c_price"]]));
                        }
                    }
                    lock (CurrentItemsLock)
                    {
                        this.currentItems = currentItems;
                        SaveNonStickeredBase(); 
                        SortCurrentItems();
                    }

                    // Calling WantToBuy function for all items. 
                    indexes = lines[0].Split(';');
                    id = 0;
                    for (id = 1; id < lines.Length - 1; ++id)
                    {
                        string[] itemInString = lines[id].Split(';');
                        NewItem newItem = new NewItem(itemInString);
                        if (WantToBuy(newItem))
                        {
                            Protocol.Buy(newItem);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }

                return true;
            }
            catch (Exception e) {
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
            }
            catch (Exception ex) {
                Log.Error(ex.Message);
            }
        }

        bool LoadNonStickeredBase() {
            try {
                string[] lines = File.ReadAllLines(UNSTICKEREDPATH);
                foreach (var line in lines)
                    unStickered.Add(line);
                return true;
            }
            catch (Exception e) {
                Log.Warn("Could not load unstickered DB, check whether DB name is correct (\'" + UNSTICKEREDPATH +
                         "\'):\n" + e.Message);
                return false;
            }
        }

        bool SaveNonStickeredBase() {
            try {
                if (File.Exists(UNSTICKEREDPATH))
                    File.Delete(UNSTICKEREDPATH);
                string[] lines = new string[unStickered.Count];
                int id = 0;
                foreach (var line in unStickered)
                    lines[id++] = line;
                File.WriteAllLines(UNSTICKEREDPATH, lines);
                return true;
            }
            catch (Exception e) {
                Log.Info(
                    "Could not save unstickered DB, check whether DB name is correct (\'emptystickered.txt\'). Maybe this file is write-protected?:\n" +
                    e.Message);
                return false;
            }
        }

        [Serializable]
        public class SalesHistory {
            public List<HistoryItem> sales = new List<HistoryItem>();
            public int median;
            public int cnt;

            public SalesHistory(HistoryItem item) {
                cnt = 1;
                sales.Add(item);
            }
        }


        [System.Obsolete("Specify item type instead of cid and iid")]
        bool hasStickers(string ClassId, string InstanceId) {
            return !unStickered.Contains(ClassId + '_' + InstanceId);
        }

        bool hasStickers(NewItem item) {
            return !unStickered.Contains(item.i_classid + "_" + item.i_instanceid);
        }

        bool hasStickers(HistoryItem item) {
            return !unStickered.Contains(item.i_classid + "_" + item.i_instanceid);
        }

        public void ProcessItem(HistoryItem item) {
            if (!hasStickers(item)) {
                needOrderUnstickered.Enqueue(item);
                return;
            }

            //Console.WriteLine(item.i_market_name);
            SalesHistory salesHistory;
            lock (DatabaseLock)
            {
                if (dataBase.ContainsKey(item.i_market_name))
                {
                    salesHistory = dataBase[item.i_market_name];
                    if (dataBase[item.i_market_name].cnt == MAXSIZE)
                        dataBase[item.i_market_name].sales.RemoveAt(0);
                    else
                        dataBase[item.i_market_name].cnt++;
                    dataBase[item.i_market_name].sales.Add(item);
                }
                else
                {
                    salesHistory = new SalesHistory(item);
                    dataBase.Add(item.i_market_name, salesHistory);
                }

                int[] a = new int[salesHistory.cnt];
                for (int i = 0; i < salesHistory.cnt; i++)
                    a[i] = salesHistory.sales[i].price;
                Array.Sort(a);
                dataBase[item.i_market_name].median = a[salesHistory.cnt / 2];

                if (salesHistory.cnt >= MINSIZE && !blackList.Contains(item.i_market_name))
                {
                    needOrder.Enqueue(item);
                }
            }
        }

        public bool WantToBuy(NewItem item) {
            if (!hasStickers(item)) {
                //we might want to manipulate it.
                string id = item.i_classid + "_" + item.i_instanceid;
                if (!ManipulatedItems.ContainsKey(id))
                    return false;
                return ManipulatedItems[id] < item.ui_price + 10;
            }

            lock (DatabaseLock) lock (CurrentItemsLock)
            {
                if (!dataBase.ContainsKey(item.i_market_name))
                {
                    return false;
                }

                if (!currentItems.ContainsKey(item.i_market_name))
                {
                    return false;
                }

                SalesHistory salesHistory = dataBase[item.i_market_name];
                HistoryItem oldest = salesHistory.sales[0];
                List<int> prices = currentItems[item.i_market_name];
                //if (item.ui_price < 40000 && salesHistory.cnt >= MINSIZE && item.ui_price < 0.8 * salesHistory.median && salesHistory.median - item.ui_price > 600 && !blackList.Contains(item.i_market_name))

                
                 //Logging 
                if (item.ui_price < 25000 && prices.Count >= 6 &&
                    item.ui_price < 0.9 * prices[2] && !blackList.Contains(item.i_market_name) &&
                    salesHistory.cnt >= MINSIZE &&
                    prices[2] < salesHistory.median * 1.25 && prices[2] - item.ui_price > 400)
                {
                    JObject log = new JObject();
                    log["item"] = JObject.Parse(JsonConvert.SerializeObject(item));
                    log["curPrice"] = prices[2];
                    
                    Log.Info("Have seen interesting item: " + log.ToString(Formatting.None));
                }

                
                if (item.ui_price < 25000 && prices.Count >= 6 &&
                    item.ui_price < Consts.WANT_TO_BUY * prices[2] && !blackList.Contains(item.i_market_name) &&
                    salesHistory.cnt >= MINSIZE &&
                    prices[2] < salesHistory.median * 1.2 && prices[2] - item.ui_price > 400)
                {
                    //TODO какое-то условие на время
                    Log.Info("Going to buy " + item.i_market_name + ". Expected profit " +
                             (salesHistory.median - item.ui_price));
                    return true;
                }
                
            }

            return false;
        }

        public bool doNotSell = false; // True when we don`t want to sell.  
        public bool sellOnly = false;
        public Protocol Protocol;

        private String botName;

        private const int MAXSIZE = 12000;
        private const int MINSIZE = 70;
        private const string PREFIXPATH = "CS";
        private SortedSet<string> unStickered = new SortedSet<string>();

        private const string UNSTICKEREDPATH = PREFIXPATH + "/emptystickered.txt";
        private const string DATABASEPATH = PREFIXPATH + "/database.txt";
        private const string DATABASETEMPPATH = PREFIXPATH + "/databaseTemp.txt";
        private const string DATABASEJSONPATH = PREFIXPATH + "/database.json";
        private const string BLACKLISTPATH = PREFIXPATH + "/blackList.txt";
        private const string MONEYTPATH = PREFIXPATH + "/money.txt";



        private ConcurrentQueue<Inventory.SteamItem> toBeSold = new ConcurrentQueue<Inventory.SteamItem>();
        private ConcurrentQueue<TMTrade> refreshPrice = new ConcurrentQueue<TMTrade>();
        private Queue<TMTrade> unstickeredRefresh = new Queue<TMTrade>();

        private Queue<HistoryItem> needOrder = new Queue<HistoryItem>();
        private Queue<HistoryItem> needOrderUnstickered = new Queue<HistoryItem>();
        private SortedSet<string> blackList = new SortedSet<string>();
        private Dictionary<string, SalesHistory> dataBase = new Dictionary<string, SalesHistory>();

        private Dictionary<string, List<int>> currentItems = new Dictionary<string, List<int>>();

        private Dictionary<String, int> ManipulatedItems = new Dictionary<string, int>(); // [cid_iid] -> price
    }
}

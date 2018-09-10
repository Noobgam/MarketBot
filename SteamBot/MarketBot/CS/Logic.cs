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
using SteamBot.MarketBot.CS;

namespace CSGOTM {
    public class Logic {
        public Utility.MarketLogger Log;
        private Mutex DatabaseLock = new Mutex();
        private Mutex CurrentItemsLock = new Mutex();
        private Mutex RefreshItemsLock = new Mutex();
        private Mutex UnstickeredRefreshItemsLock = new Mutex();
        private static double MAXFROMMEDIAN = 0.78;
        private static double WANT_TO_BUY = 0.8;
        private static double UNSTICKERED_ORDER = 0.78;
        NewBuyFormula newBuyFormula = null;
        SellMultiplier sellMultiplier = null;

        public Logic(String botName)
        {
            this.botName = botName;
            PREFIXPATH = "CS/" + botName;
            UNSTICKEREDPATH = PREFIXPATH + "/emptystickered.txt";
            DATABASEPATH = PREFIXPATH + "/database.txt";
            DATABASETEMPPATH = PREFIXPATH + "/databaseTemp.txt";
            DATABASEJSONPATH = PREFIXPATH + "/database.json";
            BLACKLISTPATH = PREFIXPATH + "/blackList.txt";
            MONEYTPATH = PREFIXPATH + "/money.txt";
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
            //Task.Run((Action)SaveDataBaseCycle);
            //Task.Run((Action)SellFromQueue);
            //Task.Run((Action)AddNewItems);
            //Task.Run((Action)UnstickeredRefresh);
            //Task.Run((Action)SetNewOrder);
            //if (!sellOnly)
            //{
            //    Task.Run((Action)SetOrderForUnstickered);
            //}
            //Task.Run((Action)AddGraphData);
            Task.Run((Action)RefreshConfig);
        }

        void RefreshConfig()
        {
            while (true)
            {
                try
                {
                    JObject data = JObject.Parse(Utility.Request.Get(
                        "https://gist.githubusercontent.com/Noobgam/819841a960112ae85fe8ac61b6bd33e1/raw/"));
                    if (!data.ContainsKey(botName))
                    {
                        Log.Error("Config contains no bot definition.");
                    }
                    else
                    {
                        JToken token = data[botName];
                        if (token["sell_only"].Type != JTokenType.Boolean)
                            Log.Error($"Sell only is not a boolean for {botName}");
                        else
                        {
                            if (sellOnly != (bool)token["sell_only"])
                            {
                                Log.Info("Sellonly was changed from {0} to {1}", sellOnly, (bool)token["sell_only"]);
                                sellOnly = (bool)token["sell_only"];
                            }
                        }

                        if (token["want_to_buy"].Type != JTokenType.Float)
                            Log.Error($"Want to buy is not a float for {botName}");
                        else
                        {
                            if (WANT_TO_BUY != (double)token["want_to_buy"])
                            {
                                Log.Info("Want to buy was changed from {0} to {1}", WANT_TO_BUY, (double)token["want_to_buy"]);
                                WANT_TO_BUY = (double)token["want_to_buy"];
                            }
                        }

                        if (token["max_from_median"].Type != JTokenType.Float)
                            Log.Error($"Max from median is not a float for {botName}");
                        else
                        {
                            if (MAXFROMMEDIAN != (double)token["max_from_median"])
                            {
                                Log.Info("Max from median was changed from {0} to {1}", WANT_TO_BUY, (double)token["max_from_median"]);
                                MAXFROMMEDIAN = (double)token["max_from_median"];
                            }
                        }

                        if (token["unstickered_order"].Type != JTokenType.Float)
                            Log.Error($"Unstickered order is not a float for {botName}");
                        else
                        {
                            if (UNSTICKERED_ORDER != (double)token["unstickered_order"])
                            {
                                Log.Info("Unsctickered order was changed from {0} to {1}", UNSTICKERED_ORDER, (double)token["unstickered_order"]);
                                UNSTICKERED_ORDER = (double)token["unstickered_order"];
                            }
                        }
                        if (token["experiments"] != null)
                        {
                            JToken new_buy_formula = token["experiments"]["new_buy_formula"];
                            if (new_buy_formula != null)
                            {
                                try
                                {
                                    DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                                    DateTime start = dtDateTime.AddSeconds((double)new_buy_formula["start"]).ToLocalTime();
                                    DateTime end = dtDateTime.AddSeconds((double)new_buy_formula["end"]).ToLocalTime();
                                    if (DateTime.Now < end)
                                    {
                                        double want_to_buy = (double)new_buy_formula["want_to_buy"];
                                        NewBuyFormula temp = new NewBuyFormula(start, end, want_to_buy);
                                        if (newBuyFormula != temp)
                                        {
                                            newBuyFormula = temp;
                                            Log.Info("New newBuyFormula applied:");
                                            Log.Info(new_buy_formula.ToString(Formatting.None));
                                        }
                                    }
                                }
                                catch
                                {                                    
                                    Log.Error("Incorrect experiment");
                                }
                            }
                            JToken sell_multiplier = token["experiments"]["sell_multiplier"];
                            if (sell_multiplier != null)
                            {
                                try
                                {
                                    DateTime dtDateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
                                    DateTime start = dtDateTime.AddSeconds((double)new_buy_formula["start"]).ToLocalTime();
                                    DateTime end = dtDateTime.AddSeconds((double)new_buy_formula["end"]).ToLocalTime();
                                    if (DateTime.Now < end)
                                    {
                                        double sellmultiplier = (double)sell_multiplier["multiplier"];
                                        SellMultiplier temp = new SellMultiplier(start, end, sellmultiplier);
                                        if (sellMultiplier != temp)
                                        {
                                            temp = sellMultiplier;
                                            Log.Info("New sellmultiplier applied:");
                                            Log.Info(sell_multiplier.ToString(Formatting.None));
                                        }
                                    }
                                }
                                catch
                                {
                                    Log.Error("Incorrect experiment");
                                }
                            }
                        }
                    }

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
            double price = 0;
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

                    int curPrice = 50;
                    try {
                        if (res["buy_offers"] != null && res["buy_offers"].Type != JTokenType.Boolean
                            && res["buy_offers"]["best_offer"] != null) {
                            curPrice = int.Parse((string) res["buy_offers"]["best_offer"]);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Info(res.ToString(Formatting.None));
                        Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                    }
                    
                    if (price > 9000 && curPrice < price * UNSTICKERED_ORDER && !blackList.Contains(top.i_market_hash_name)) {
                        Protocol.SetOrder(top.i_classid, top.i_instanceid, curPrice + 1);
                    }
                    needOrderUnstickered.Dequeue();
                }
            }
        }

        public void RefreshPrices(TMTrade[] trades) {
            lock (RefreshItemsLock) lock (UnstickeredRefreshItemsLock)
            {
                unstickeredRefresh.Clear();
                for (int i = 1; i <= trades.Length; i++)
                {
                    var cur = trades[trades.Length - i];
                    if (!hasStickers(cur.i_classid, cur.i_instanceid))
                    {
                        unstickeredRefresh.Enqueue(cur);
                    }
                    if (i <= 7 && cur.ui_status == "1")
                    {
                        if (GetMySellPriceByName(cur.i_market_name) != -1)
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
                                if (dataBase[item.i_market_name].median * MAXFROMMEDIAN - price > 30)
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
                {
                    int price = currentItems[name][2] - 30;
                    lock (DatabaseLock)
                    {
                        if (dataBase.ContainsKey(name) && price > 2 * dataBase[name].median)
                        {
                            price = 2 * dataBase[name].median;
                        }
                    }
                    return price;
                }
            }
            lock (DatabaseLock)
            {
                if (dataBase.ContainsKey(name))
                {
                    return dataBase[name].median;
                }
            }
            return -1;
        }

        /// <summary>
        /// Returns price to sell for or -1
        /// </summary>
        /// <returns></returns>
        int GetMySellPrice(Inventory.SteamItem item)
        {
            if (!hasStickers(item.i_classid, item.i_instanceid))
                return GetMyUnstickeredSellPrice(item);

            if (ManipulatedItems.ContainsKey(item.i_classid + "_" + item.i_instanceid))
            {
                return ManipulatedItems[item.i_classid + "_" + item.i_instanceid];
            }
            else
            {
                int temp = GetMySellPriceByName(item.i_market_name);
                if (temp != -1)
                    if (sellMultiplier != null && sellMultiplier.IsRunning())
                    {
                        temp = (int)(temp * sellMultiplier.Multiplier);
                    }
                return GetMySellPriceByName(item.i_market_name);
            }
        }


        /// <summary>
        /// Returns price to sell for or -1
        /// </summary>
        /// <returns></returns>
        int GetMyUnstickeredSellPrice(Inventory.SteamItem item)
        {
            if (ManipulatedItems.ContainsKey(item.i_classid + "_" + item.i_instanceid))
            {
                return ManipulatedItems[item.i_classid + "_" + item.i_instanceid];
            }
            else
            {
                lock (DatabaseLock)
                {
                    if (dataBase.ContainsKey(item.i_market_name))
                        return dataBase[item.i_market_name].median;
                    else
                        return -1;
                }
            }
        }

        void UnstickeredRefresh()
        {
            while (true)
            {
                Thread.Sleep(1000);
                try {
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
                        JObject info = Protocol.MassInfo(tpls, history:1, sell: 1, method: Protocol.ApiMethod.UnstickeredMassInfo);
                        if (info == null)
                            break;
                        List<Tuple<string, int>> items = new List<Tuple<string, int>>();
                        Dictionary<string, Tuple<int, int>[]> marketOffers = new Dictionary<string, Tuple<int, int>[]>();
                        Dictionary<string, int> myOffer = new Dictionary<string, int>();
                        foreach (JToken token in info["results"])
                        {
                            string cid = (string)token["classid"];
                            string iid = (string)token["instanceid"];
                            if (token["sell_offers"].Type == JTokenType.Null || token["sell_offers"].Type == JTokenType.Boolean)
                                continue;
                            Tuple<int, int>[] arr = new Tuple<int, int>[0];
                            try
                            {
                                arr = token["sell_offers"]["offers"].Select(x => new Tuple<int, int>((int)x[0], (int)x[1])).ToArray();
                            }
                            catch
                            {
                                throw;
                                //Log.Info(token.ToString(Formatting.Indented));
                            }
                            marketOffers[$"{cid}_{iid}"] = arr;
                            //think it cant be empty because we have at least one order placed.
                            try
                            {
                                myOffer[$"{cid}_{iid}"] = (int)token["sell_offers"]["my_offers"].Min();
                            }
                            catch
                            {
                                myOffer[$"{cid}_{iid}"] = arr[0].Item1 + 1;
                            };
                        }
                        foreach (TMTrade trade in unstickeredChunk)
                        {
                            try
                            {
                                //think it cant be empty because we have at least one order placed.
                                if (marketOffers[$"{trade.i_classid}_{trade.i_instanceid}"][0].Item1 <= myOffer[$"{trade.i_classid}_{trade.i_instanceid}"])
                                {
                                    int coolPrice = marketOffers[$"{trade.i_classid}_{trade.i_instanceid}"][0].Item1 - 1;
                                    int careful = (int)info["results"].Where(x => (string)x["classid"] == trade.i_classid && (string)x["instanceid"] == trade.i_instanceid).First()["history"]["average"];
                                    if (coolPrice < careful * 0.9)
                                        coolPrice = 0;
                                    items.Add(new Tuple<string, int>(trade.ui_id, coolPrice));
                                }
                                else
                                {
                                    int coolPrice = marketOffers[$"{trade.i_classid}_{trade.i_instanceid}"][1].Item1 - 1;
                                    int careful = (int)info["results"].Where(x => (string)x["classid"] == trade.i_classid && (string)x["instanceid"] == trade.i_instanceid).First()["history"]["average"];
                                    if (coolPrice < careful * 0.9)
                                        coolPrice = 0;
                                    items.Add(new Tuple<string, int>(trade.ui_id, coolPrice));
                                }
                            }
                            catch { }
                        }
                        /*JOBject obj = */
                        Protocol.MassSetPriceById(items, method: Protocol.ApiMethod.UnstickeredMassSetPriceById);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Some error happened. Message: {ex.Message} \nTrace: {ex.StackTrace}" );
                }
            }
        }

        void SellFromQueue() {
            while (true)
            {
                Thread.Sleep(1000); //dont want to spin nonstop
                while (refreshPrice.IsEmpty && toBeSold.IsEmpty)
                    Thread.Sleep(1000);
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
                        //if (newItem.i_market_name == "")
                        //{
                        //    Log.Info("Item has no name");
                        //}
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
                Log.Error("Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
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

            lock (DatabaseLock)
            {

                lock (CurrentItemsLock)
                {
                    if (!dataBase.ContainsKey(item.i_market_name))
                    {
                        return false;
                    }
                    SalesHistory salesHistory = dataBase[item.i_market_name];
                    if (newBuyFormula != null && newBuyFormula.IsRunning())
                    {
                        if (item.ui_price < 40000
                            && item.ui_price < newBuyFormula.WantToBuy * salesHistory.median
                            && salesHistory.median - item.ui_price > 1000
                            && salesHistory.cnt >= MINSIZE)
                        {
                            return true; //back to good ol' dayz
                        }
                    }

                    if (!currentItems.ContainsKey(item.i_market_name))
                    {
                        
                        return false;
                    }
                    HistoryItem oldest = salesHistory.sales[0];
                    List<int> prices = currentItems[item.i_market_name];
                    //if (item.ui_price < 40000 && salesHistory.cnt >= MINSIZE && item.ui_price < 0.8 * salesHistory.median && salesHistory.median - item.ui_price > 600 && !blackList.Contains(item.i_market_name))

                    //else
                    {
                        if (item.ui_price < 40000 
                            && prices.Count >= 6 
                            && item.ui_price < WANT_TO_BUY * prices[2] 
                            && !blackList.Contains(item.i_market_name) 
                            && salesHistory.cnt >= MINSIZE 
                            && prices[2] < salesHistory.median * 1.2 
                            && prices[2] - item.ui_price > 1000)
                        {
                            Log.Info("Going to buy " + item.i_market_name + ". Expected profit " +
                                     (salesHistory.median - item.ui_price));
                            return true;
                        }
                    }
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
        private string PREFIXPATH;
        private HashSet<string> unStickered = new HashSet<string>();

        private string UNSTICKEREDPATH;
        private string DATABASEPATH;
        private string DATABASETEMPPATH;
        private string DATABASEJSONPATH;
        private string BLACKLISTPATH;
        private string MONEYTPATH;

        private ConcurrentQueue<Inventory.SteamItem> toBeSold = new ConcurrentQueue<Inventory.SteamItem>();
        private ConcurrentQueue<TMTrade> refreshPrice = new ConcurrentQueue<TMTrade>();
        private Queue<TMTrade> unstickeredRefresh = new Queue<TMTrade>();

        private Queue<HistoryItem> needOrder = new Queue<HistoryItem>();
        private Queue<HistoryItem> needOrderUnstickered = new Queue<HistoryItem>();
        private HashSet<string> blackList = new HashSet<string>();
        private Dictionary<string, SalesHistory> dataBase = new Dictionary<string, SalesHistory>();

        private Dictionary<string, List<int>> currentItems = new Dictionary<string, List<int>>();

        private Dictionary<String, int> ManipulatedItems = new Dictionary<string, int>(); // [cid_iid] -> price
    }
}

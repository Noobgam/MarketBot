using System;
using System.Net;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Specialized;
using Newtonsoft.Json.Linq;
using System.Text;
using Newtonsoft.Json;


namespace CSGOTM {
    public class Logic {
        public Utility.MarketLogger Log;
        private static Mutex DatabaseLock = new Mutex();
        private static Mutex CurrentItemsLock = new Mutex();

        public Logic() {
            Thread starter = new Thread(new ThreadStart(StartUp));
            if (!Directory.Exists(PREFIXPATH))
                Directory.CreateDirectory(PREFIXPATH);
            starter.Start();
        }

        private void StartUp() {
            while (Protocol == null) {
                Thread.Sleep(10);
            }

            LoadNonStickeredBase();
            FulfillBlackList();
            LoadDataBase();
            Thread parser = new Thread(ParsingCycle);
            parser.Start();
            Thread saver = new Thread(SaveDataBaseCycle);
            saver.Start();
            Thread seller = new Thread(SellFromQueue);
            seller.Start();
            Thread adder = new Thread(AddNewItems);
            adder.Start();
            Thread setter = new Thread(SetNewOrder);
            setter.Start();
            Thread refresher = new Thread(RefreshPrices);
            refresher.Start();
            Thread orderForUnstickered = new Thread(SetOrderForUnstickered);
            orderForUnstickered.Start();
            Thread addGraphData = new Thread(AddGraphData);
            addGraphData.Start();
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
                if (needOrderUnstickered.Count > 0) {
                    var top = needOrderUnstickered.Peek();
                    var info = Protocol.MassInfo(
                        new List<Tuple<string, string>> {new Tuple<string, string>(top.i_classid, top.i_instanceid)},
                        buy: 2, history: 1);
                    Thread.Sleep(1000);
                    if (info == null || (string) info["success"] == "false") {
                        needOrderUnstickered.Dequeue();
                        continue;
                    }

                    var res = info["results"][0];
                    JArray history = (JArray) res["history"]["history"];

                    double sum = 0;
                    int cnt = 0;
                    long time = long.Parse((string) history[0][0]);
                    for (int i = 0; i < history.Count && time - long.Parse((string) history[i][0]) < 10 * DAY; i++) {
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
                        if (res["buy_offers"]?["best_offer"] != null) {
                            curPrice = int.Parse((string) res["buy_offers"]["best_offer"]);
                        }
                    }
                    catch (Exception ex) {
                    }

                    Log.Info("My Price for {0} is {1}, order is {2}", top.i_market_hash_name, price, curPrice);
                    if (price > 9000 && curPrice < price * 0.85) {
                        Protocol.SetOrder(top.i_classid, top.i_instanceid, curPrice + 1);
                    }
                    needOrderUnstickered.Dequeue();
                }

                Thread.Sleep(1000);
            }
        }

        private void RefreshPrices() {
            TMTrade[] trades = Protocol.GetTradeList();
            for (int i = 1; i <= 4 && i < trades.Length; i++) {
                var cur = trades[trades.Length - i];
                if (cur.ui_status == "1" && hasStickers(cur.i_classid, cur.i_instanceid)) {
                    refreshPrice.Enqueue(trades[trades.Length - i]);
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
            catch (Exception e) {
                Log.Warn("No blackList found.");
            }
        }

        void SetNewOrder() {
            while (true) {
                if (needOrder.Count != 0) {
                    HistoryItem item = needOrder.Dequeue();
                    try {
                        int price = Protocol.getBestOrder(item.i_classid, item.i_instanceid);
                        Thread.Sleep(APICOOLDOWN);
                        DatabaseLock.WaitOne();
                        SalesHistory history = dataBase[item.i_market_name];
                        Log.Info("Checking item..." + price + "  vs  " + history.median);
                        if (price != -1 && price < 30000 && history.median * 0.8 > price &&
                            history.median * 0.8 - price > 30) {
                            try {
                                Protocol.SetOrder(item.i_classid, item.i_instanceid, ++price);
                                Log.Success("Settled order for " + item.i_market_name);
                            }
                            catch (Exception ex) {
                            }
                        }

                        DatabaseLock.ReleaseMutex();
                    }
                    catch (Exception ex) {
                        Log.Error("Handled ex");
                    }
                }

                Thread.Sleep(APICOOLDOWN);
            }
        }

        void AddNewItems() {
            while (true) {
                if (doNotSell) {
                    doNotSell = false;
                    Thread.Sleep(MINORCYCLETIMEINTERVAL);
                }
                else if (toBeSold.Count == 0) {
                    try {
                        Inventory inventory = Protocol.GetSteamInventory();
                        foreach (Inventory.SteamItem item in inventory.content) {
                            Log.Info(item.i_market_name + " is going to be sold.");
                            toBeSold.Enqueue(item);
                        }
                    }
                    catch (Exception ex) {
                    }
                }

                Thread.Sleep(MINORCYCLETIMEINTERVAL);
            }
        }

        void SellFromQueue() {
            while (true) {
                if (refreshPrice.Count != 0) {
                    TMTrade item = refreshPrice.Dequeue();
                    try {
                        Protocol.Sell(item.ui_id, 0);
                    }
                    catch (Exception ex) {
                    }
                }

                else if (toBeSold.Count != 0) {
                    Inventory.SteamItem item = toBeSold.Dequeue();
                    if (ManipulatedItems.ContainsKey(item.i_classid + "_" + item.i_instanceid))
                    {
                        try
                        {
                            Protocol.SellNew(item.i_classid, item.i_instanceid,
                                ManipulatedItems[item.i_classid + "_" + item.i_instanceid]);
                        }
                        catch (Exception ex)
                        {
                            toBeSold.Enqueue(item);
                        }
                    }
                    else
                    {
                        CurrentItemsLock.WaitOne();
                        try
                        {
                            string[] ui_id = item.ui_id.Split('_');
                            Protocol.SellNew(ui_id[1], ui_id[2],
                                (int)currentItems[item.i_market_name][2] - 30);
                        }
                        catch (Exception ex)
                        {
                            toBeSold.Enqueue(item);
                        }
                        CurrentItemsLock.ReleaseMutex();
                    }
                }

                Thread.Sleep(APICOOLDOWN);
            }
        }

        void ParsingCycle() {
            while (true) {
                if (ParseNewDatabase()) {
                    Log.Debug("Finished parsing new DB");
                }
                else {
                    Log.Error("Couldn\'t parse new DB");
                }

                Thread.Sleep(MINORCYCLETIMEINTERVAL);
            }
        }

        void SaveDataBaseCycle() {
            while (true) {
                SaveDataBase();
                Thread.Sleep(MINORCYCLETIMEINTERVAL);
            }
        }


        public void LoadDataBase() {
            if (File.Exists(DATABASETEMPPATH)) {
                if (File.Exists(DATABASEPATH)) {
                    File.Delete(DATABASEPATH);
                }

                File.Move(DATABASETEMPPATH, DATABASEPATH);
            }
            else if (!File.Exists(DATABASEPATH)) {
                Log.Success("No database found, creating empty DB.");
                return;
            }

            dataBase = BinarySerialization.ReadFromBinaryFile<Dictionary<string, SalesHistory>>(DATABASEPATH);
            Log.Success("Loaded new DB. Total item count: " + dataBase.Count);
        }


        public void SaveDataBase() {
            if (File.Exists(DATABASEPATH))
                File.Copy(DATABASEPATH, DATABASETEMPPATH);
            DatabaseLock.WaitOne();
            BinarySerialization.WriteToBinaryFile(DATABASEPATH, dataBase);
            DatabaseLock.ReleaseMutex();
            if (File.Exists(DATABASETEMPPATH))
                File.Delete(DATABASETEMPPATH);
        }

#if DEBUG
        public void SaveJSONDataBase()
        {
            JsonSerialization.WriteToJsonFile(DATABASEJSONPATH, dataBase);
        }
#endif

        bool ParseNewDatabase() {
            try {
                using (WebClient myWebClient = new WebClient()) {
                    myWebClient.Encoding = System.Text.Encoding.UTF8;
                    NameValueCollection myQueryStringCollection = new NameValueCollection();
                    myQueryStringCollection.Add("q", "");
                    myWebClient.QueryString = myQueryStringCollection;
                    try {
                        string[] lines;
                        {
                            JObject things =
                                JObject.Parse(
                                    myWebClient.DownloadString("https://market.csgo.com/itemdb/current_730.json"));
                            string db = (string) things["db"];
                            lines = myWebClient.DownloadString("https://market.csgo.com/itemdb/" + db).Split('\n');
                        }
                        string[] indexes = lines[0].Split(';');
                        int id = 0;

                        if (NewItem.mapping.Count == 0)
                            foreach (var str in indexes)
                                NewItem.mapping[str] = id++;

                        CurrentItemsLock.WaitOne();
                        currentItems.Clear();

                        for (id = 1; id < lines.Length - 1; ++id) {
                            string[] item = lines[id].Split(';');
                            if (item[NewItem.mapping["c_stickers"]] == "0")

                                unStickered.Add(item[NewItem.mapping["c_classid"]] + "_" +
                                                item[NewItem.mapping["c_instanceid"]]);
                            // new logic
                            else {
                                String name = item[NewItem.mapping["c_market_name"]];
                                if (name.Length >= 2) {
                                    name = name.Remove(0, 1);
                                    name = name.Remove(name.Length - 1);
                                }
                                
                                if (!currentItems.ContainsKey(name))
                                    currentItems[name] = new List<long>();
                                currentItems[name].Add(Int64.Parse(item[NewItem.mapping["c_price"]]));
                            }
                        }

                        SaveNonStickeredBase();
                        SortCurrentItems();
                        CurrentItemsLock.ReleaseMutex();

                        // Calling WantToBuy function for all items. 
                        indexes = lines[0].Split(';');
                        id = 0;
                        for (id = 1; id < lines.Length - 1; ++id) {
                            string[] itemInString = lines[id].Split(';');
                            NewItem newItem = new NewItem(itemInString);
                            if (WantToBuy(newItem)) {
                                Protocol.Buy(newItem);
                                Thread.Sleep(APICOOLDOWN);
                            }
                        }
                    }
                    catch (Exception ex) {
                        Console.WriteLine(ex.Message);
                    }
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

#if DEBUG
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
            DatabaseLock.WaitOne();
            if (dataBase.ContainsKey(item.i_market_name)) {
                salesHistory = dataBase[item.i_market_name];
                if (dataBase[item.i_market_name].cnt == MAXSIZE)
                    dataBase[item.i_market_name].sales.RemoveAt(0);
                else
                    dataBase[item.i_market_name].cnt++;
                dataBase[item.i_market_name].sales.Add(item);
            }
            else {
                salesHistory = new SalesHistory(item);
                dataBase.Add(item.i_market_name, salesHistory);
            }

            int[] a = new int[salesHistory.cnt];
            for (int i = 0; i < salesHistory.cnt; i++)
                a[i] = salesHistory.sales[i].price;
            Array.Sort(a);
            dataBase[item.i_market_name].median = a[salesHistory.cnt / 2];

            if (salesHistory.cnt >= MINSIZE && !blackList.Contains(item.i_market_name)) {
                needOrder.Enqueue(item);
            }

            DatabaseLock.ReleaseMutex();
        }

        public bool WantToBuy(NewItem item) {
            if (!hasStickers(item)) {
                //we might want to manipulate it.
                string id = item.i_classid + "_" + item.i_instanceid;
                if (!ManipulatedItems.ContainsKey(id))
                    return false;
                return ManipulatedItems[id] < item.ui_price + 10;
            }

            if (!currentItems.ContainsKey(item.i_market_name)) {
                return false;
            }

            DatabaseLock.WaitOne();
            if (!dataBase.ContainsKey(item.i_market_name)) {
                DatabaseLock.ReleaseMutex();
                return false;
            }

            SalesHistory salesHistory = dataBase[item.i_market_name];
            HistoryItem oldest = (HistoryItem) salesHistory.sales[0];
            List<long> prices = currentItems[item.i_market_name];
            //if (item.ui_price < 40000 && salesHistory.cnt >= MINSIZE && item.ui_price < 0.8 * salesHistory.median && salesHistory.median - item.ui_price > 600 && !blackList.Contains(item.i_market_name))

            if (item.ui_price < 25000 && prices.Count >= 8 &&
                item.ui_price < 0.85 * prices[2] && !blackList.Contains(item.i_market_name) &&
                salesHistory.cnt >= MINSIZE &&
                prices[2] < dataBase[item.i_market_name].median * 1.25 && prices[2] - item.ui_price > 400) {
                //TODO какое-то условие на время
                Log.Info("Going to buy " + item.i_market_name + ". Expected profit " +
                         (salesHistory.median - item.ui_price));
                DatabaseLock.ReleaseMutex();
                return true;
            }

            DatabaseLock.ReleaseMutex();
            return false;
        }

        public bool doNotSell = false; // True when we don`t want to sell.  
        public Protocol Protocol;

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

        private const int MINORCYCLETIMEINTERVAL = 1000 * 60 * 10; // 10 minutes
        private const int APICOOLDOWN = 1000 * 3; // 3 seconds
        private const int DAY = 86400;

        private Queue<Inventory.SteamItem> toBeSold = new Queue<Inventory.SteamItem>();
        private Queue<TMTrade> refreshPrice = new Queue<TMTrade>();

        private Queue<HistoryItem> needOrder = new Queue<HistoryItem>();
        private Queue<HistoryItem> needOrderUnstickered = new Queue<HistoryItem>();
        private SortedSet<string> blackList = new SortedSet<string>();
        private Dictionary<string, SalesHistory> dataBase = new Dictionary<string, SalesHistory>();

        private Dictionary<string, List<long>> currentItems = new Dictionary<string, List<long>>();

        private Dictionary<String, int> ManipulatedItems = new Dictionary<string, int>(); // [cid_iid] -> price
    }
}
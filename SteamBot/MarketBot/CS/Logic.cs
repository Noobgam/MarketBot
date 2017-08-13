﻿using System;
using System.Net;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Collections.Specialized;
using Newtonsoft.Json.Linq;

namespace CSGOTM
{

    public class Logic
    {
        public Utility.MarketLogger Log;
        public Logic()
        {
            Thread starter = new Thread(new ThreadStart(StartUp));
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
            Thread parser = new Thread(new ThreadStart(ParsingCycle));
            parser.Start();
            Thread saver = new Thread(new ThreadStart(SaveDataBaseCycle));
            saver.Start();
            Thread seller = new Thread(new ThreadStart(SellFromQueue));
            seller.Start();
            Thread adder = new Thread(new ThreadStart(AddNewItems));
            adder.Start();
            Thread setter = new Thread(new ThreadStart(SetNewOrder));
            setter.Start();

        }

        void FulfillBlackList()
        {
            try
            {
                string[] lines = File.ReadAllLines(BLACKLISTPATH);
                foreach (var line in lines)
                {
                    blackList.Add(line);
                }
            }
            catch (Exception e)
            {
                Log.Warn("No blackList found.");
            }
        }

        void SetNewOrder()
        {
            while (true)
            {
                if (needOrder.Count != 0)
                {
                    HistoryItem item = needOrder.Dequeue();
                    try
                    {
                        int price = Protocol.getBestOrder(item.i_classid, item.i_instanceid);
                        Thread.Sleep(APICOOLDOWN);

                        SalesHistory history = dataBase[item.i_market_name];
                        Log.Info("Checking item..." + price + "  vs  " + history.median);
                        if (price < 30000 && history.median * 0.8 > price && history.median * 0.8 - price > 30)
                        {
                            try
                            {
                                Protocol.SetOrder(item.i_classid, item.i_instanceid, ++price);
                                Log.Success("Settled order for " + item.i_market_name);
                            }
                            catch (Exception ex)
                            {

                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Handled ex");
                    }

                }
                Thread.Sleep(APICOOLDOWN);
            }
        }

        void AddNewItems()
        {
            while (true)
            {
                if (doNotSell)
                {
                    doNotSell = false;
                    Thread.Sleep(MINORCYCLETIMEINTERVAL);
                }
                else if (toBeSold.Count == 0)
                {
                    try
                    {
                        Inventory inventory = Protocol.GetSteamInventory();
                        foreach (Inventory.SteamItem item in inventory.content)
                        {
                            Log.Info(item.i_market_name + " is going to be sold.");
                            toBeSold.Enqueue(item);
                        }
                    }
                    catch (Exception ex)
                    {

                    }
                }
                Thread.Sleep(MINORCYCLETIMEINTERVAL);
            }
        }

        void SellFromQueue()
        {
            while (true)
            {
                if (toBeSold.Count != 0)
                {
                    Inventory.SteamItem item = toBeSold.Dequeue();
                    if (dataBase.ContainsKey(item.i_market_name))
                    {
                        try
                        {
                            Protocol.Sell(item.i_classid, item.i_instanceid, dataBase[item.i_market_name].median);
                        }
                        catch (Exception ex)
                        {

                        }
                    }
                }
                Thread.Sleep(APICOOLDOWN);
            }
        }

        void ParsingCycle()
        {
            while (true)
            {
                if (ParseNewDatabase())
                {
                    Log.Debug("Finished parsing new DB");
                }
                else
                {
                    Log.Error("Couldn\'t parse new DB");
                }
                Thread.Sleep(MINORCYCLETIMEINTERVAL);
            }
        }

        void SaveDataBaseCycle()
        {
            while (true)
            {
                SaveDataBase();
                Thread.Sleep(MINORCYCLETIMEINTERVAL);
            }
        }


        public void LoadDataBase()
        {
            if (File.Exists(DATABASETEMPPATH))
            {
                if (File.Exists(DATABASEPATH))
                {
                    File.Delete(DATABASEPATH);
                }
                File.Move(DATABASETEMPPATH, DATABASEPATH);
            }
            else if (!File.Exists(DATABASEPATH))
            {
                Log.Success("No database found, creating empty DB.");
                return;
            }

            dataBase = BinarySerialization.ReadFromBinaryFile<Dictionary<string, SalesHistory>>(DATABASEPATH);
            Log.Success("Loaded new DB. Total item count: " + dataBase.Count);
        }


        public void SaveDataBase()
        {
            if (File.Exists(DATABASEPATH))
            {
                File.Copy(DATABASEPATH, DATABASETEMPPATH);
            }
            BinarySerialization.WriteToBinaryFile(DATABASEPATH, dataBase);
            if (File.Exists(DATABASETEMPPATH))
            {
                File.Delete(DATABASETEMPPATH);
            }
        }

#if DEBUG
        public void SaveJSONDataBase()
        {
            JsonSerialization.WriteToJsonFile<Dictionary<string, SalesHistory>>(DATABASEJSONPATH, dataBase);
        }
#endif
        bool ParseNewDatabase()
        {
            try
            {
                using (WebClient myWebClient = new WebClient())
                {
                    myWebClient.Encoding = System.Text.Encoding.UTF8;
                    NameValueCollection myQueryStringCollection = new NameValueCollection();
                    myQueryStringCollection.Add("q", "");
                    myWebClient.QueryString = myQueryStringCollection;
                    Dictionary<string, int> mapping = new Dictionary<string, int>();
                    try
                    {
                        string[] lines;
                        {
                            JObject things = JObject.Parse(myWebClient.DownloadString("https://market.csgo.com/itemdb/current_730.json"));
                            string db = (string)things["db"];
                            string database = myWebClient.DownloadString("https://market.csgo.com/itemdb/" + db);
                            lines = database.Split('\n');
                        }
                        string[] indexes = lines[0].Split(';');
                        int id = 0;
                        foreach (var str in indexes)
                            mapping[str] = id++;
                        for (id = 1; id < lines.Length - 1; ++id)
                        {
                            string[] item = lines[id].Split(';');
                            if (item[mapping["c_stickers"]] == "0")
                                unStickered.Add(item[mapping["c_classid"]] + "_" + item[mapping["c_instanceid"]]);
                        }
                        SaveNonStickeredBase();
                    }
                    catch (Exception ex)
                    {

                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Log.Error(e.Message);
                return false;
            }
        }

        bool LoadNonStickeredBase()
        {
            try
            {
                string[] lines = File.ReadAllLines(UNSTICKEREDPATH);
                foreach (var line in lines)
                    unStickered.Add(line);
                return true;
            }
            catch (Exception e)
            {
                Log.Warn("Could not load unstickered DB, check whether DB name is correct (\'" + UNSTICKEREDPATH + "\'):\n" + e.Message);
                return false;
            }
        }

        bool SaveNonStickeredBase()
        {
            try
            {
                if (File.Exists(UNSTICKEREDPATH))
                    File.Delete(UNSTICKEREDPATH);
                string[] lines = new string[unStickered.Count];
                int id = 0;
                foreach (var line in unStickered)
                    lines[id++] = line;
                File.WriteAllLines(UNSTICKEREDPATH, lines);
                return true;
            }
            catch (Exception e)
            {
                Log.Info("Could not save unstickered DB, check whether DB name is correct (\'emptystickered.txt\'). Maybe this file is write-protected?:\n" + e.Message);
                return false;
            }
        }

        [Serializable]
        public class SalesHistory
        {
            public List<HistoryItem> sales = new List<HistoryItem>();
            public int median;
            public int cnt = 0;

            public SalesHistory(HistoryItem item)
            {
                cnt = 1;
                sales.Add(item);
            }
        }


        [System.Obsolete("Specify item type instead of cid and iid")]
        bool hasStickers(string ClassId, string InstanceId)
        {
            return !unStickered.Contains(ClassId + '_' + InstanceId);
        }

        bool hasStickers(NewItem item)
        {
            return !unStickered.Contains(item.i_classid + "_" + item.i_instanceid);
        }

        bool hasStickers(HistoryItem item)
        {
            return !unStickered.Contains(item.i_classid + "_" + item.i_instanceid);
        }

        public void ProcessItem(HistoryItem item)
        {
            if (!hasStickers(item))
                return;
            //Console.WriteLine(item.i_market_name);
            SalesHistory salesHistory;
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

        public bool WantToBuy(NewItem item)
        {
            if (!hasStickers(item))
                return false;
            if (!dataBase.ContainsKey(item.i_market_name))
                return false;
            SalesHistory salesHistory = dataBase[item.i_market_name];
            HistoryItem oldest = (HistoryItem)salesHistory.sales[0];
            if (item.ui_price < 40000 && salesHistory.cnt >= MINSIZE && item.ui_price < 0.8 * salesHistory.median && salesHistory.median - item.ui_price > 600 && !blackList.Contains(item.i_market_name))
            {//TODO какое-то условие на время
                Log.Info("Going to buy " + item.i_market_name + ". Expected profit " + (salesHistory.median - item.ui_price));
                return true;
            }
            return false;
        }

        public bool doNotSell = false; // True when we don`t want to sell.  
        public Protocol Protocol;

        private const int MAXSIZE = 12000;
        private const int MINSIZE = 40;
        private SortedSet<string> unStickered = new SortedSet<string>();

        private const string UNSTICKEREDPATH = "emptystickered.txt";
        private const string DATABASEPATH = "database.txt";
        private const string DATABASETEMPPATH = "databaseTemp.txt";
        private const string DATABASEJSONPATH = "database.json";
        private const string BLACKLISTPATH = "blackList.txt";

        private const int MINORCYCLETIMEINTERVAL = 1000 * 60 * 10; // 10 minutes
        private const int APICOOLDOWN = 1000 * 3; // 3 seconds

        private Queue<Inventory.SteamItem> toBeSold = new Queue<Inventory.SteamItem>();
        private Queue<HistoryItem> needOrder = new Queue<HistoryItem>();
        private SortedSet<string> blackList = new SortedSet<string>();
        private Dictionary<string, SalesHistory> dataBase = new Dictionary<string, SalesHistory>();

    }
}
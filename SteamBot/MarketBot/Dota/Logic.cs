using System;
using System.Threading.Tasks;
using System.Web;
using System.Net;
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
using System.Collections;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace NDota2Market
{

    public class Logic
    {
        public Utility.MarketLogger Log;
        public Logic()
        {
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

            LoadDataBase();
            Thread saver = new Thread(new ThreadStart(SaveDataBaseCycle));
            saver.Start();
            Thread seller = new Thread(new ThreadStart(SellFromQueue));
            seller.Start();
            //Thread adder = new Thread(new ThreadStart(AddNewItems));
            //adder.Start();
            Thread setter = new Thread(new ThreadStart(SetNewOrder));
            setter.Start();

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
                            Log.Info(item.i_classid + "_" + item.i_instanceid);
                            Log.Success(item.i_market_name + " is going to be sold.");
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
                        catch(Exception ex)
                        {

                        }
                    }
                }
                Thread.Sleep(APICOOLDOWN);
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
            if (!File.Exists(DATABASEPATH))
            {
                Log.Warn("No database found, creating empty DB.");
            }
            else { 
                dataBase = BinarySerialization.ReadFromBinaryFile<Dictionary<string, SalesHistory>>(DATABASEPATH);
                Log.Success("Loaded new DB. Total item count: " + dataBase.Count);
            }
        }

        public void SaveDataBase()
        {
            if (File.Exists(DATABASEPATH))
                File.Copy(DATABASEPATH, DATABASETEMPPATH);
            BinarySerialization.WriteToBinaryFile(DATABASEPATH, dataBase);
            if (File.Exists(DATABASETEMPPATH))
                File.Delete(DATABASETEMPPATH);
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

        public void ProcessItem(HistoryItem item)
        {
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

            if (salesHistory.cnt >= MINSIZE)
            {
                needOrder.Enqueue(item);
            }
        }

        public bool WantToBuy(NewItem item)
        {
            if (!dataBase.ContainsKey(item.i_market_name))
                return false;
            SalesHistory salesHistory = dataBase[item.i_market_name];
            HistoryItem oldest = (HistoryItem)salesHistory.sales[0];
            if (item.ui_price < 40000 && salesHistory.cnt >= MINSIZE && item.ui_price < 0.8 * salesHistory.median && salesHistory.median - item.ui_price > 600)
            {//TODO какое-то условие на время
                Log.Info("Going to buy " + item.i_market_name + ". Expected profit " +  (salesHistory.median - item.ui_price));
                return true;
            }
            return false;
        }

        public bool doNotSell = false; // True when we don`t want to sell.  
        public Dota2Market Protocol;

        private const int APICOOLDOWN = 3000;
        private const int MINORCYCLETIMEINTERVAL = 50000;
        private const int MAXSIZE = 500;
        private const int MINSIZE = 60;
        private const string PREFIXPATH = "DOTA";
        private const string DATABASEPATH = PREFIXPATH + "/database.txt";
        private const string DATABASETEMPPATH = PREFIXPATH + "/databaseTemp.txt";
        private Queue<Inventory.SteamItem> toBeSold = new Queue<Inventory.SteamItem>();
        private Queue<HistoryItem> needOrder = new Queue<HistoryItem>();
        private Dictionary<string, SalesHistory> dataBase = new Dictionary<string, SalesHistory>();
        
    }
}
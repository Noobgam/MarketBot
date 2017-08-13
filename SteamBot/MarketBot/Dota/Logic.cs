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
            LoadDataBase();
            Thread starter = new Thread(new ThreadStart(StartUp));
            starter.Start();           
        }

        private void StartUp()
        {
            while (Protocol == null)
            {
                Thread.Sleep(10);
            }

            Thread saver = new Thread(new ThreadStart(SaveDataBaseCycle));
            saver.Start();
            Thread seller = new Thread(new ThreadStart(SellFromQueue));
            seller.Start();
            Thread adder = new Thread(new ThreadStart(AddNewItems));
            adder.Start();
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
                        Thread.Sleep(3000);

                        SalesHistory history = dataBase[item.i_market_name];
                        Console.WriteLine("Checking item..." + price + "  vs  " + history.median);
                        if (price < 30000 && history.median * 0.8 > price && history.median * 0.8 - price > 30)
                        {
                            try
                            {
                                Protocol.SetOrder(item.i_classid, item.i_instanceid, ++price);
                                Console.WriteLine("Settled order for " + item.i_market_name);
                            }
                            catch (Exception ex)
                            {

                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Handled ex");
                    }
                    
                }
                Thread.Sleep(3000);
            }
        }

        void AddNewItems()
        {
            while (true)
            {
                if (doNotSell)
                {
                    doNotSell = false;
                    Thread.Sleep(500000);
                }
                else if (toBeSold.Count == 0)
                {
                    try
                    {
                        Inventory inventory = Protocol.GetSteamInventory();
                        foreach (Inventory.SteamItem item in inventory.content)
                        {
                            Console.WriteLine(item.i_market_name + " is going to be sold.");
                            toBeSold.Enqueue(item);
                        }
                    }
                    catch (Exception ex)
                    {

                    }

                }
                Thread.Sleep(500000);
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
                Thread.Sleep(2000);
            }
        }

        void SaveDataBaseCycle()
        {
            while (true)
            {
                if (SaveDataBase())
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Saved new DB");
                    Console.WriteLine(dataBase.Count);
                    Console.ForegroundColor = ConsoleColor.White;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Couldn\'t save DB");
                    Console.ForegroundColor = ConsoleColor.White;
                }
                Thread.Sleep(6000);
            }
        }

        public bool ReadFile(string path)
        {
            if (!File.Exists(path))
                return false;
            dataBase = BinarySerialization.ReadFromBinaryFile<Dictionary<string,SalesHistory>> (path);
            return true;
        }

        public void LoadDataBase()
        {
            if (!File.Exists(DATABASEPATH))
            {
                Console.WriteLine("No database found, creating empty DB.");
            }
            else { 
                dataBase = BinarySerialization.ReadFromBinaryFile<Dictionary<string, SalesHistory>>(DATABASEPATH);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Loaded new DB. Total item count: " + dataBase.Count);
                Console.ForegroundColor = ConsoleColor.White;
            }
        }

        public bool SaveDataBase()
        {
            try
            {
                if (File.Exists(DATABASEPATH))
                {
                    if (File.Exists(DATABASETEMPPATH))
                        File.Delete(DATABASETEMPPATH);
                    File.Copy(DATABASEPATH, DATABASETEMPPATH);
                }
                BinarySerialization.WriteToBinaryFile(DATABASEPATH, dataBase);
                File.Delete(DATABASETEMPPATH);
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not save DB, check whether DB name is correct (\'database.txt\'). Maybe this file is write-protected?:");
                Console.WriteLine(e.Message);
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
            dataBase[item.i_market_name].median = a[(int)(salesHistory.cnt * 0.5)];

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
                Console.WriteLine("Going to buy " + item.i_market_name + ". Expected profit " +  (salesHistory.median - item.ui_price));
                return true;
            }
            return false;
        }

        public bool doNotSell = false; // True when we don`t want to sell.  
        public Dota2Market Protocol;

        private const int MAXSIZE = 500;
        private const int MINSIZE = 80;
        private const string MARKETPREFIX = "dota_";
        private const string DATABASEPATH = MARKETPREFIX + "database.txt";
        private const string DATABASETEMPPATH = MARKETPREFIX + "databaseTemp.txt";
        private Queue<Inventory.SteamItem> toBeSold = new Queue<Inventory.SteamItem>();
        private Queue<HistoryItem> needOrder = new Queue<HistoryItem>();
        private Dictionary<string, SalesHistory> dataBase = new Dictionary<string, SalesHistory>();
        
    }
}
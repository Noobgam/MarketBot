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

namespace CSGOTM
{


    public class Logic
    {
        public const int MAXSIZE = 100;
        public CSGOTMProtocol Protocol;
        private SortedSet<string> unStickered = new SortedSet<string>();
        private const string UNSTICKEREDPATH = "emptystickered.txt";
        private const string DATABASEPATH = "database.txt";
        public Queue<Inventory.SteamItem> toBeSold = new Queue<Inventory.SteamItem>();

        public Logic()
        {

        }
        public Logic(CSGOTMProtocol Pr1)
        {
            Protocol = Pr1;
            if (LoadNonStickeredBase())
                if (SaveNonStickeredBase())
                {
                    Thread parser = new Thread(new ThreadStart(ParsingCycle));
                    parser.Start();
                }
            LoadDataBase();
            Thread saver = new Thread(new ThreadStart(SaveDataBaseCycle));
            saver.Start();
            Thread seller = new Thread(new ThreadStart(SellFromQueue));
            seller.Start();
            Thread adder = new Thread(new ThreadStart(AddNewItems));
            adder.Start();
        }

        void AddNewItems()
        {
            while (true)
            {
                if (toBeSold.Count == 0)
                {
                    Inventory inventory = Protocol.GetSteamInventory();
                    foreach (Inventory.SteamItem item in inventory.content)
                    {
                        Console.WriteLine(item.i_market_name + " is going to be sold.");
                        toBeSold.Enqueue(item);
                    }
                }
                Thread.Sleep(60000);
            }
        }
        
        void SellFromQueue()
        {
            while (true)
            {
                if (toBeSold.Count != 0)
                {
                    Inventory.SteamItem item = toBeSold.Peek();
                    if (dataBase.ContainsKey(item.i_market_name))
                    {
                        //Protocol.Sell(item.i_classid, item.i_instanceid, dataBase[item.i_market_name].median);
                        toBeSold.Dequeue();
                    }
                }
                Thread.Sleep(1000);
            }
        }
       
        void ParsingCycle()
        {
            while (true)
            {
                if (ParseNewDatabase())
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    //Console.WriteLine("Finished parsing new DB");
                    Console.ForegroundColor = ConsoleColor.White;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    //Console.WriteLine("Couldn\'t parse new DB");
                    Console.ForegroundColor = ConsoleColor.White;
                }
                Thread.Sleep(60000);
            }
        }

        void SaveDataBaseCycle()
        {
            while (true)
            {
                if (SaveDataBase())
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    //Console.WriteLine("Saved new DB");
                    Console.ForegroundColor = ConsoleColor.White;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    //Console.WriteLine("Couldn\'t save DB");
                    Console.ForegroundColor = ConsoleColor.White;
                }
                Thread.Sleep(60000);
            }
        }

        public bool LoadDataBase()
        {
            try
            {
                string[] lines = File.ReadAllLines(DATABASEPATH);
                foreach (var line in lines)
                {
                    string[] words = line.Split(';');
                    SalesHistory salesHistory = (SalesHistory) JsonConvert.DeserializeObject<SalesHistory>(words[1]);
                    if (salesHistory.cnt >= 13)
                        Console.WriteLine(words[0]);
                    dataBase.Add(words[0], salesHistory);
                }
                Console.WriteLine("Loaded " + lines.Length + " items.");
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not load DB, check whether DB name is correct (\'" + DATABASEPATH + "\'):");
                Console.WriteLine(e.Message);
                return false;
            }
        }

        public bool SaveDataBase()
        {
            try
            {
                if (File.Exists(DATABASEPATH))
                    File.Delete(DATABASEPATH);
                string[] lines = new string[dataBase.Count];
                int id = 0;
                foreach (KeyValuePair<string, SalesHistory> kvp in dataBase)
                {
                    string line = kvp.Key + ";" + JsonConvert.SerializeObject(kvp.Value);
                    lines[id++] = line;
                }
                File.WriteAllLines(DATABASEPATH, lines);
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine("Could not save DB, check whether DB name is correct (\'database.txt\'). Maybe this file is write-protected?:");
                Console.WriteLine(e.Message);
                return false;
            }
        }

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
                    string[] lines;
                    {
                        JObject things = JObject.Parse(myWebClient.DownloadString("https://csgo.tm/itemdb/current_730.json"));
                        string db = (string)things["db"];
                        string database = myWebClient.DownloadString("https://csgo.tm/itemdb/" + db);
                        lines = database.Split('\n');
                    }
                    string[] indexes = lines[0].Split(';');
                    int id = 0;
                    foreach (var str in indexes)
                        mapping[str] = id++;
                    for(id = 1; id < lines.Length - 1; ++id)
                    {
                        string[] item = lines[id].Split(';');
                        if (item[mapping["c_stickers"]] == "0")
                            unStickered.Add(item[mapping["c_classid"]] + "_" + item[mapping["c_instanceid"]]);
                    }
                    SaveNonStickeredBase();
                }
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
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
                Console.WriteLine("Could not load unstickered DB, check whether DB name is correct (\'" + UNSTICKEREDPATH + "\'):");
                Console.WriteLine(e.Message);
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
                Console.WriteLine("Could not save unstickered DB, check whether DB name is correct (\'emptystickered.txt\'). Maybe this file is write-protected?:");
                Console.WriteLine(e.Message);
                return false;
            }
        }

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

        public Dictionary<string, SalesHistory> dataBase = new Dictionary<string, SalesHistory>();

        bool hasStickers(string ClassId, string InstanceId)
        {
            return !unStickered.Contains(ClassId + '_' + InstanceId);
        }

        public void ProcessItem(HistoryItem item)
        {
            if (!hasStickers(item.i_classid, item.i_instanceid))
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
        }

        public bool WantToBuy(NewItem item)
        {
            if (!hasStickers(item.i_classid, item.i_instanceid))
                return false;
            if (!dataBase.ContainsKey(item.i_market_name))
                return false;
            SalesHistory salesHistory = dataBase[item.i_market_name];
            HistoryItem oldest = (HistoryItem)salesHistory.sales[0];
            if (item.ui_price < 10000 && salesHistory.cnt >= 13 && item.ui_price < 0.82 * salesHistory.median && salesHistory.median - item.ui_price > 500)
            {//TODO какое-то условие на время
                Console.Write(salesHistory.median - item.ui_price + " ");
                return true;
            }
            return false;
        }
    }
}
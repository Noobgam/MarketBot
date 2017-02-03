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

namespace CSGOTM
{


    public class Logic
    {
        public int MAXSIZE = 50;
        public CSGOTMProtocol Protocol;
        int tmp = 0;
        public Logic()
        {

        }
        public Logic(CSGOTMProtocol Pr1)
        {
            Protocol = Pr1;
        }
        public void Hello()
        {
            Console.WriteLine(++tmp);
        }


        public class SalesHistory
        {
            public ArrayList sales = new ArrayList();
            public int median;
            public int cnt = 0;

            public SalesHistory(HistoryItem item)
            {
                cnt = 1;
                sales.Add(item);
            }
        }

        public Dictionary<string, SalesHistory> dataBase = new Dictionary<string, SalesHistory>();

        public void ProcessItem(HistoryItem item)
        {
            if (dataBase.ContainsKey(item.i_market_name))
            {
                SalesHistory salesHistory = dataBase[item.i_market_name];
                if (dataBase[item.i_market_name].cnt == MAXSIZE)
                    dataBase[item.i_market_name].sales.RemoveAt(0);
                else
                    dataBase[item.i_market_name].cnt++;
                dataBase[item.i_market_name].sales.Add(item);
            }
            else
            {
                SalesHistory salesHistory = new SalesHistory(item);
                salesHistory.sales.Add(item);
                dataBase.Add(item.i_market_name, salesHistory);
            }

            //find new median
        }

        public bool WantToBuy(NewItem item)
        {
            if (!dataBase.ContainsKey(item.i_market_name))
                return false;
            SalesHistory salesHistory = dataBase[item.i_market_name];
            HistoryItem oldest = (HistoryItem) salesHistory.sales[0];
            if (salesHistory.cnt == MAXSIZE && item.ui_price < 0.85 * salesHistory.median && salesHistory.median - item.ui_price > 400) //TODO какое-то условие на время
                return true;
            return false;
        }

        public void LoadDataBase()
        {
            //TODO
        }

        public void SaveDataBase()
        {
            //TODO
        }
    }
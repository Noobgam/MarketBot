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

namespace CSGOTM
{
    public class CSGOTMProtocol
    {
        public SortedSet<string> Codes;
        string Api = "";
        public CSGOTMProtocol()
        {

        }
        bool died = false;
        WebSocket socket = new WebSocket("wss://wsn.dota2.net/wsn/");
        public CSGOTMProtocol(SortedSet<string> temp, string api = "6AL09F5z8m98GPwSPN0ew2P7saRr8uI")
        {
            //Open connection.
            Codes = temp;
            Api = api;
            socket.Opened += Open;
            socket.Closed += Error;
            socket.MessageReceived += Msg;
            socket.Open();
        }

        #region JsonParsers
        public class Pair<T, U>
        {
            public Pair() { }

            public Pair(T first, U second)
            {
                this.First = first;
                this.Second = second;
            }

            public T First { get; set; }
            public U Second { get; set; }
        };
        public class NewItem
        {
            public string i_quality;
            public string i_name_color;
            public string i_classid;
            public string i_instanceid;
            public string i_market_hash_name;
            public string i_market_name;
            public double ui_price;
            public string app;
        }
        public class Message
        {
            public string type;
            public string data;
        }
        public class Trade_Result
        {
            public string result;
            public string id;
        }
        public class HistoryItem
        {
            public string i_classid;
            public string i_instanceid;
            public string name;
            public double price;
            public double date; //date is just hour and minute atm.
        }
        #endregion JsonParsers

        public readonly string[] search = { "<div class=\\\"price\\\"", "<div class=\\\"name\\\"" };

        public class Dummy
        {
            public string s;
        }

        string parse(string s)
        {
            var sb = new StringBuilder();
            sb.Append("{\"s\":\"" + s + "\"}");
            string t = sb.ToString();
            var dummy = JsonConvert.DeserializeObject<Dummy>(t);
            return dummy.s;
        }

        public double Timing()
        {
            DateTime Begin = new DateTime(2016, 1, 9);
            DateTime cur = DateTime.Now;
            TimeSpan elapsed = cur.Subtract(Begin);
            return elapsed.TotalMinutes;
        }

        public HistoryItem History(string s)
        {
            HistoryItem a = new HistoryItem();
            for (int i = 0; i < s.Length; i++)
            {
                if (s.Substring(i, 7) == "/item\\/")
                {
                    int j = i + 7;
                    int q = j;
                    while (s[j] != '-')
                        j++;
                    a.i_classid = s.Substring(q, j - q);
                    ++j;
                    q = j;
                    while (s[j] != '-')
                        j++;
                    a.i_instanceid = s.Substring(q, j - q);
                    break;
                }
            }
            int cnt = 0;

            for (int i = 0; i < s.Length; i++)
            {
                bool yeah = true;
                for (int j = 0; j < search[cnt].Length; j++)
                {
                    if (s[i + j] != search[cnt][j])
                    {
                        yeah = false;
                        break;
                    }
                }
                if (yeah)
                {
                    i = i + search[cnt].Length;
                    while (s[i] != '>') i++;
                    i += 5; // 5 characters from here. > \ r \ n
                    while (s[i] == ' ')
                        i++;
                    int j = 0;
                    while (s[i + j] != '<' && (!(s[i + j] == '\\' && s[i + j + 1] == 'r')) && (!(s[i + j] == '\\' && s[i + j + 1] == 'n')))
                        j++;
                    switch (cnt)
                    {
                        case 0:
                            a.price = double.Parse(s.Substring(i, j).Replace('.', ','));
                            break;
                        case 1:
                            a.name = parse(s.Substring(i, j));
                            break;
                    }
                    if (cnt == 1)
                        break;
                    cnt++;
                }
            }
            a.date = Timing();

            return a;
        }

        bool close = false;
        class auth
        {
            public string wsAuth;
            public string success;
        }

        void Msg(object sender, MessageReceivedEventArgs e)
        {
            if (e.Message == "pong")
                return;
            var message = e.Message;

            Message x = JsonConvert.DeserializeObject<Message>(message);
            switch (x.type)
            {
                case "newitem":
                    NewItem item = JsonConvert.DeserializeObject<NewItem>(x.data);
                    break;
                case "history_go":
                    break;
                case "newitems_go":
                    break;
                default:
                    Console.WriteLine(x.type);
                    Console.WriteLine(x.data);
                    break;
            }
        }

        void pinger()
        {
            while (!close)
            {
                socket.Send("ping");
                Thread.Sleep(30000);
            }
        }

        void Subscribe()
        {
            socket.Send("newitems_go");
            socket.Send("history_go");
        }

        void Auth()
        {
            using (WebClient myWebClient = new WebClient())
            {
                NameValueCollection myQueryStringCollection = new NameValueCollection();
                myQueryStringCollection.Add("q", "");
                myWebClient.QueryString = myQueryStringCollection;
                string a = myWebClient.DownloadString("https://csgo.tm/api/GetWSAuth/?key=" + Api);
                auth q = JsonConvert.DeserializeObject<auth>(a);
                socket.Send(q.wsAuth);
                Subscribe();               
            }
        }

        void Open(object sender, EventArgs e)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Connection opened!");
            Console.ForegroundColor = ConsoleColor.White;
            Thread ping = new Thread(new ThreadStart(pinger));
            Auth();
            ping.Start();
        }

        void Error(object sender, EventArgs e)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error");
            Console.ForegroundColor = ConsoleColor.White;
        }
    }
}
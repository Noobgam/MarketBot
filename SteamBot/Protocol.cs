﻿using System;
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
        public SteamBot.Bot Parent;
        string Api = "";
        public CSGOTMProtocol()
        {

        }
        bool died = true;
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
        public CSGOTMProtocol(SteamBot.Bot p, SortedSet<string> temp, string api = "6AL09F5z8m98GPwSPN0ew2P7saRr8uI")
        {
            Parent = p;
            //Open connection.
            Codes = temp;
            Api = api;
            socket.Opened += Open;
            socket.Closed += Error;
            socket.MessageReceived += Msg;
            socket.Open();
        }
        
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
            died = false;
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
            died = true;
            ReOpen();
            Console.ForegroundColor = ConsoleColor.White;
        }

        void ReOpen()
        {
            for (int i = 0; !died && i < 10; ++i)
            {
                socket = new WebSocket("wss://wsn.dota2.net/wsn/");
                socket.Opened += Open;
                socket.Closed += Error;
                socket.MessageReceived += Msg;
                socket.Open();
                Thread.Sleep(5000);
                Console.WriteLine("Trying to reconnect for the %d-th time", i + 1);
            }
        }
    }
}
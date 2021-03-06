﻿using CSGOTM;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Utility;
using Utility.MongoApi;
using Utility.VK;
using WebSocketSharp;
using static CSGOTM.Perfomance;

namespace SteamBot.MarketBot.CS {
    static class Balancer {
        private static Log Log = new Log("balancer.log");
        public static event EventHandler<NewItem> NewItemAppeared = delegate { };
        private static WebSocket socket;
        private static bool opening = false;
        private static bool died = true;
        public readonly static string BALANCER = Path.Combine("CS", "balancer");
        public readonly static string UNSTICKEREDPATH = Path.Combine(BALANCER, "emptystickered.txt");
        private static readonly RPSKeeper newItemRpsKeeper = new RPSKeeper();

        private static HashSet<Tuple<long, long>> unstickeredCache = new HashSet<Tuple<long, long>>();
        private static MongoHistoryCSGO mongoHistoryCSGO = new MongoHistoryCSGO();

        public static double GetNewItemsRPS() {
            return newItemRpsKeeper.GetRps();
        }

        public static void Init() {
            AllocSocket();
            OpenSocket();
        }

        private static void AllocSocket() {
            if (socket != null) {
                socket = null;
            }
            socket = new WebSocket(Consts.Endpoints.TMSocket);
        }

        private static void OpenSocket() {
            opening = true;
            socket.OnOpen += Open;
            socket.OnError += Error;
            socket.OnClose += Close;
            socket.OnMessage += Msg;
            socket.Connect();
        }

        static void SocketPinger() {
            while (!died) {
                try {
                    socket.Send("ping");
                } catch (Exception ex) {
                    Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                }
                Tasking.WaitForFalseOrTimeout(() => !died, 30000).Wait();
            }
        }

        static void Open(object sender, EventArgs e) {
            died = false;
            opening = false;
            Log.Success("Connection opened!");
            Tasking.Run((Action)SocketPinger, "Balancer");
            socket.Send("newitems_go");
            socket.Send("history_go");
            //start = DateTime.Now;
        }

        static void Error(object sender, EventArgs e) {
            Log.Error($"Connection error: " + e.ToString());
        }

        static void Close(object sender, EventArgs e) {
            Log.Error($"Connection closed: " + e.ToString());
            if (!died) {
                died = true;
                socket = null;
            }
        }

        static void ReOpener() {
            int i = 1;
            while (true) {
                Thread.Sleep(10000);
                if (died) {
                    if (!opening) {
                        try {
                            Log.ApiError($"Trying to reconnect for the {i++}-th time");
                            VK.Alert("АХТУНГ, БАЛАНСЕР УПАЛ");
                            AllocSocket();
                            OpenSocket();
                        } catch (Exception ex) {
                            Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                        }
                    } else {
                        AllocSocket();
                        OpenSocket();
                    }
                }
            }
        }

        static private void ProcessItem(NewHistoryItem item) {
            mongoHistoryCSGO.Add(item);
        }
        
        static void Msg(object sender, MessageEventArgs e) {
            try {
                //Log.Info($"Message: {e.Data}");
                #region ParseType
                if (e.Data == "pong")
                    return;
                string type = string.Empty;
                string data = string.Empty;
                JsonTextReader reader = new JsonTextReader(new StringReader(e.Data));
                string currentProperty = string.Empty;
                while (reader.Read()) {
                    if (reader.Value != null) {
                        if (reader.TokenType == JsonToken.PropertyName)
                            currentProperty = reader.Value.ToString();
                        else if (reader.TokenType == JsonToken.String) {
                            if (currentProperty == "type")
                                type = reader.Value.ToString();
                            else
                                data = reader.Value.ToString();
                        }
                    }
                }
                #endregion
                switch (type) {
                    case "newitems_go":
                        NewItem newItem = new NewItem(data);
                        newItemRpsKeeper.Tick();
                        if (newItem.i_market_name == "") {
                            Log.Warn("Socket item has no market name");
                        } else {
                            NewItemAppeared(null, newItem);
                        }
                        break;
                    case "history_go":
                        try {
                            NewHistoryItem historyItem = new NewHistoryItem(data);
                            ProcessItem(historyItem);
                        } catch (Exception ex) {
                            Log.Error($"Some error occured during history parse. [{data}] Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
                        }
                        break;
                    default:
                        //Log.Info(JObject.Parse(e.Message).ToString(Formatting.Indented));
                        //Console.WriteLine(x.type);
                        //data = DecodeEncodedNonAsciiCharacters(data);
                        //Log.Info(data);
                        break;
                }
            } catch (Exception ex) {
                Log.Error("Some error occured. Message: " + ex.Message + "\nTrace: " + ex.StackTrace);
            } finally {
            }
        }
    }
}

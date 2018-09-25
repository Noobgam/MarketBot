﻿using CSGOTM;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using SteamBot.MarketBot.Utility.VK;
using Utility;
using System.Collections.Concurrent;

namespace MarketBot.Server {
    public class Core : IDisposable {
        private HttpListener server;
        private HashSet<BotConfig> botSet = new HashSet<BotConfig>();
        private Dictionary<string, DateTime> LastPing = new Dictionary<string, DateTime>();

        public Core() {
            server = new HttpListener();
            CurSizes = new Dictionary<string, int>();
            server.Prefixes.Add(Consts.Endpoints.prefix);
            Console.Error.WriteLine("Starting!");
            VK.Init();
            CoreConfig coreConfig = JsonConvert.DeserializeObject<CoreConfig>(Utility.Request.Get(Consts.Endpoints.ServerConfig));
            foreach (BotConfig bot in coreConfig.botList)
                botSet.Add(bot);
            server.Start();            
            Console.Error.WriteLine("Started!");
            JObject temp = null;
            Task.Run((Action)Listen);
            Task.Run((Action)BackgroundCheck);
#if DEBUG
            try {
                temp = LocalRequest.GetBestToken("grim2");
                Console.WriteLine(temp.ToString());
            } catch {
                Console.Error.WriteLine("Could not get a response from local server");
            }
#endif
        }

        private void BackgroundCheck() {
            Tasking.WaitForFalseOrTimeout(() => !disposed, 30000).Wait(); //30 sec should be enough to init all bots
            while (!disposed) {
                foreach (BotConfig bot in botSet) {
                    if (LastPing.TryGetValue(bot.Name, out DateTime dt)) {
                        DateTime temp = DateTime.Now;
                        if (temp.Subtract(dt).TotalSeconds > 60) {
                            VK.Alert($"Бот {bot.Name} давно не пинговал, видимо, он умер.");
                        }
                    } else {
                        VK.Alert($"Бот {bot.Name} не пингуется, хотя прописан в конфиге.");
                    }
                }
                Tasking.WaitForFalseOrTimeout(() => !disposed, 60000).Wait();
            }
        }

        private void Listen() {
            while (!disposed) {
                ThreadPool.QueueUserWorkItem(Process, server.GetContext());
            }
        }

        private Dictionary<string, int> CurSizes;
        private Dictionary<string, ConcurrentQueue<Pair<DateTime, int>>> salesHistorySizes = new Dictionary<string, ConcurrentQueue<Pair<DateTime, int>>>();


        private void Process(object o) {
            var context = o as HttpListenerContext;
            JObject resp = null;
            try {
                string Endpoint = context.Request.Url.AbsolutePath;
                if (Endpoint == Consts.Endpoints.GetBestToken) {
                    KeyValuePair<string, int> kv = CurSizes.OrderBy(t => t.Value)
                            .FirstOrDefault();
                    if (kv.Key == null) {
                        throw new Exception("Don't know about bot inventory sizes");
                    }

                    JToken extrainfo = new JObject();
                    foreach (var kvp in CurSizes) {
                        extrainfo[kvp.Key] = new JObject {
                            ["inventorysize"] = kvp.Value
                        };
                    }
                    if (context.Request.Url.Query.Contains("dbhit=1")) {
                        foreach (var kvp in salesHistorySizes) {
                            while (kvp.Value.TryPeek(out var result)) {
                                if (DateTime.Now.Subtract(result.First).TotalHours <= 1) {
                                    break;
                                }
                                if (!kvp.Value.TryDequeue(out result)) {
                                    break;
                                }
                            }
                            if (!kvp.Value.IsEmpty) {
                                extrainfo[kvp.Key]["dbhit"] = kvp.Value.Where(x => x.Second >= Consts.MINSIZE).Count() / (double)kvp.Value.Count;
                            }
                        }
                    }
                    resp = new JObject {
                        ["success"] = true,
                        ["token"] = Consts.TokenCache[kv.Key],
                        ["botname"] = kv.Key,
                        ["extrainfo"] = extrainfo
                    };
                } else if (Endpoint == Consts.Endpoints.PutCurrentInventory) {
                    string[] usernames = context.Request.Headers.GetValues("botname");
                    if (usernames.Length != 1) {
                        throw new Exception($"You have to provide 1 username, {usernames.Length} were provided");
                    }
                    string[] data = context.Request.Headers.GetValues("data");
                    if (data.Length != 1) {
                        throw new Exception($"You have to provide 1 data, {data.Length} were provided");
                    }
                    CurSizes[usernames[0]] = int.Parse(data[0]);
                } else if (Endpoint == Consts.Endpoints.PingPong) {
                    string[] usernames = context.Request.Headers.GetValues("botname");
                    if (usernames.Length != 1) {
                        throw new Exception($"You have to provide 1 username, {usernames.Length} were provided");
                    }
                    LastPing[usernames[0]] = DateTime.Now;
                } else if (Endpoint == Consts.Endpoints.SalesHistorySize) {
                    string[] usernames = context.Request.Headers.GetValues("botname");
                    if (usernames.Length != 1) {
                        throw new Exception($"You have to provide 1 username, {usernames.Length} were provided");
                    }
                    string[] data = context.Request.Headers.GetValues("data");
                    if (data.Length != 1) {
                        throw new Exception($"You have to provide 1 data, {data.Length} were provided");
                    }
                    if (!salesHistorySizes.ContainsKey(usernames[0])) {
                        salesHistorySizes[usernames[0]] = new ConcurrentQueue<Pair<DateTime, int>>();
                    }
                    salesHistorySizes[usernames[0]].Enqueue(new Pair<DateTime, int>(DateTime.Now, int.Parse(data[0])));
                }
            } catch (Exception ex) {
                resp = new JObject {
                    ["success"] = false,
                    ["error"] = ex.Message
                };

            } finally {
                if (resp == null)
                    resp = new JObject {
                        ["success"] = false,
                        ["error"] = "Unsupported request"
                    };
                Respond(context, resp);
            }
        }

        // process request and make response

        private void Respond(HttpListenerContext ctx, JObject json) {
            //TODO(noobgam): prettify if User-Agent is defined
            string resp = json.ToString(
                ctx.Request.Headers.GetValues("User-Agent") == null
                ? Newtonsoft.Json.Formatting.None
                : Newtonsoft.Json.Formatting.Indented);
            byte[] buffer = Encoding.UTF8.GetBytes(resp);
            HttpListenerResponse response = ctx.Response;

            response.ContentLength64 = buffer.Length;
            Stream output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);

            output.Close();
        }

        public void Stop() {
            Dispose(true);
        }

        #region IDisposable Support
        private bool disposed = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing) {
            if (!disposed) {
                if (disposing) {
                    if (server.IsListening)
                        server.Stop();
                }

                disposed = true;
            }
        }

        public void Dispose() {
            Dispose(true);
        }
        #endregion
    }
}

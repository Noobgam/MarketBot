using CSGOTM;
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
using System.Collections.Specialized;
using System.Web;

namespace MarketBot.Server {
    public class Core : IDisposable {
        private HttpListener server;
        private CoreConfig coreConfig;
        private Dictionary<string, DateTime> LastPing = new Dictionary<string, DateTime>();

        public Core() {
            server = new HttpListener();
            CurSizes = new Dictionary<string, int>();
            CurInventory = new Dictionary<string, double>();
            CurTradable = new Dictionary<string, double>();
            CurMoney = new Dictionary<string, int>();
            server.Prefixes.Add(Consts.Endpoints.prefix);
            Console.Error.WriteLine("Starting!");
            VK.Init();
            coreConfig = JsonConvert.DeserializeObject<CoreConfig>(Utility.Request.Get(Consts.Endpoints.ServerConfig));
            server.Start();            
            Console.Error.WriteLine("Started!");
            JObject temp = null;
            Task.Run((Action)Listen);
            Task.Run((Action)BackgroundCheck);
            //Task.Run((Action)DBHitProvider);
        }

        private void DBHitProvider() {
            //Doesn't work now.
            while (true) {
                Thread.Sleep(2000);
                try {
                    var temp = LocalRequest.RawGet(Consts.Endpoints.GetBestToken + "?dbhit=70&extradb=1", "ffedor98");
                    double dbhit = (double)temp["extrainfo"]["ffedor98"]["dbhit"];
                    int dbcnt = (int)temp["extrainfo"]["ffedor98"]["dbcnt"];
                    if (dbcnt < 15000)
                        continue;
                    VK.Alert(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss") + " - " + dbhit.ToString(), VK.AlertLevel.Garbage);
                } catch {
                    //Console.Error.WriteLine("Could not get a response from local server");
                    continue;
                }
                Thread.Sleep(180000);
            }
        }

        private void BackgroundCheck() {
            Thread.Sleep(15000);
            while (!disposed) {
                foreach (BotConfig bot in coreConfig.botList) {
                    if (LastPing.TryGetValue(bot.Name, out DateTime dt)) {
                        DateTime temp = DateTime.Now;
                        if (temp.Subtract(dt).TotalSeconds > 60) {
                            VK.Alert($"Бот {bot.Name} давно не пинговал, видимо, он умер.");
                        }
                    } else {
                        //VK.Alert($"Бот {bot.Name} не пингуется, хотя прописан в конфиге.");
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
        private Dictionary<string, int> CurMoney;
        private Dictionary<string, double> CurInventory;
        private Dictionary<string, double> CurTradable;
        private Dictionary<string, ConcurrentQueue<Pair<DateTime, int>>> salesHistorySizes = new Dictionary<string, ConcurrentQueue<Pair<DateTime, int>>>();


        private void Process(object o) {
            var context = o as HttpListenerContext;
            JObject resp = null;
            try {
                string Endpoint = context.Request.Url.AbsolutePath;
                if (Endpoint == Consts.Endpoints.GetBestToken) {
                    var Forced = coreConfig.botList.Where(x => x.Force);
                    if (Forced.Any()) {
                        BotConfig forcedBot = Forced.First();
                        resp = new JObject {
                            ["success"] = true,
                            ["token"] = Consts.TokenCache[forcedBot.Name],
                            ["botname"] = forcedBot.Name,
                        };
                    } else {
                        var Filtered = CurSizes.Where(t => t.Value < Consts.CRITICALTHRESHHOLD);
                        if (!Filtered.Any()) {
                            throw new Exception("All bots are overflowing!");
                        }
                        KeyValuePair<string, int> kv = Filtered.OrderBy(
                            t => t.Value / coreConfig.botList.First(x => x.Name == t.Key).Weight
                            ).FirstOrDefault();
                        if (kv.Key == null) {
                            throw new Exception("Don't know about bot inventory sizes");
                        }

                        JToken extrainfo = new JObject();
                        foreach (var kvp in CurSizes) {
                            extrainfo[kvp.Key] = new JObject {
                                ["inventorysize"] = kvp.Value
                            };
                        }
                        NameValueCollection qscoll = HttpUtility.ParseQueryString(context.Request.Url.Query);
                        bool extradb = qscoll.AllKeys.Contains("extradb");
                        foreach (var key in qscoll.AllKeys) {
                            if (key == "dbhit") {
                                int value = int.Parse(qscoll[key]);
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
                                        extrainfo[kvp.Key]["dbhit"] = kvp.Value.Where(x => x.Second >= value).Count() / (double)kvp.Value.Count;
                                        if (extradb) {
                                            extrainfo[kvp.Key]["dbcnt"] = kvp.Value.Count;
                                        }
                                    }
                                }
                            }
                        }
                        resp = new JObject {
                            ["success"] = true,
                            ["token"] = Consts.TokenCache[kv.Key],
                            ["botname"] = kv.Key,
                            ["extrainfo"] = extrainfo
                        };
                    }
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
                } else if (Endpoint == Consts.Endpoints.PutInventoryCost) {
                    string[] usernames = context.Request.Headers.GetValues("botname");
                    if (usernames.Length != 1) {
                        throw new Exception($"You have to provide 1 username, {usernames.Length} were provided");
                    }
                    string[] data = context.Request.Headers.GetValues("data");
                    if (data.Length != 1) {
                        throw new Exception($"You have to provide 1 data, {data.Length} were provided");
                    }
                    CurInventory[usernames[0]] = double.Parse(data[0]);
                } else if (Endpoint == Consts.Endpoints.PutTradableCost) {
                    string[] usernames = context.Request.Headers.GetValues("botname");
                    if (usernames.Length != 1) {
                        throw new Exception($"You have to provide 1 username, {usernames.Length} were provided");
                    }
                    string[] data = context.Request.Headers.GetValues("data");
                    if (data.Length != 1) {
                        throw new Exception($"You have to provide 1 data, {data.Length} were provided");
                    }
                    CurTradable[usernames[0]] = double.Parse(data[0]);
                } else if (Endpoint == Consts.Endpoints.PutMoney) {
                    string[] usernames = context.Request.Headers.GetValues("botname");
                    if (usernames.Length != 1) {
                        throw new Exception($"You have to provide 1 username, {usernames.Length} were provided");
                    }
                    string[] data = context.Request.Headers.GetValues("data");
                    if (data.Length != 1) {
                        throw new Exception($"You have to provide 1 data, {data.Length} were provided");
                    }
                    CurMoney[usernames[0]] = int.Parse(data[0]);
                } else if (Endpoint == Consts.Endpoints.Status) {
                    bool full = context.Request.QueryString["full"] == null ? false : bool.Parse(context.Request.QueryString["full"]);
                    JToken extrainfo = new JObject();
                    double moneySum = 0;
                    foreach (var kvp in CurSizes) {
                        if (extrainfo[kvp.Key] == null)
                            extrainfo[kvp.Key] = new JObject();
                        extrainfo[kvp.Key]["inventorysize"] = kvp.Value;
                    }
                    foreach (var kvp in CurMoney) {
                        double myMoney = (double)kvp.Value / 100;
                        if (full) {
                            if (extrainfo[kvp.Key] == null)
                                extrainfo[kvp.Key] = new JObject();
                            extrainfo[kvp.Key]["curmoney"] = myMoney;
                        }
                        moneySum += myMoney;
                    }
                    double usd_inv_sum = 0;
                    foreach (var kvp in CurInventory) {
                        if (extrainfo[kvp.Key] == null)
                            extrainfo[kvp.Key] = new JObject();
                        extrainfo[kvp.Key]["inventory_usd_cost"] = kvp.Value.ToString("C");
                        usd_inv_sum += kvp.Value;
                    }
                    double usd_trade_sum = 0;
                    foreach (var kvp in CurTradable) {
                        if (full) {
                            if (extrainfo[kvp.Key] == null)
                                extrainfo[kvp.Key] = new JObject();
                            extrainfo[kvp.Key]["tradable_usd_cost"] = kvp.Value.ToString("C");
                        }
                        usd_trade_sum += kvp.Value;
                    }
                    resp = new JObject {
                        ["success"] = true,
                        ["extrainfo"] = extrainfo,
                        ["moneysum"] = new JObject() {
                            ["RUB"] = moneySum,
                            ["USD"] = Economy.ConvertCurrency(Economy.Currency.RUB, Economy.Currency.USD, moneySum).ToString("C"),
                            ["INVUSD"] = usd_inv_sum.ToString("C"),
                            ["TRADE"] = usd_trade_sum.ToString("C")
                        }
                    };
                }
            } catch (Exception ex) {
                resp = new JObject {
                    ["success"] = false,
                    ["error"] = ex.Message,
                    ["trace"] = ex.StackTrace
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

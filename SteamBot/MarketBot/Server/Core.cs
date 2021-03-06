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
using Utility;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Web;
using System.Globalization;
using SteamBot.MarketBot.CS;
using SteamBot.MarketBot.CS.Bot;
using SteamBot;
using static SteamBot.Configuration;
using Utility.MongoApi;
using Utility.VK;
using Common.Utility;

namespace Server {
    public class Core : IDisposable {

        public Dictionary<string, string> TokenCache = new Dictionary<string, string>();
        public NewMarketLogger logger;
        private HttpListener server;
        private CoreConfig coreConfig;
        private ConcurrentDictionary<string, DateTime> LastPing = new ConcurrentDictionary<string, DateTime>();
        private MongoLogCollection mongoLogs = new MongoLogCollection();
        private MongoBannedUsers mongoBannedUsers = new MongoBannedUsers();
        private Dictionary<string, string> ipCache;
        private int requestsServed = 0;

        public void Init() {
            CurSizes = new Dictionary<string, int>();
            CurInventory = new Dictionary<string, double>();
            CurTradable = new Dictionary<string, double>();
            CurMedian = new Dictionary<string, double>();
            CurUntracked = new Dictionary<string, int>();
            CurMoney = new Dictionary<string, int>();
            ipCache = new Dictionary<string, string>();
        }

        public Core(int port = 4345) {
            try {
                server = new HttpListener();
                logger = new NewMarketLogger("Core");
                server.Prefixes.Add($"http://+:{port}/");
                Init();
                VK.Init();

                unstickeredCache = new EmptyStickeredDatabase();
                unstickeredCache.LoadFromArray(File.ReadAllLines(Path.Combine("assets", "emptystickered.txt")));

                logger.Nothing("Starting!");
                ReloadConfig();
                server.Start();
                logger.Nothing("Started!");
                Task.Run((Action)Listen);
                Task.Run((Action)BackgroundCheck);
                Task.Run((Action)UnstickeredDumper);
                //Task.Run((Action)DBHitProvider);
            } catch (Exception ex) {
                logger.Crash($"Message: {ex.Message}. Trace: {ex.StackTrace}");
            }
        }

        private void UnstickeredDumper() {
            while (!disposed) {
                Tasking.WaitForFalseOrTimeout(() => !disposed, 60000).Wait();
                string[] lines = unstickeredCache.Dump();
                File.WriteAllLines(Path.Combine("assets", "emptystickered.txt"), lines);
            }
        }

        public bool ReloadConfig() {
            try {
                coreConfig = JsonConvert.DeserializeObject<CoreConfig>(Request.Get(Consts.Endpoints.ServerConfig));
                foreach (var bot in coreConfig.Bots) {
                    TokenCache[bot.Username] = bot.TradeToken;
                }
                return true;
            } catch {
                logger.Crash("Could not reload config");
                return false;
            }
        }

        private bool PingedRecently(string name) {
            if (LastPing.TryGetValue(name, out DateTime dt)) {
                return DateTime.Now.Subtract(dt).TotalSeconds < 120;
            }
            return false;
        }

        private void BackgroundCheck() {
            Thread.Sleep(60000);
            while (!disposed) {
                try {
                    foreach (BotConfig bot in coreConfig.Bots) {
                        if (!PingedRecently(bot.Username)) {
                            logger.Warn($"Бот {bot.Username} давно не пинговал, видимо, он умер.");
                            VK.Alert($"Бот {bot.Username} давно не пинговал, видимо, он умер.");
                        }
                    }
                } catch {
                }
                Tasking.WaitForFalseOrTimeout(() => !disposed, 60000).Wait();
                ReloadConfig();
            }
        }

        private void Listen() {
            while (!disposed) {
                try {
                    ThreadPool.QueueUserWorkItem(Process, server.GetContext());
                } catch {

                }
            }
        }

        private Dictionary<string, int> CurSizes;
        private Dictionary<string, int> CurMoney;
        private Dictionary<string, double> CurInventory;
        private Dictionary<string, double> CurTradable;
        private Dictionary<string, double> CurMedian;
        private Dictionary<string, int> CurUntracked;
        private Dictionary<string, ConcurrentQueue<Pair<DateTime, int>>> salesHistorySizes = new Dictionary<string, ConcurrentQueue<Pair<DateTime, int>>>();
        private EmptyStickeredDatabase unstickeredCache;

        private void Process(object o) {
            var context = o as HttpListenerContext;
            JObject resp = null;
            try {
                logger.Info($"[Request {++requestsServed}] {context.Request.RemoteEndPoint.ToString()} - {context.Request.Url.AbsolutePath}");
                string Endpoint = context.Request.Url.AbsolutePath;
                if (Endpoint == Consts.Endpoints.GetBestToken) {
                    var Forced = coreConfig.Bots.Where(x => x.Force && PingedRecently(x.Username));
                    if (Forced.Any()) {
                        BotConfig forcedBot = Forced.First();
                        resp = new JObject {
                            ["success"] = true,
                            ["token"] = TokenCache[forcedBot.Username],
                            ["botname"] = forcedBot.Username,
                        };
                    } else {
                        var Filtered = CurSizes.Where(t => t.Value < Consts.CRITICALTHRESHHOLD && PingedRecently(t.Key));
                        if (!Filtered.Any()) {
                            throw new Exception("All bots are overflowing!");
                        }
                        KeyValuePair<string, int> kv = Filtered.OrderBy(
                            t => t.Value / coreConfig.Bots.First(x => x.Username == t.Key).Weight
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
                            ["token"] = TokenCache[kv.Key],
                            ["botname"] = kv.Key,
                            ["extrainfo"] = extrainfo
                        };
                    }
                } else if (Endpoint == Consts.Endpoints.BanUser) {
                    long id = context.Request.QueryString["id"] == null ? -1 : long.Parse(context.Request.QueryString["id"]);
                    if (id == -1) {
                        throw new Exception($"Id {id} doesn't look good");
                    }
                    if (mongoBannedUsers.Add(id)) {
                        resp = new JObject {
                            ["success"] = true
                        };
                    } else {
                        throw new Exception($"Could not add user (possibly he is present)");
                    }
                } else if (Endpoint == Consts.Endpoints.UnBanUser) {
                    long id = context.Request.QueryString["id"] == null ? -1 : long.Parse(context.Request.QueryString["id"]);
                    if (id == -1) {
                        throw new Exception($"Id {id} doesn't look good");
                    }
                    if (mongoBannedUsers.Delete(id)) {
                        resp = new JObject {
                            ["success"] = true
                        };
                    } else {
                        throw new Exception($"Could not delete user (possibly he isn't in banned)");
                    }
                } else if (Endpoint == Consts.Endpoints.GetBannedUsers) {
                    var stuff = new JArray(mongoBannedUsers.GetBannedUsers().Select(user => user.SteamID64));
                    resp = new JObject {
                        ["success"] = true,
                        ["userlist"] = stuff
                    };
                } else if (Endpoint == Consts.Endpoints.PutTradeToken) {
                    string[] usernames = context.Request.Headers.GetValues("botname") ?? new string[0];
                    if (usernames.Length != 1) {
                        throw new Exception($"You have to provide 1 username, {usernames.Length} were provided");
                    }
                    string[] data = context.Request.Headers.GetValues("data") ?? new string[0];
                    if (data.Length != 1) {
                        throw new Exception($"You have to provide 1 data, {data.Length} were provided");
                    }
                    TokenCache[usernames[0]] = data[0];
                    resp = new JObject {
                        ["success"] = true
                    };
                } else if (Endpoint == Consts.Endpoints.PutCurrentInventory) {
                    string[] usernames = context.Request.Headers.GetValues("botname") ?? new string[0];
                    if (usernames.Length != 1) {
                        throw new Exception($"You have to provide 1 username, {usernames.Length} were provided");
                    }
                    string[] data = context.Request.Headers.GetValues("data") ?? new string[0];
                    if (data.Length != 1) {
                        throw new Exception($"You have to provide 1 data, {data.Length} were provided");
                    }
                    CurSizes[usernames[0]] = int.Parse(data[0]);
                    resp = new JObject {
                        ["success"] = true
                    };
                } else if (Endpoint == Consts.Endpoints.GetAuthFile) {
                    string[] usernames = context.Request.Headers.GetValues("botname") ?? new string[0];
                    if (usernames.Length != 1) {
                        throw new Exception($"You have to provide 1 username, {usernames.Length} were provided");
                    }
                    string authPath = Path.Combine("authfiles", usernames[0] + ".auth");
                    if (File.Exists(authPath)) {
                        resp = new JObject {
                            ["success"] = true,
                            ["data"] = File.ReadAllText(authPath)
                        };
                    } else {
                        resp = new JObject {
                            ["success"] = false,
                            ["error"] = "File not found"
                        };
                    }
                } else if (Endpoint == Consts.Endpoints.PutEmptyStickered) {
                    string[] usernames = context.Request.Headers.GetValues("botname") ?? new string[0];
                    string[] data = context.Request.Headers.GetValues("data") ?? new string[0];
                    if (data.Length != 1) {
                        throw new Exception($"You have to provide 1 data, {data.Length} were provided");
                    }
                    string[] splitter = data[0].Split('_');
                    unstickeredCache.Add(long.Parse(splitter[0]), long.Parse(splitter[1]));
                    resp = new JObject {
                        ["success"] = true
                    };
                } else if (Endpoint == Consts.Endpoints.GetConfig) {
                    BotConfig chosen = null;
                    foreach (BotConfig bot in coreConfig.Bots) {
                        if (ipCache.TryGetValue(bot.Username, out string ip) && ip == context.Request.RemoteEndPoint.ToString()) {
                            chosen = bot;
                            break;
                        }
                    }
                    if (chosen == null) {
                        foreach (BotConfig bot in coreConfig.Bots) {
                            if (LastPing.TryGetValue(bot.Username, out DateTime dt)) {
                                if (DateTime.Now.Subtract(dt).TotalSeconds > 120) {
                                    chosen = bot;
                                    break;
                                }
                            } else {
                                chosen = bot;
                                break;
                            }
                        }
                    }
                    if (chosen == null) {
                        resp = new JObject {
                            ["success"] = false,
                            ["error"] = "No free instance available"
                        };
                    } else {
                        Configuration cfg = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(coreConfig));
                        
                        cfg.Bots = new BotInfo[]{
                            JsonConvert.DeserializeObject<BotInfo>(JsonConvert.SerializeObject(chosen))
                        };
                        resp = new JObject {
                            ["success"] = true,
                            ["config"] = JObject.FromObject(cfg)
                        };
                    }
                } else if (Endpoint == Consts.Endpoints.MongoFind) {
                    string query = context.Request.QueryString["query"] ?? "{}";
                    int limit = int.Parse(context.Request.QueryString["limit"] ?? "-1");
                    int skip = int.Parse(context.Request.QueryString["skip"] ?? "-1");
                    //TODO(noobgam): add other tables
                    var filtered = mongoLogs.Find(query, limit, skip);
                    var cursor = filtered.ToCursor();
                    JArray logs = new JArray();
                    while (cursor.MoveNext()) {
                        foreach (var msg in cursor.Current) {
                            logs.Add(msg.ToString());
                        }
                    }
                    resp = new JObject {
                        ["success"] = true,
                        ["extrainfo"] = logs
                    };
                } else if (Endpoint == Consts.Endpoints.PingPong) {
                    string[] usernames = context.Request.Headers.GetValues("botname") ?? new string[0];
                    if (usernames.Length != 1) {
                        throw new Exception($"You have to provide 1 username, {usernames.Length} were provided");
                    }
                    logger.Info($"{usernames[0]} ping");
                    LastPing[usernames[0]] = DateTime.Now;
                    ipCache[usernames[0]] = context.Request.RemoteEndPoint.ToString();

                    resp = new JObject {
                        ["success"] = true,
                        ["ping"] = "pong",
                    };
                } else if (Endpoint == Consts.Endpoints.SalesHistorySize) {
                    // deprecated
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
                } else if (Endpoint == Consts.Endpoints.PutTradableCost) {
                    string[] usernames = context.Request.Headers.GetValues("botname");
                    if (usernames.Length != 1) {
                        throw new Exception($"You have to provide 1 username, {usernames.Length} were provided");
                    }
                    string[] data = context.Request.Headers.GetValues("data");
                    if (data.Length != 1) {
                        throw new Exception($"You have to provide 1 data, {data.Length} were provided");
                    }
                    string[] stuff = data[0].Split(':');
                    CurTradable[usernames[0]] = double.Parse(stuff[0]);
                    CurUntracked[usernames[0]] = int.Parse(stuff[1]);
                    resp = new JObject {
                        ["success"] = true
                    };
                } else if (Endpoint == Consts.Endpoints.PutMedianCost) {
                    string[] usernames = context.Request.Headers.GetValues("botname");
                    if (usernames.Length != 1) {
                        throw new Exception($"You have to provide 1 username, {usernames.Length} were provided");
                    }
                    string[] data = context.Request.Headers.GetValues("data");
                    if (data.Length != 1) {
                        throw new Exception($"You have to provide 1 data, {data.Length} were provided");
                    }
                    CurMedian[usernames[0]] = double.Parse(data[0]);
                    resp = new JObject {
                        ["success"] = true
                    };

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
                    resp = new JObject {
                        ["success"] = true
                    };
                } else if (Endpoint == Consts.Endpoints.GetSalesDatabase) {
                    byte[] bytes = File.ReadAllBytes(Path.Combine("assets", "newDatabase"));
                    resp = new JObject {
                        ["success"] = true,
                        ["data"] = StringUtils.ToBase64(bytes)
                    };
                } else if (Endpoint == Consts.Endpoints.GetEmptyStickeredDatabase) {
                    string[] lines = unstickeredCache.Dump();
                    byte[] bytes = BinarySerialization.NS.Serialize(lines, true);
                    resp = new JObject {
                        ["success"] = true,
                        ["data"] = StringUtils.ToBase64(bytes)
                    };
                } else if (Endpoint == Consts.Endpoints.GetSalesDatabase) {
                    byte[] bytes = BinarySerialization.NS.Serialize(unstickeredCache);
                    resp = new JObject {
                        ["success"] = true,
                        ["data"] = StringUtils.ToBase64(bytes)
                    };
                } else if (Endpoint == Consts.Endpoints.RPS) {
                    resp = new JObject {
                        ["success"] = true,
                        //["rps"] = Balancer.GetNewItemsRPS()
                    };
                } else if (Endpoint == Consts.Endpoints.Status) {
                    bool full = context.Request.QueryString["full"] == null ? false : bool.Parse(context.Request.QueryString["full"]);
                    bool table = context.Request.QueryString["table"] == null ? false : bool.Parse(context.Request.QueryString["table"]);
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
                            extrainfo[kvp.Key]["curmoney"] = myMoney.ToString("C", new CultureInfo("ru-RU"));
                        }
                        moneySum += myMoney;
                    }
                    foreach (var kvp in CurMedian) {
                        if (kvp.Value == 0) continue;
                        if (extrainfo[kvp.Key] == null)
                            extrainfo[kvp.Key] = new JObject();
                        extrainfo[kvp.Key]["median_sum"] = kvp.Value.ToString("C", new CultureInfo("en-US"));
                    }
                    double usd_trade_sum = 0;
                    foreach (var kvp in CurTradable) {
                        if (full) {
                            if (extrainfo[kvp.Key] == null)
                                extrainfo[kvp.Key] = new JObject();
                            extrainfo[kvp.Key]["tradable_usd_cost"] = kvp.Value.ToString("C", new CultureInfo("en-US"));
                        }
                        usd_trade_sum += kvp.Value;
                    }
                    resp = new JObject {
                        ["success"] = true,
                        ["extrainfo"] = extrainfo,
                        ["moneysum"] = new JObject() {
                            ["RUB"] = moneySum,
                            ["USD"] = Economy.ConvertCurrency(Economy.Currency.RUB, Economy.Currency.USD, moneySum).ToString("C", new CultureInfo("en-US")),
                            ["TRADE"] = usd_trade_sum.ToString("C", new CultureInfo("en-US"))
                        }
                    };
                    if (table) {
                        if (RespondTable(context, resp)) {
                            return;
                        }
                    }
                }
                if (resp == null)
                    resp = new JObject {
                        ["success"] = false,
                        ["error"] = "Unsupported request"
                    };
                Respond(context, resp);
            } catch (Exception ex) {
                resp = new JObject {
                    ["success"] = false,
                    ["error"] = ex.Message,
                    ["trace"] = ex.StackTrace
                };
                Respond(context, resp);
            }
        }

        // process request and make response

        private void RawRespond(HttpListenerContext ctx, string resp) {
            try {
                byte[] buffer = Encoding.UTF8.GetBytes(resp);
                ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
                HttpListenerResponse response = ctx.Response;

                response.ContentLength64 = buffer.Length;
                Stream output = response.OutputStream;
                output.Write(buffer, 0, buffer.Length);

                output.Close();
            } catch (Exception ex) { 
                // might help in some dumb cases.
            }
        }        
        
        private string Row(string[] arr, string type = "th") {
            string res = "";
            res += "<tr>";
            for (int i = 0; i < arr.Length; ++i) {
                res += $"<{type}>";

                res += arr[i];

                res += $"</{type}>";
            }
            res += "</tr>";
            return res;
        }

        private bool ConvertToDomTable(JObject json, out string html) {
            html = "";
            try {
                int cnt = 0;
                SortUtils.Sort(json);
                foreach (var table in json) {
                    if (table.Key != "extrainfo" && table.Key != "moneysum")
                        continue;
                    //html += "Table " + table.Key + "</br>";
                    string header = "";
                    string body = "";
                    Dictionary<string, int> mapping = new Dictionary<string, int>();
                    if (table.Key == "extrainfo") {
                        header =
                        "<table style=\"width:100%\">";
                        foreach (var bot in (JObject)table.Value) {
                            foreach (JProperty innerkey in ((JObject)bot.Value).Properties()) {
                                if (!mapping.ContainsKey(innerkey.Name))
                                    mapping[innerkey.Name] = ++cnt;
                            }
                        }
                        string[] thing = new string[cnt + 1];
                        foreach (string x in mapping.Keys)
                            thing[mapping[x]] = x;
                        body += Row(thing, "th");
                        foreach (var bot in (JObject)table.Value) {
                            thing = new string[cnt + 1];
                            thing[0] = bot.Key;
                            foreach (JProperty innerkey in ((JObject)bot.Value).Properties()) {
                                thing[mapping[innerkey.Name]] = (string)innerkey.Value;
                            }
                            body += Row(thing, "td");
                        }
                    } else if (table.Key == "moneysum") {
                        header =
                        "<table style=\"width:50%\">";
                        string[] thing = new string[2];
                        foreach (var field in (JObject)table.Value) {
                            thing[0] = field.Key;
                            thing[1] = (string)field.Value;
                            body += Row(thing, "th");
                        }
                    }
                    string footer =
                        "</table>";
                    html += header + body + footer;
                }
                return true;

            } catch {
                return false;
            }

        }

        private bool RespondTable(HttpListenerContext ctx, JObject json) {
            try {
                string html =
                    @"<!DOCTYPE html>
<html>
<head>
<meta charset=" + "\"utf-8\"/>" +
    @"<style>
table, th, td {
    border: 1px solid black;
}
th {
	background-color: lightblue
}
</style>" +
//"<script>" + SteamBot.Properties.Resources.chartUpdateScript + "</script>" + 
@"</head>
<body>";
                //html += "<script src=\"https://canvasjs.com/assets/script/canvasjs.min.js\"></script>";
                if (!ConvertToDomTable(json, out string body)) {
                    return false;
                }
                //html += SteamBot.Properties.Resources.chartdiv;
                html += body;
                html += @"</body>
</html>";
                RawRespond(ctx, html);
                return true;
            }
            catch {
                return false;
            }
        }

        private void Respond(HttpListenerContext ctx, JObject json) {
            if (json["error"] == null) {
                //ctx.Response.StatusCode = 200;
                if (json["type"] != null && (string)json["type"] == "table") {
                    if (RespondTable(ctx, json))
                        return;
                    else {
                        json["respond_error"] = "response could not be processed as table";
                    }
                }
            } else {
                //ctx.Response.StatusCode = 500;
            }
            //TODO(noobgam): prettify if User-Agent is defined
            string resp = json.ToString(
                ctx.Request.Headers.GetValues("User-Agent") == null
                ? Newtonsoft.Json.Formatting.None
                : Newtonsoft.Json.Formatting.Indented);
            RawRespond(ctx, resp);
        }

        public void Stop() {
            Dispose(true);
        }

        #region IDisposable Support
        private bool disposed = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing) {
            if (!disposed) {
                if (disposing) {
                    server.Close();
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

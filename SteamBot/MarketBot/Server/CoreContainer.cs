using Common.Utility;
using CSGOTM;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamBot;
using SteamBot.MarketBot.CS.Bot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Utility;
using Utility.MongoApi;
using Utility.VK;
using static SteamBot.Configuration;

namespace Server
{
    public class CoreContainer : ApiEndpointContainer
    {
        private Dictionary<string, int> CurSizes = new Dictionary<string, int>();
        private Dictionary<string, int> CurMoney = new Dictionary<string, int>();
        private Dictionary<string, double> CurInventory = new Dictionary<string, double>();
        private Dictionary<string, double> CurTradable = new Dictionary<string, double>();
        private Dictionary<string, double> CurMedian = new Dictionary<string, double>();
        private Dictionary<string, int> CurUntracked = new Dictionary<string, int>();
        private Dictionary<string, string> TokenCache = new Dictionary<string, string>();
        private ConcurrentDictionary<string, DateTime> LastPing = new ConcurrentDictionary<string, DateTime>();
        private CoreConfig coreConfig;
        private EmptyStickeredDatabase unstickeredCache = new EmptyStickeredDatabase();
        private NewMarketLogger logger = new NewMarketLogger("CoreContainer");
        private MongoBannedUsers mongoBannedUsers = new MongoBannedUsers();
        private Dictionary<string, string> ipCache = new Dictionary<string, string>();
        private CoreProtocol coreProtocol = new CoreProtocol();

        public CoreContainer()
        {
            Init();
        }

        private void Init()
        {
            try
            {
                unstickeredCache.LoadFromArray(File.ReadAllLines(Path.Combine("assets", "emptystickered.txt")));
            } catch {

            }
            ReloadConfig();
            Task.Run((Action)BackgroundCheck);
            Task.Run((Action)UnstickeredDumper);
        }

        private void UnstickeredDumper()
        {
            while (true)
            {
                Thread.Sleep(60000);
                string[] lines = unstickeredCache.Dump();
                File.WriteAllLines(Path.Combine("assets", "emptystickered.txt"), lines);
            }
        }

        private bool ReloadConfig()
        {
            try
            {
                coreConfig = JsonConvert.DeserializeObject<CoreConfig>(Request.Get(Consts.Endpoints.ServerConfig));
                foreach (var bot in coreConfig.Bots)
                {
                    TokenCache[bot.Username] = bot.TradeToken;
                }
                return true;
            }
            catch
            {
                logger.Crash("Could not reload config");
                return false;
            }
        }

        private bool PingedRecently(string name)
        {
            if (LastPing.TryGetValue(name, out DateTime dt))
            {
                return DateTime.Now.Subtract(dt).TotalSeconds < 120;
            }
            return false;
        }

        private bool IsPrimetime()
        {
            //https://github.com/mono/mono/issues/6368
            // mono does not support TimeZone.
            DateTime dt = DateTime.UtcNow.AddHours(3);
            return dt.Hour >= 10 && dt.Hour <= 23;
        }

        private void BackgroundCheck() {
#if DEBUG
            logger.Warn("Background pinger is disabled in debug mode.");
#else
            Thread.Sleep(60000);
            while (true)
            {
                try
                {
                    foreach (BotConfig bot in coreConfig.Bots)
                    {
                        if (!PingedRecently(bot.Username) && IsPrimetime())
                        {
                            logger.Warn($"Бот {bot.Username} давно не пинговал, видимо, он умер.");
                            VK.Alert($"Бот {bot.Username} давно не пинговал, видимо, он умер.");
                        }
                    }
                }
                catch
                {
                }
                Thread.Sleep(60000);
                ReloadConfig();
            }
#endif
        }

        [ApiEndpoint(Consts.Endpoints.GetEmptyStickeredDatabase)]
        public JObject GetEmptyStickeredDatabase()
        {
            string[] lines = unstickeredCache.Dump();
            byte[] bytes = BinarySerialization.NS.Serialize(lines, true);
            return new JObject
            {
                ["success"] = true,
                ["data"] = StringUtils.ToBase64(bytes)
            };
        }

        [ApiEndpoint(Consts.Endpoints.GetSalesDatabase)]
        public JObject GetSalesDatabase() {
            byte[] bytes = File.ReadAllBytes(Path.Combine("assets", "newDatabase"));
            return new JObject {
                ["success"] = true,
                ["data"] = StringUtils.ToBase64(bytes)
            };
        }

        [ApiEndpoint(Consts.Endpoints.GetBestToken)]
        public JObject GetBestToken()
        {
            var Forced = coreConfig.Bots.Where(x => x.Force && PingedRecently(x.Username));
            if (Forced.Any())
            {
                BotConfig forcedBot = Forced.First();
                return new JObject
                {
                    ["success"] = true,
                    ["token"] = TokenCache[forcedBot.Username],
                    ["botname"] = forcedBot.Username,
                };
            }
            else
            {
                var Filtered = CurSizes.Where(
                    t => t.Value < Consts.CRITICALTHRESHHOLD 
                    && PingedRecently(t.Key)
                    && coreConfig.Bots.Any(botConfig => botConfig.Username == t.Key));
                if (!Filtered.Any())
                {
                    throw new ArgumentException("All bots are overflowing!");
                }
                KeyValuePair<string, int> kv = Filtered.OrderBy(
                    t => t.Value / coreConfig.Bots.First(x => x.Username == t.Key).Weight
                    ).FirstOrDefault();
                if (kv.Key == null)
                {
                    throw new ArgumentException("Don't know about bot inventory sizes");
                }

                JToken extrainfo = new JObject();
                foreach (var kvp in CurSizes)
                {
                    extrainfo[kvp.Key] = new JObject
                    {
                        ["inventorysize"] = kvp.Value
                    };
                }
                return new JObject
                {
                    ["success"] = true,
                    ["token"] = TokenCache[kv.Key],
                    ["botname"] = kv.Key,
                    ["extrainfo"] = extrainfo
                };
            }

        }

        [ApiEndpoint(Consts.Endpoints.GetCurrency)]
        public JObject GetCurrency() {
            return new JObject {
                ["success"] = true,
                ["quotes"] = new JObject {
                    ["USDRUB"] = Economy.ConvertCurrency(Economy.Currency.USD, Economy.Currency.RUB, 1)
                }
            };
        }

        [ApiEndpoint(Consts.Endpoints.BanUser)]
        public JObject BanUser([PathParam] long id)
        {
            if (mongoBannedUsers.Add(id))
            {
                return new JObject
                {
                    ["success"] = true
                };
            }
            else
            {
                throw new ArgumentException($"Could not add user (possibly he is present)");
            }
        }

        [ApiEndpoint(Consts.Endpoints.UnBanUser)]
        public JObject UnBanUser([PathParam] long id)
        {
            if (mongoBannedUsers.Delete(id))
            {
                return new JObject
                {
                    ["success"] = true
                };
            }
            else
            {
                throw new ArgumentException($"Could not delete user (possibly he isn't in banned)");
            }
        }

        [ApiEndpoint(Consts.Endpoints.GetBannedUsers)]
        public JObject GetBannedUsers()
        {
            var stuff = new JArray(mongoBannedUsers.GetBannedUsers().Select(user => user.SteamID64));
            return new JObject
            {
                ["success"] = true,
                ["userlist"] = stuff
            };
        }

        [ApiEndpoint(Consts.Endpoints.PutTradeToken)]
        public JObject PutTradeToken(
            [PathParam] string botname,
            [PathParam] string data)
        {
            TokenCache[botname] = data;
            return new JObject
            {
                ["success"] = true
            };
        }

        [ApiEndpoint(Consts.Endpoints.PutTradableCost)]
        public JObject PutTradableCost(
            [PathParam] string botname,
            [PathParam] string data)
        {
            string[] stuff = data.Split(':');
            CurTradable[botname] = double.Parse(stuff[0]);
            CurUntracked[botname] = int.Parse(stuff[1]);
            return new JObject
            {
                ["success"] = true
            };
        }

        [ApiEndpoint(Consts.Endpoints.PutCurrentInventory)]
        public JObject PutCurrentInventory(
            [PathParam] string botname,
            [PathParam] string data)
        {
            CurSizes[botname] = int.Parse(data);
            return new JObject
            {
                ["success"] = true
            };
        }

        [ApiEndpoint(Consts.Endpoints.PutMedianCost)]
        public JObject PutMedianCost(
            [PathParam] string botname,
            [PathParam] double data)
        {
            CurMedian[botname] = data;
            return new JObject
            {
                ["success"] = true
            };
        }

        [ApiEndpoint(Consts.Endpoints.PutMoney)]
        public JObject PutMoney(
            [PathParam] string botname,
            [PathParam] int data)
        {
            CurMoney[botname] = data;
            return new JObject
            {
                ["success"] = true
            };
        }

        [ApiEndpoint(Consts.Endpoints.GetAuthFile)]
        public JObject GetAuthFile([PathParam] string botname)
        {
            string authPath = Path.Combine("authfiles", botname + ".auth");
            if (File.Exists(authPath))
            {
                return new JObject {
                    ["success"] = true,
                    ["data"] = File.ReadAllText(authPath).Trim()
                };
            }
            else
            {
                return new JObject
                {
                    ["success"] = false,
                    ["error"] = "File not found"
                };
            }
        }

        [ApiEndpoint(Consts.Endpoints.PutEmptyStickered)]
        public JObject PutEmptyStickered(
            [PathParam] string data)
        {
            string[] splitter = data.Split('_');
            unstickeredCache.Add(long.Parse(splitter[0]), long.Parse(splitter[1]));
            return new JObject
            {
                ["success"] = true
            };
        }

        [ApiEndpoint(Consts.Endpoints.GetConfig)]
        public JObject GetConfig(HttpListenerContext context)
        {
            BotConfig chosen = null;
            foreach (BotConfig bot in coreConfig.Bots)
            {
                if (ipCache.TryGetValue(bot.Username, out string ip) && ip == context.Request.RemoteEndPoint.ToString())
                {
                    chosen = bot;
                    break;
                }
            }
            if (chosen == null)
            {
                foreach (BotConfig bot in coreConfig.Bots)
                {
                    if (LastPing.TryGetValue(bot.Username, out DateTime dt))
                    {
                        if (DateTime.Now.Subtract(dt).TotalSeconds > 120)
                        {
                            chosen = bot;
                            break;
                        }
                    }
                    else
                    {
                        chosen = bot;
                        break;
                    }
                }
            }
            if (chosen == null)
            {
                throw new ArgumentException("No free instance available");
            }
            else
            {
                Configuration cfg = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(coreConfig));

                cfg.Bots = new BotInfo[]{
                            JsonConvert.DeserializeObject<BotInfo>(JsonConvert.SerializeObject(chosen))
                        };
                return new JObject
                {
                    ["success"] = true,
                    ["config"] = JObject.FromObject(cfg)
                };
            }
        }

        [ApiEndpoint(Consts.Endpoints.PingPong)]
        public JObject PingPong(
            HttpListenerContext context, 
            [PathParam] string botname)
        {
            logger.Info($"{botname} ping");
            LastPing[botname] = DateTime.Now;
            ipCache[botname] = context.Request.RemoteEndPoint.ToString();

            return new JObject
            {
                ["success"] = true,
                ["ping"] = "pong",
            };
        }

        [ApiEndpoint(Consts.Endpoints.Status)]
        public JObject Status([PathParam] bool full = false)
        {
            JToken extrainfo = new JObject();
            double moneySum = 0;
            foreach (var kvp in CurSizes)
            {
                if (extrainfo[kvp.Key] == null)
                    extrainfo[kvp.Key] = new JObject();
                extrainfo[kvp.Key]["inventorysize"] = kvp.Value;
            }
            foreach (var kvp in CurMoney)
            {
                double myMoney = (double)kvp.Value / 100;
                if (full)
                {
                    if (extrainfo[kvp.Key] == null)
                        extrainfo[kvp.Key] = new JObject();
                    extrainfo[kvp.Key]["curmoney"] = myMoney.ToString("C", new CultureInfo("ru-RU"));
                }
                moneySum += myMoney;
            }
            foreach (var kvp in CurMedian)
            {
                if (kvp.Value == 0) continue;
                if (extrainfo[kvp.Key] == null)
                    extrainfo[kvp.Key] = new JObject();
                extrainfo[kvp.Key]["median_sum"] = kvp.Value.ToString("C", new CultureInfo("en-US"));
            }
            double usd_trade_sum = 0;
            foreach (var kvp in CurTradable)
            {
                if (full)
                {
                    if (extrainfo[kvp.Key] == null)
                        extrainfo[kvp.Key] = new JObject();
                    extrainfo[kvp.Key]["tradable_usd_cost"] = kvp.Value.ToString("C", new CultureInfo("en-US"));
                }
                usd_trade_sum += kvp.Value;
            }
            return new JObject
            {
                ["success"] = true,
                ["extrainfo"] = extrainfo,
                ["moneysum"] = new JObject()
                {
                    ["RUB"] = moneySum.ToString("C", new CultureInfo("ru-RU")),
                    ["USD"] = Economy.ConvertCurrency(Economy.Currency.RUB, Economy.Currency.USD, moneySum).ToString("C", new CultureInfo("en-US")),
                    ["TRADE"] = usd_trade_sum.ToString("C", new CultureInfo("en-US"))
                }
            };
        }
        
        [ApiEndpoint(Consts.Endpoints.Primetime)]
        public JObject Primetime() {
            return new JObject {
                ["success"] = true,
                ["primetime"] = IsPrimetime()
            };
        }
    }
}

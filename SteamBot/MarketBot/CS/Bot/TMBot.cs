using SteamBot;
using SteamBot.MarketBot.CS;
using SteamBot.MarketBot.CS.Bot;
using SteamBot.MarketBot.Utility.VK;
using SteamTrade;
using SuperSocket.ClientEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Utility;

namespace CSGOTM {
    public class TMBot : IDisposable {

        private string botName;
        public TMBot(Bot bot, Configuration.BotInfo config) {
            this.bot = bot;
            this.config = new BotConfig(config);
            botName = config.Username;
            Init();
        }

        public void Init() {
            if (Log == null) {
                Log = new NewMarketLogger(this);
            }
            Log.Info("Initializing TMBot " + bot.DisplayName);
            logic = new Logic(this);
            protocol = new Protocol(this);

            logic.Log = Log;
            protocol.Log = Log;

            logic.Protocol = protocol;
            protocol.Logic = logic;
            WaitingForRestart = false;

            Tasking.Run(Delayer, botName);
        }

        private void Delayer() {
            while (!bot.IsLoggedIn) {
                Thread.Sleep(10);
            }

            ReadyToRun = true;
            Tasking.Run(Restarter, botName);
            Tasking.Run(InventoryFetcher, botName);
        }

        private bool Alert(string message) {
            return VK.Alert($"[{bot.DisplayName}]: {message}");
        }

        private string ConvertQualityToRussian(string en_quality) {
            switch (en_quality) {
                case "Factory New":
                    return "Прямо с завода";
                case "Minimal Wear":
                    return "Немного поношенное";
                case "Field-Tested":
                    return "После полевых испытаний";
                case "Well-Worn":
                    return "Поношенное";
                case "Battle-Scarred":
                    return "Закаленное в боях";
                default:
                    return "";                    
            }
        }

        private void InventoryFetcher() {
            while (!WaitingForRestart) {
              
                GenericInventory inv = new GenericInventory(bot.SteamWeb);
                inv.load(730, new long[] { 2 }, bot.SteamUser.SteamID, "russian");
                int counter = inv.descriptions.Count;
                if (counter != 0) { //lol...
                    logic.cachedInventory = inv;
                    logic.cachedTradableCount = counter;
                    LocalRequest.PutInventory(config.Username, inv);
                    int cnt = 0;
                    double totalprice = 0;
                    double tradeprice = 0;
                    int untracked = 0;
                    foreach (var item in inv.descriptions) {
                        if (SteamDataBase.cache.TryGetValue(item.Value.market_hash_name, out double price)) {
                            ++cnt;
                            totalprice += price;
                            if (item.Value.tradable) {
                                tradeprice += price;
                            }
                        } else {
                            ++untracked;
                        }
                    }
                    logic._DatabaseLock.EnterReadLock();
                    double medianprice = 0;
                    int something = 0;
                    foreach (var item in inv.descriptions) {
                        Console.WriteLine(something++);
                        string quality;
                        try {
                            quality = item.Value.market_hash_name.Split(new char[] { '(', ')' })[1];
                        } 
                        catch {
                            //???
                            continue;
                        }
                        string runame = item.Value.name + " (" + ConvertQualityToRussian(quality) + ")";
                        if (logic.dataBase.TryGetValue(runame, out Logic.SalesHistory sales)) {
                            medianprice += sales.median;
                        }
                    }
                    logic._DatabaseLock.ExitReadLock();
                    LocalRequest.PutInventoryCost(config.Username, totalprice);
                    LocalRequest.PutTradableCost(config.Username, tradeprice, untracked);
                    LocalRequest.PutMedianCost(config.Username, Economy.ConvertCurrency(Economy.Currency.RUB, Economy.Currency.USD, medianprice));
                }
                LocalRequest.PutMoney(config.Username, protocol.GetMoney());
                if (counter != 0)
                    Tasking.WaitForFalseOrTimeout(IsRunning, timeout: Consts.MINORCYCLETIMEINTERVAL).Wait(); //10 minutes this data is pretty much static
                else
                    Tasking.WaitForFalseOrTimeout(IsRunning, timeout: Consts.MINORCYCLETIMEINTERVAL / 10).Wait(); //1 minute because I need to reupload inventory on failure.
            }
        }

        private void Restarter() {
            while (!WaitingForRestart) {
                if (prior >= (int)RestartPriority.Alarm) {
                    if (prior >= (int)RestartPriority.Restart) {
                        ScheduleRestart();
                        Alert("бот перезапускается.");
                        Thread.Sleep(5000);
                        bot.ScheduleRestart();
                        return;
                    }

                }
                prior = (int)(prior * 0.9);
                Thread.Sleep(1000);
            }
        }

        public void FlagError(RestartPriority error, string message = "") {
            prior += (int)error;
            if ((int)error > 0 && message != "") {
                Alert("Error: " + message);
            }
        }

        //basically percentages
        public enum RestartPriority {
            UnknownError = 0,
            SmallError = 3,
            MediumError = 10,
            BigError = 15,
            Alarm = 60,
            CriticalError = 100,
            Restart = 100,
        }

        public bool IsRunning() {
            return !WaitingForRestart;
        }

        public void ScheduleRestart() {
            WaitingForRestart = true;
        }

        public void Dispose() {
            ScheduleRestart();
        }

        public int prior = 0;
        public Bot bot;
        public bool ReadyToRun = false;
        public BotConfig config;

        private Logic logic;
        private Protocol protocol;
        private NewMarketLogger Log;
        private bool WaitingForRestart = false; //usually false
    }
}

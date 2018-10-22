using SteamBot;
using SteamBot.MarketBot.Utility.VK;
using SteamTrade;
using SuperSocket.ClientEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CSGOTM {
    public class TMBot {

        public void Stop() {
            Log.Dispose();
        }

        public TMBot(Bot bot, Configuration.BotInfo config) {
            this.bot = bot;
            this.config = new BotConfig(config);

            logic = new Logic(this);
            protocol = new Protocol(this);
            Log = new MarketLogger(this);

            logic.Log = Log;
            protocol.Log = Log;

            logic.Protocol = protocol;
            protocol.Logic = logic;

            Task.Run((Action)Delayer);
        }

        private void Delayer() {
            while (!bot.IsLoggedIn) {
                Thread.Sleep(10);
            }

            ReadyToRun = true;
            Task.Run((Action)Restarter);
            Task.Run((Action)InventoryFetcher);
        }

        private bool Alert(string message) {
            return VK.Alert($"[{bot.DisplayName}]: {message}");
        }

        private void InventoryFetcher() {
            while (!WaitingForRestart) {
              
                GenericInventory inv = new GenericInventory(bot.SteamWeb);
                inv.load(730, new long[] { 2 }, bot.SteamUser.SteamID);
                int counter = inv.descriptions.Count(x => x.Value.tradable);
                if (counter != 0) { //lol...
                    logic.cachedInventory = inv;
                    logic.cachedTradableCount = counter;
                    LocalRequest.PutInventory(config.Username, inv);
                }
                LocalRequest.PutMoney(config.Username, protocol.GetMoney());
                Utility.Tasking.WaitForFalseOrTimeout(IsRunning, timeout: Consts.MINORCYCLETIMEINTERVAL).Wait(); //10 minutes this data is pretty much static
            }
        }

        private void Restarter() {
            while (!WaitingForRestart) {
                if (prior >= (int)RestartPriority.Alarm) {
                    if (prior >= (int)RestartPriority.Restart) {
                        WaitingForRestart = true;
                        Alert("бот перезапускается.");
                        Task.Delay(5000)
                            .ContinueWith(task => bot.ScheduleRestart());
                        break;
                    }

                }
                prior = (int)(prior * 0.9);
                Thread.Sleep(1000);
            }
        }

        public void FlagError(RestartPriority error) {
            prior += (int)error;
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

        public int prior = 0;
        public Bot bot;
        public bool ReadyToRun = false;
        public BotConfig config;

        private Logic logic;
        private Protocol protocol;
        private MarketLogger Log;
        private bool WaitingForRestart = false; //usually false
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CSGOTM.TMBot;

namespace CSGOTM {
    public class MarketLogger : SteamBot.Log {
        private TMBot bot;

        public MarketLogger(TMBot bot) : base(bot.config.Username, bot.config.DisplayName) {
            this.bot = bot;
        }

        public void ApiError(RestartPriority prior, string data, params object[] formatParams) {
            bot.FlagError(prior);
            base.ApiError(data, formatParams);
        }

        // This outputs a log entry of the level error.
        public void Error(RestartPriority prior, string data, params object[] formatParams) {
            bot.FlagError(prior);
            base.Error(data, formatParams);
        }
    }
}
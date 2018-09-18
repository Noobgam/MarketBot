using SteamBot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSGOTM {
    public class BotConfig {
        public BotConfig(Configuration.BotInfo steamConfig) {
            Api = steamConfig.MarketApiKey;
            Username = steamConfig.Username;
            DisplayName = steamConfig.DisplayName;
            Consts.TokenCache[Username] = steamConfig.TradeToken;
        }

        public string Api;
        public string Username;
        public string DisplayName;
    }
}

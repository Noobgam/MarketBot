using SteamBot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BitSkins {
    public class BitSkinsBot {
        public Bot bot;
        public bool ReadyToRun = false;
        public BotConfig config;
        public Protocol protocol;

        public BitSkinsBot(Bot bot, Configuration.BotInfo config) {
            this.bot = bot;
            this.config = new BotConfig(config);
            protocol = new Protocol();
        }
    }
}

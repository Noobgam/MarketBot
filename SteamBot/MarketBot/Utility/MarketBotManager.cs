using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility
{
    /// <summary>
    /// Reasoning behind this class is simple. 
    /// This class will lead us to further separating Market and SteamBot, which is our non-distant goal
    /// More than that, construction of markets might become way easier than it used to be
    /// </summary>
    class MarketBotManager
    {
        public enum AvailableBot
        {
            CSGOTM,
            DOTA2TM,
            //OPSKINS
        }
        
        /// <summary>
        /// Should construct bot and return whether the construction succeeded
        /// Example of false is MarketBotManager(CSGOTM, CSGOTM, CSGOTM, ...)
        /// </summary>
        /// <param name="bot"></param>
        private bool ConstructBot(AvailableBot bot)
        {
            //do something
            return false;
        }

        public MarketBotManager(params AvailableBot[] usable)
        {
            
        }
    }
}

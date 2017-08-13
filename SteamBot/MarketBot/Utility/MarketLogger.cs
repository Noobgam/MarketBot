using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility
{
    public class MarketLogger : SteamBot.Log
    {
        public MarketLogger(String path, String marketPrefix = "Market") : base(path, marketPrefix)
        {
        }
    }
}

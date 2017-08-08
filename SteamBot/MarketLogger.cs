using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility
{
    class MarketLogger : SteamBot.Log
    {
        MarketLogger(String path) : base(path, "Market")
        {
        }
    }
}

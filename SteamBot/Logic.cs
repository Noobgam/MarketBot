using System;
using System.Threading.Tasks;
using System.Web;
using System.Net;
using System.Text;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.ComponentModel;
using SteamBot.SteamGroups;
using SteamKit2;
using WebSocket4Net;
using SteamTrade;
using SteamKit2.Internal;
using SteamTrade.TradeOffer;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using System.Collections.Specialized;

namespace CSGOTM
{
    public class Logic
    {
        public CSGOTMProtocol Protocol;
        int tmp = 0;
        public Logic()
        {

        }
        public Logic(CSGOTMProtocol Pr1)
        {
            Protocol = Pr1;
        }
        public void Hello()
        {
            Console.WriteLine(++tmp);
        }
    }
}
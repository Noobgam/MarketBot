using SteamBot.MarketBot.CS.Bot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CSGOTM.Consts;

namespace Utility.MongoApi {
    public class MongoLogCollection : GenericMongoDB<NewMarketLogger.LogMessage> {
        public override string GetCollectionName() {
            return "logs";
        }

        public override string GetDBName() {
            return Databases.Mongo.SteamBotMain;
        }
    }
}

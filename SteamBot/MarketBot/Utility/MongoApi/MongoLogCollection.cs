using SteamBot.MarketBot.CS.Bot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SteamBot.MarketBot.Utility.MongoApi {
    public class MongoLogCollection : GenericMongoDB<NewMarketLogger.LogMessage> {
        const string DBNAME = "steambot_main";

        public MongoLogCollection() : base(DBNAME) {

        }

        public override string GetCollectionName() {
            return "logs";
        }
    }
}

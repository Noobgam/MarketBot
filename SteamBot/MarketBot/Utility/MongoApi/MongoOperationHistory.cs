using SteamBot.MarketBot.CS.Bot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSGOTM;
using MongoDB.Bson;
using static CSGOTM.Consts;

namespace SteamBot.MarketBot.Utility.MongoApi {
    public class MongoOperationHistory : GenericMongoDB<MongoHistoricalOperation> {
        readonly string botname;

        public MongoOperationHistory(string name) : base() {
            botname = name;
        }

        public void InsertOrReplace(HistoricalOperation data) {            
            collection.ReplaceOne(
                new BsonDocument("_id", data.h_event_id),
                new MongoHistoricalOperation(data, botname),
                new MongoDB.Driver.UpdateOptions { IsUpsert = true }
                );
        }

        public override string GetCollectionName() {
            return "operation_history";
        }

        public override string GetDBName() {
            return Databases.Mongo.SteamBotMain;
        }
    }
}
using CSGOTM;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SteamBot.MarketBot.Utility.MongoApi {
    public class MongoHistoryItem : NewHistoryItem {
        [BsonId]
        public ObjectId id;

        public MongoHistoryItem(NewHistoryItem item) : base(item) {
            id = ObjectId.GenerateNewId(DateTime.Now);
        }
    }

    public class MongoHistoryCSGO : GenericMongoDB<MongoHistoryItem> {
        public override string GetCollectionName() {
            return "history_csgo";
        }

        public override string GetDBName() {
            return Consts.Databases.Mongo.SteamBotMain;
        }      

        public void Add(NewHistoryItem item) {
            collection.InsertOne(
                new MongoHistoryItem(item)
            );
        }
    }
}

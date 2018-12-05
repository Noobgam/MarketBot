using CSGOTM;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SteamBot.MarketBot.Utility.MongoApi {
    public class MongoBannedUsers : GenericMongoDB<BannedUser> {
        public override string GetCollectionName() {
            return "banned-users";
        }

        public override string GetDBName() {
            return Consts.Databases.Mongo.SteamBotMain;
        }

        public List<BannedUser> GetBannedUsers() {
            return collection.FindSync(FilterDefinition<BannedUser>.Empty).ToList();
        }

        public void Add(long id) {
            collection.ReplaceOne(
                new BsonDocument("_id", id),
                new BannedUser(id),
                new UpdateOptions { IsUpsert = true }
                );
        }
    }
}

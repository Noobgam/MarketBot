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
        public MongoBannedUsers() {
            Add(76561198379677339L
                ,76561198328630783L
                ,76561198321472965L
                ,76561198408228242L
                ,76561198033167623L
                ,76561198857835986L
                ,76561198857940860L
                ,76561198328630783L
                ,76561198356087536L
                ,76561198309616729L
                ,76561198316325564L
                ,76561198027819122L);            
        }

        public override string GetCollectionName() {
            return "banned_users";
        }

        public override string GetDBName() {
            return Consts.Databases.Mongo.SteamBotMain;
        }

        public List<BannedUser> GetBannedUsers() {
            return collection.FindSync(FilterDefinition<BannedUser>.Empty).ToList();
        }

        public void Add(params long[] ids) {
            foreach (long id in ids) {
                Add(id);
            }
        }

        public bool Delete(long id) {
            DeleteResult res = collection.DeleteOne(new BsonDocument("_id", id));
            if (!res.IsAcknowledged)
                return false;
            return res.DeletedCount > 0;
        }

        public bool Add(long id) {
           ReplaceOneResult res = collection.ReplaceOne(
                new BsonDocument("_id", id),
                new BannedUser(id),
                new UpdateOptions { IsUpsert = true }
                );
            if (!res.IsAcknowledged)
                return false;
            if (res.UpsertedId != null)
                return true;
            return false;
        }
    }
}

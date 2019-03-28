using CSGOTM;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility.MongoApi {
    public class MongoBannedUsers : GenericMongoDB<BannedUser> {

        public override string GetCollectionName() {
            return "banned_users";
        }

        public override string GetDBName() {
            return Consts.Databases.Mongo.SteamBotMain;
        }

        public List<BannedUser> GetBannedUsers() {
            return FindAll().ToList();
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

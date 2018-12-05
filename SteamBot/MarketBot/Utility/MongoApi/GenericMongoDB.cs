using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace SteamBot.MarketBot.Utility.MongoApi {
    public abstract class GenericMongoDB<Data> {

        //Interface
        public abstract string GetDBName();
        public abstract string GetCollectionName();

        protected MongoClient mongoClient;
        protected IMongoDatabase db;
        protected IMongoCollection<Data> collection;

        protected GenericMongoDB() {
            mongoClient = new MongoClient();
            db = mongoClient.GetDatabase(GetDBName());
            collection = db.GetCollection<Data>(GetCollectionName());
        }

        public void Insert(Data data) {
            collection.InsertOne(data);
        }

        public IFindFluent<Data, Data> Find(string query, int limit = -1, int skip = -1) {
            try {
                var bson = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(query);
                IFindFluent<Data, Data> temp = collection.Find(bson);
                if (limit != -1)
                    temp.Limit(limit);
                if (skip != -1)
                    temp.Skip(skip);
                return temp;
            } catch {
                return null;
            }
        }

        public IFindFluent<Data, Data> Find(BsonDocument filter) {
            return collection.Find(filter);
        }
    }
}

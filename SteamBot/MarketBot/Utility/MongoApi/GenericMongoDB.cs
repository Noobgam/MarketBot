using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace SteamBot.MarketBot.Utility.MongoApi {
    abstract class GenericMongoDB<Data> {
        public abstract string GetCollectionName();
        protected MongoClient mongoClient;
        protected IMongoDatabase db;
        protected IMongoCollection<Data> collection;

        protected GenericMongoDB(string database) {
            mongoClient = new MongoClient();
            db = mongoClient.GetDatabase(database);
            collection = db.GetCollection<Data>(GetCollectionName());
        }

        public void Insert(Data data) {
            collection.InsertOne(data);
        }
    }
}

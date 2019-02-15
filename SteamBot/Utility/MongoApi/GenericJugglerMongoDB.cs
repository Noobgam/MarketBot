using CSGOTM;
using Utility;
using Utility.MongoApi;

namespace SteamBot.Utility.MongoApi {
    public abstract class GenericJugglerMongoDB<Data> : GenericMongoDB<Data>{
        protected GenericJugglerMongoDB() : base(Consts.Endpoints.juggler.Remove(0, "http://".Length)) {
        }
    }
}

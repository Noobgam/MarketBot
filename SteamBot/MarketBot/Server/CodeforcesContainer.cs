using MongoDB.Driver;
using Newtonsoft.Json.Linq;
using Server;
using SteamBot.Utility.MongoApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SteamBot.MarketBot.Server {
    public class CodeforcesContainer : ApiEndpointContainer {
        private readonly FakeDatabase fakeDatabase = new FakeDatabase();
        private Random R = new Random();

        [ApiEndpoint("/getfake/")]
        public JObject GetFake([PathParam] string handle = null) {
            Fake fake = null;
            if (handle == null) {
                fake = fakeDatabase.FindAny();
            } else {
                fake = fakeDatabase.FindByHandle(handle);
            }
            if (fake == null) {
                throw new ArgumentException($"No such fake found.");
            }
            return new JObject {
                ["success"] = true,
                ["handle"] = fake.name,
                ["password"] = fake.password
            };             
        }

        [ApiEndpoint("/getallfakes/")]
        public JObject GetAllFakes() {
            JArray fakes = JArray.FromObject(fakeDatabase.FindAll().ToList());
            return new JObject {
                ["success"] = true,
                ["fakes"] = fakes
            };
        }

        [ApiEndpoint("/getrandomfake/")]
        public JObject GetRandomFake() {
            List<Fake> fakes = fakeDatabase.FindAll().ToList();
            if (fakes.Count == 0) {
                throw new ArgumentException($"No fakes found.");
            }
            return new JObject {
                ["success"] = true,
                ["fake"] = JObject.FromObject(fakes[R.Next(fakes.Count)])
            };
        }
    }
}

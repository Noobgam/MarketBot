using NUnit.Framework;
using SteamBot.MarketBot.CS.Bot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SteamBotUnitTest.Serializing.Database {
    class Database {

        [Test]
        public void EndToEnd() {
            EmptyStickeredDatabase database = new EmptyStickeredDatabase();
            CheckDump(database);
            for (int i = 0; i < 100; ++i) {
                database.Add(i, i * i);
                CheckDump(database);
                Assert.IsTrue(database.NoStickers(i, i * i));
            }
        }

        private void CheckDump(EmptyStickeredDatabase db) {
            HashSet<Tuple<long, long>> temp = db.unstickeredCache;
            Assert.IsTrue(db.LoadFromArray(db.Dump()));
            Assert.AreEqual(temp, db.unstickeredCache);
            Assert.IsTrue(
                db.LoadFromArray(
                    BinarySerialization.NS.Deserialize<string[]>(
                        BinarySerialization.NS.Serialize(db.Dump()))));
            Assert.AreEqual(temp, db.unstickeredCache);
            Assert.IsTrue(
                db.LoadFromArray(
                    BinarySerialization.NS.Deserialize<string[]>(
                        BinarySerialization.NS.Serialize(db.Dump(), true), true)));
            Assert.AreEqual(temp, db.unstickeredCache);
        }
    }
}

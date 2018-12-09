using CSGOTM;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SteamBot.MarketBot.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WebSocket4Net;

namespace SteamBotUnitTest.Websockets {

    [TestFixture]
    class FormatChecker {
        private static WebSocket socket;
        private static readonly TimeSpan TEST_RUNNING_TIME = new TimeSpan(0, 0, 60);

        [SetUp]
        public void Init() {
            socket = new WebSocket("wss://wsn.dota2.net/wsn/", receiveBufferSize: 65536);
            socket.Open();
            while (socket.State != WebSocketState.Open) {
                Thread.Sleep(100);
            }
        }

        [Test]
        public void CheckHistory() {
            string item1 = "\"[\\\"3081291837\\\",\\\"188530139\\\",\\\"AK-47 | Orbit Mk01 (Field-Tested)\\\",\\\"\\u0421\\u0435\\u0433\\u043e\\u0434\\u043d\\u044f 20:07\\\",\\\"48300\\\",\\\"AK-47 | \\u041e\\u0440\\u0431\\u0438\\u0442\\u0430, \\u0432\\u0435\\u0440. 01 (\\u041f\\u043e\\u0441\\u043b\\u0435 \\u043f\\u043e\\u043b\\u0435\\u0432\\u044b\\u0445 \\u0438\\u0441\\u043f\\u044b\\u0442\\u0430\\u043d\\u0438\\u0439)\\\",\\\"#000000\\\",\\\"RUB\\\"]\"";
            string item2 = "\"[\\\"384801282\\\",\\\"0\\\",\\\"CS:GO Weapon Case 3\\\",\\\"\\u0421\\u0435\\u0433\\u043e\\u0434\\u043d\\u044f 20:03\\\",\\\"739\\\",\\\"\\u041e\\u0440\\u0443\\u0436\\u0435\\u0439\\u043d\\u044b\\u0439 \\u043a\\u0435\\u0439\\u0441 CS:GO, \\u0442\\u0438\\u0440\\u0430\\u0436 #3\\\",\\\"#000000\\\",\\\"RUB\\\"]\"";
            string item3 = "\"[\\\"1325773219\\\",\\\"143865972\\\",\\\"Music Kit | Daniel Sadowski, The 8-Bit Kit\\\",\\\"\\u0421\\u0435\\u0433\\u043e\\u0434\\u043d\\u044f 20:01\\\",\\\"13955\\\",\\\"\\u041d\\u0430\\u0431\\u043e\\u0440 \\u043c\\u0443\\u0437\\u044b\\u043a\\u0438 | Daniel Sadowski - The 8-Bit Kit\\\",\\\"#000000\\\",\\\"RUB\\\"]\"";
            NewHistoryItem item = new NewHistoryItem(item1);
            item = new NewHistoryItem(item2);
            item = new NewHistoryItem(item3);
        }

        [Test]
        public void CheckHistorySocket() {
            socket.Send("history_go");
            int counter = 0;
            socket.MessageReceived += (sender, e) => {
                if (e.Message == "pong")
                    return;
                JObject temp = JObject.Parse(e.Message);
                Assert.AreEqual((string)temp["type"], "history_go");                
                Assert.IsNotNull(temp["data"]);
                TestContext.WriteLine((string)temp["data"]);
                char[] trimming = { '[', ']' };
                string data = (string)temp["data"];
                NewHistoryItem historyItem = new NewHistoryItem(data);
                ++counter;
            };
            Thread.Sleep(TEST_RUNNING_TIME);
            Assert.Greater(counter, 0);
        }

        [TearDown] 
        public void Dispose() {
            socket.Close();
            socket = null;
        }
    }
}

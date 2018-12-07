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
        private static readonly TimeSpan TEST_RUNNING_TIME = new TimeSpan(0, 0, 30);

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
            socket.Send("history_go");
            int counter = 0;
            socket.MessageReceived += (sender, e) => {
                if (e.Message == "pong")
                    return;
                JObject temp = JObject.Parse(e.Message);
                Assert.AreEqual((string)temp["type"], "history_go");
                Assert.IsNotNull(temp["data"]);
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

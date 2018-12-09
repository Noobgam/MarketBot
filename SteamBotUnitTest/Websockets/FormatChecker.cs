using CSGOTM;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using SteamBot.MarketBot.Utility;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Utility;
using WebSocket4Net;

namespace SteamBotUnitTest.Websockets {

    [TestFixture]
    [Parallelizable(ParallelScope.Self)]
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
        public void CheckHistoryItemFormat() {
            string item1 = "\"[\\\"3081291837\\\",\\\"188530139\\\",\\\"AK-47 | Orbit Mk01 (Field-Tested)\\\",\\\"\\u0421\\u0435\\u0433\\u043e\\u0434\\u043d\\u044f 20:07\\\",\\\"48300\\\",\\\"AK-47 | \\u041e\\u0440\\u0431\\u0438\\u0442\\u0430, \\u0432\\u0435\\u0440. 01 (\\u041f\\u043e\\u0441\\u043b\\u0435 \\u043f\\u043e\\u043b\\u0435\\u0432\\u044b\\u0445 \\u0438\\u0441\\u043f\\u044b\\u0442\\u0430\\u043d\\u0438\\u0439)\\\",\\\"#000000\\\",\\\"RUB\\\"]\"";
            string item2 = "\"[\\\"384801282\\\",\\\"0\\\",\\\"CS:GO Weapon Case 3\\\",\\\"\\u0421\\u0435\\u0433\\u043e\\u0434\\u043d\\u044f 20:03\\\",\\\"739\\\",\\\"\\u041e\\u0440\\u0443\\u0436\\u0435\\u0439\\u043d\\u044b\\u0439 \\u043a\\u0435\\u0439\\u0441 CS:GO, \\u0442\\u0438\\u0440\\u0430\\u0436 #3\\\",\\\"#000000\\\",\\\"RUB\\\"]\"";
            string item3 = "\"[\\\"1325773219\\\",\\\"143865972\\\",\\\"Music Kit | Daniel Sadowski, The 8-Bit Kit\\\",\\\"\\u0421\\u0435\\u0433\\u043e\\u0434\\u043d\\u044f 20:01\\\",\\\"13955\\\",\\\"\\u041d\\u0430\\u0431\\u043e\\u0440 \\u043c\\u0443\\u0437\\u044b\\u043a\\u0438 | Daniel Sadowski - The 8-Bit Kit\\\",\\\"#000000\\\",\\\"RUB\\\"]\"";
            new NewHistoryItem(item1);
            new NewHistoryItem(item2);
            new NewHistoryItem(item3);
        }

        [Test]
        public void CheckNewItemFormat() {
            string item1 = "{\"i_quality\":\"\\u041d\\u0435\\u043c\\u043d\\u043e\\u0433\\u043e \\u043f\\u043e\\u043d\\u043e\\u0448\\u0435\\u043d\\u043d\\u043e\\u0435\",\"i_name_color\":\"D2D2D2\",\"i_classid\":\"937251650\",\"i_instanceid\":\"188530139\",\"i_market_hash_name\":\"Desert Eagle | Bronze Deco (Minimal Wear)\",\"i_market_name\":\"Desert Eagle | \\u0411\\u0440\\u043e\\u043d\\u0437\\u043e\\u0432\\u0430\\u044f \\u0434\\u0435\\u043a\\u043e\\u0440\\u0430\\u0446\\u0438\\u044f (\\u041d\\u0435\\u043c\\u043d\\u043e\\u0433\\u043e \\u043f\\u043e\\u043d\\u043e\\u0448\\u0435\\u043d\\u043d\\u043e\\u0435)\",\"ui_price\":31.96,\"ui_currency\":\"RUB\",\"app\":\"go\"}";
            string item2 = "{\"app\":\"go\",\"i_quality\":\"\\u041d\\u0435\\u043c\\u043d\\u043e\\u0433\\u043e \\u043f\\u043e\\u043d\\u043e\\u0448\\u0435\\u043d\\u043d\\u043e\\u0435\",\"i_name_color\":\"D2D2D2\",\"i_classid\":\"2993844086\",\"i_instanceid\":\"188530139\",\"i_market_hash_name\":\"M4A4 | Bullet Rain (Minimal Wear)\",\"i_market_name\":\"M4A4 | \\u0414\\u043e\\u0436\\u0434\\u044c \\u0438\\u0437 \\u043f\\u0443\\u043b\\u044c (\\u041d\\u0435\\u043c\\u043d\\u043e\\u0433\\u043e \\u043f\\u043e\\u043d\\u043e\\u0448\\u0435\\u043d\\u043d\\u043e\\u0435)\",\"ui_price\":840,\"ui_currency\":\"RUB\"}";
            string item3 = "{\"i_quality\":\"\\u041d\\u0435\\u043c\\u043d\\u043e\\u0433\\u043e \\u043f\\u043e\\u043d\\u043e\\u0448\\u0435\\u043d\\u043d\\u043e\\u0435\",\"i_name_color\":\"D2D2D2\",\"i_classid\":\"2532234329\",\"i_instanceid\":\"188530139\",\"i_market_hash_name\":\"P250 | See Ya Later (Minimal Wear)\",\"i_market_name\":\"P250 | \\u041f\\u0440\\u043e\\u0449\\u0430\\u043b\\u044c\\u043d\\u044b\\u0439 \\u043e\\u0441\\u043a\\u0430\\u043b (\\u041d\\u0435\\u043c\\u043d\\u043e\\u0433\\u043e \\u043f\\u043e\\u043d\\u043e\\u0448\\u0435\\u043d\\u043d\\u043e\\u0435)\",\"ui_price\":727.29,\"ui_currency\":\"RUB\",\"app\":\"go\"}";
            string item4 = "{\"i_quality\":\"\\u041f\\u043e\\u0441\\u043b\\u0435 \\u043f\\u043e\\u043b\\u0435\\u0432\\u044b\\u0445 \\u0438\\u0441\\u043f\\u044b\\u0442\\u0430\\u043d\\u0438\\u0439\",\"i_name_color\":\"CF6A32\",\"i_classid\":\"2735398626\",\"i_instanceid\":\"188530170\",\"i_market_hash_name\":\"StatTrak\\u2122 Nova | Wild Six (Field-Tested)\",\"i_market_name\":\"StatTrak\\u2122 Nova | Wild Six (\\u041f\\u043e\\u0441\\u043b\\u0435 \\u043f\\u043e\\u043b\\u0435\\u0432\\u044b\\u0445 \\u0438\\u0441\\u043f\\u044b\\u0442\\u0430\\u043d\\u0438\\u0439)\",\"ui_price\":109.99,\"ui_currency\":\"RUB\",\"app\":\"go\"}";
            string item5 = "{\"i_quality\":\"\\u041f\\u0440\\u044f\\u043c\\u043e \\u0441 \\u0437\\u0430\\u0432\\u043e\\u0434\\u0430\",\"i_name_color\":\"CF6A32\",\"i_classid\":\"520028703\",\"i_instanceid\":\"188530144\",\"i_market_hash_name\":\"StatTrak\\u2122 Desert Eagle | Conspiracy (Factory New)\",\"i_market_name\":\"StatTrak\\u2122 Desert Eagle | \\u0417\\u0430\\u0433\\u043e\\u0432\\u043e\\u0440 (\\u041f\\u0440\\u044f\\u043c\\u043e \\u0441 \\u0437\\u0430\\u0432\\u043e\\u0434\\u0430)\",\"ui_price\":1480,\"ui_currency\":\"RUB\",\"app\":\"go\"}";
            new NewItem(item1);
            new NewItem(item2);
            new NewItem(item3);
            new NewItem(item4);
            new NewItem(item5);
        }

        [Test]
        public void ParseMoney() {
            string data = "\"1 332.56\\u00a0<small><\\/small>\"";
            int money = 0;
            string splitted = data.Split('\"')[1].Split('<')[0].Replace(" ", "");
            if (splitted.EndsWith("\\u00a0")) {
                money = (int)(double.Parse(splitted.Substring(0, splitted.Length - "\\u00a0".Length), new CultureInfo("en")) * 100);
            } else {
                money = (int)(double.Parse(splitted, new CultureInfo("en")) * 100);
            }
            Assert.AreEqual(133256, money);
        }
        
        [Test]
        public void CheckSocket() {
            socket.Send("history_go");
            socket.Send("newitems_go");
            int historyItemCounter = 0;
            int newItemCounter = 0;
            socket.MessageReceived += (sender, e) => {
                if (e.Message == "pong")
                    return;
                JObject temp = JObject.Parse(e.Message);
                string type = (string)temp["type"];
                string data = (string)temp["data"];
                switch (type) {
                    case "history_go":
                        NewHistoryItem historyItem = new NewHistoryItem(data);
                        ++historyItemCounter;
                        break;
                    case "newitems_go":
                        NewItem item = new NewItem(data);
                        newItemCounter++;
                        break;
                    default:
                        Assert.Fail("Socket sent me trash");
                        break;
                }
            };
            Thread.Sleep(TEST_RUNNING_TIME);
            Assert.Greater(historyItemCounter, 0);
            Assert.Greater(newItemCounter, 0);
        }

        [TearDown] 
        public void Dispose() {
            socket.Close();
            socket = null;
        }
    }
}

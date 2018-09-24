using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VkNet;
using VkNet.Enums.Filters;
using VkNet.Model;

namespace SteamBot.MarketBot.Utility.VK {
    public static class VK {
        public static void Init() {
        }

        static VkApi api;
        static LongPollServerResponse longPollServerInfo;
        static VK() {
            Task.Run((Action)Refresher);
            Task.Run((Action)Listener);
        }

        private static bool Message(long id, string message) {
            try {
                api.Messages.Send(
                    new VkNet.Model.RequestParams.MessagesSendParams {
                        UserId = id,
                        Message = message
                    }
                );
                return true;
            } catch (Exception ex) {
                return false;
            }
        }

        static readonly Dictionary<long, AlertLevel> alerter = new Dictionary<long, AlertLevel>() {
            { 426787197L, AlertLevel.Critical },
            { 30415979L,  AlertLevel.Critical }
        }; 

        public static bool Alert(string message, AlertLevel level = AlertLevel.Critical) {
            bool result = true;
            foreach (var kv in alerter) {
                if (kv.Value <= level) {
                    result &= Message(kv.Key, message);
                }
            }
            return result;
        }

        public enum AlertLevel { 
            Critical = 1,
        };

        static bool RefreshSession() {
            try {
                api = new VkApi();
                api.Authorize(new ApiAuthParams {
                    ApplicationId = 6686807,
                    Login = "Novice1998",
                    Password = "Novice1998",
                    Settings = Settings.Messages
                });
                longPollServerInfo = api.Messages.GetLongPollServer(true);
                return true;
            } catch (Exception ex) {
                return false;
            }
        }

        public const string Pineapple =
            "Не знаю, что на это ответить. Давай расскажу тебе, почему " +
            "твои губы кровоточат, когда ты ешь ананас. " +
            "Это потому что ты приёмный, а твоя настоящая мать - " +
            "дохлая псина, и, пока все будут крутиться вокруг ёлки, " +
            "ты будешь крутиться вокруг хуя какого-нибудь мудака, " +
            "чтобы заработать себе на пропитание.";

        static void HandleMessage(Message message) {
            api.Messages.MarkAsReadAsync(message.FromId.Value.ToString(), message.Id);
            if (message.FromId == 110139244 || message.FromId == 62228399) {
                return; //just ignore these two people.
            }
            if (!alerter.ContainsKey(message.FromId.Value)) {
                Message(message.FromId.Value, "Пошёл нахуй, не пиши мне больше, урод");
                Thread.Sleep(500);
                return;
            }
            if (message.FromId == 426787197) {
                Message(message.FromId.Value, Pineapple);// $"Только пидоры говорят \"{message.Text}\"");
                Thread.Sleep(500);
            } else if (message.FromId == 30415979) {
                Message(message.FromId.Value, Pineapple);
                Thread.Sleep(500);
            }
        }

        static void Listener() {
            while (true) {
                if (longPollServerInfo != null) {
                    try {
                        var history = api.Messages.GetLongPollHistory(
                            new VkNet.Model.RequestParams.MessagesGetLongPollHistoryParams {
                                Ts = longPollServerInfo.Ts,
                                Pts = longPollServerInfo.Pts,
                                
                            });
                        foreach (Message msg in history.Messages) {
                            HandleMessage(msg);
                        }
                        longPollServerInfo.Pts = history.NewPts;

                    } catch (Exception ex) {

                    }
                }
            }
        }

        static void Refresher() {
            while (true) {
                RefreshSession();
                Thread.Sleep(300000);      
            }
        }
    }
}

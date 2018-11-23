using SteamBot.MarketBot.CS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Utility;
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
            Tasking.Run((Action)Refresher);
            Tasking.Run((Action)Listener);
            Tasking.Run((Action)PinRefresh);
        }

        private static void PinRefresh() {
            while (true) {
                Thread.Sleep(10000);
                try {
                    //api.Messages.Pin(2000000000 + 118, 1115190);
                } catch (Exception e) { 
                    Console.WriteLine("Error " + e.Message);
                }
            }
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
            Garbage = 0,
            Critical = 1,
        };

        static bool RefreshSession() {
            try {
                api = new VkApi();
                api.Authorize(new ApiAuthParams {
                    ApplicationId = 6743975,
                    Login = "Novice1998",
                    Password = "7PixelWideNoobgam",
                    AccessToken = "45ca4499949cfd7298b31891177830589253f0639ff13d6b9be7b3559375a642e75322972556bdeeb11fd",
                    Settings = Settings.Messages | Settings.Offline
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
            if (message.Action.Type.ToString() == "chat_pin_message" && message.PeerId == 2000000118 && message.FromId != 62228399) {
                Task.Delay(2500).ContinueWith(tsk => {
                    try {
                        api.Messages.Pin(2000000000 + 118, 1115190);
                    } catch {
                    }
                });
                return;
            }
            api.Messages.MarkAsReadAsync(message.FromId.Value.ToString(), message.Id);
            foreach (var attach in message.Attachments) {
                //attach.Document.Uri
                if (attach.Type == typeof(VkNet.Model.Attachments.Document) && attach.Instance is VkNet.Model.Attachments.Document doc) {
                    try {
                        SteamDataBase.RefreshDatabase(Request.Get(doc.Uri));
                        Message(message.FromId.Value, $"Спасибо, обновил базу.");
                        Thread.Sleep(500);
                    } catch {
                        Message(message.FromId.Value, $"Что ты мне прислал, долбоёб? Думал меня трахнуть? Я тебя сам трахну");
                    }
                    return;
                }
            }
            //if (message.FromId == 110139244 || message.FromId == 62228399) {
            //    return; //just ignore these two people.
            //}
            if (!alerter.ContainsKey(message.FromId.Value)) {
                //Message(message.FromId.Value, "Пошёл нахуй, не пиши мне больше, урод");
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
                Thread.Sleep(250);
                if (longPollServerInfo != null) {
                    try {
                        var history = api.Messages.GetLongPollHistory(
                            new VkNet.Model.RequestParams.MessagesGetLongPollHistoryParams {
                                Ts = ulong.Parse(longPollServerInfo.Ts),
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

﻿using SteamBot.MarketBot.CS;
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

namespace Utility.VK {
    public static class VK {
        public static void Init() {
        }

        static VkApi api;
        static LongPollServerResponse longPollServerInfo;
        static VK() {
            Tasking.Run((Action)Refresher);
            Tasking.Run((Action)Listener);
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

        public enum Admins {
            Noobgam = 426787197,
            Felix = 30415979,
        }

        static readonly Array AdminList = Enum.GetValues(typeof(Admins));

        static readonly Dictionary<Admins, AlertLevel> alerter = new Dictionary<Admins, AlertLevel>() {
            { Admins.Noobgam, AlertLevel.Noobgam },
            { Admins.Felix,  AlertLevel.Felix }
        }; 

        public static bool Alert(string message, AlertLevel level = AlertLevel.Noobgam) {
            bool result = true;
            foreach (var kv in alerter) {
                if ((kv.Value & level) == kv.Value) {
                    result &= Message((long)kv.Key, message);
                }
            }
            return result;
        }

        public enum AlertLevel { 
            None = 0,
            Noobgam = 1,
            Felix = 2,
            All = Felix | Noobgam
        };

        static bool RefreshSession() {
            try {
                api = new VkApi();
                api.Authorize(new ApiAuthParams {
                    ApplicationId = 6743198,
                    Login = "Novice1998",
                    Password = "7PixelWideNoobgam",
                    AccessToken = "20b9b0a87c28628883ca0571df6b9b3e90bdeb13731d1161a28978d59beb2ab488ff1ae398c1cf22b5905",
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
            api.Messages.MarkAsReadAsync(message.FromId.Value.ToString(), message.Id);
            bool admin = false;
            foreach (var x in AdminList) {
                if ((long)x == message.FromId) {                    
                    admin = true;
                    break;
                }
            }
            if (!admin) {
                return;
            }
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

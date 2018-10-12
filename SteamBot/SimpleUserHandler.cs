using System;
using SteamKit2;
using System.Collections.Generic;
using SteamTrade;
using SteamTrade.TradeOffer;
using SteamTrade.TradeWebAPI;
using System.Threading.Tasks;
using System.Threading;

namespace SteamBot
{
    public class SimpleUserHandler : UserHandler
    {
        public TF2Value AmountAdded;

        public SimpleUserHandler (Bot bot, SteamID sid) : base(bot, sid) {}

        public override bool OnGroupAdd()
        {
            return false;
        }

        public override bool OnFriendAdd () 
        {
            return false;
        }

        public override void OnLoginCompleted()
        {
        }

        public override void OnChatRoomMessage(SteamID chatID, SteamID sender, string message)
        {
            Log.Info(Bot.SteamFriends.GetFriendPersonaName(sender) + ": " + message);
            base.OnChatRoomMessage(chatID, sender, message);
        }

        public override void OnFriendRemove () {}
        
        public override void OnMessage (string message, EChatEntryType type) 
        {
        }

        public override bool OnTradeRequest() 
        {
            return true;
        }
        
        public override void OnTradeError (string error) 
        {
            SendChatMessage("Oh, there was an error: {0}.", error);
            Log.Warn (error);
        }
        
        public override void OnTradeTimeout () 
        {
            SendChatMessage("Sorry, but you were AFK and the trade was canceled.");
            Log.Info ("User was kicked because he was AFK.");
        }
        
        public override void OnTradeInit() 
        {
            SendTradeMessage("Success. Please put up your items.");
        }
        
        public override void OnTradeAddItem (Schema.Item schemaItem, Inventory.Item inventoryItem) {}
        
        public override void OnTradeRemoveItem (Schema.Item schemaItem, Inventory.Item inventoryItem) {}
        
        public override void OnTradeMessage (string message) {}
        
        public override void OnTradeReady (bool ready) 
        {
        }

        public override void OnTradeAwaitingConfirmation(long tradeOfferID)
        {
            Log.Warn("Trade ended awaiting confirmation");
            //SendChatMessage("Please complete the confirmation to finish the trade");
        }

        public override void OnTradeOfferUpdated(TradeOffer offer)
        {
            Log.Info($"Handling offer. Message: [{offer.Message}]");
            switch (offer.OfferState)
            {
                case TradeOfferState.TradeOfferStateAccepted:
                    return;
                case TradeOfferState.TradeOfferStateActive:
                    var their = offer.Items.GetTheirItems();
                    var my = offer.Items.GetMyItems();
                    if (my.Count == 0) {
                        for (int retryCount = 0; retryCount < 2; ++retryCount) {
                            var tradeAccept = offer.Accept();
                            if (tradeAccept.Accepted) {
                                string st = "Offer completed.";
                                if (their.Count != 0)
                                    st += " Received: " + their.Count + " items.";
                                Log.Warn(st);
                                return;
                            } else {
                                Log.Error($"Could not accept offer on try {retryCount + 1}: {tradeAccept.TradeError}.");
                                Thread.Sleep(333);
                            }
                        }
                        break;
                    }
                    long aid = -1, cid = -1;
                    bool unstable = false;
                    foreach (var item in their)
                    {
                        if (aid == -1)
                        {
                            aid = item.AppId;
                        }
                        else
                        {
                            if (aid != item.AppId)
                            {
                                unstable = true;
                            }
                            aid = item.AppId;
                        }

                        if (cid == -1)
                        {
                            cid = item.ContextId;
                        }
                        else
                        {
                            if (cid != item.ContextId)
                            {
                                unstable = true;
                            }
                            cid = item.ContextId;
                        }
                    }
                    foreach (var item in my)
                    {
                        if (aid == -1)
                        {
                            aid = item.AppId;
                        }
                        else
                        {
                            if (aid != item.AppId)
                            {
                                unstable = true;
                            }
                            aid = item.AppId;
                        }

                        if (cid == -1)
                        {
                            cid = item.ContextId;
                        }
                        else
                        {
                            if (cid != item.ContextId)
                            {
                                unstable = true;
                            }
                            cid = item.ContextId;
                        }
                    }

                    string appid_contextid;
                    if (unstable) appid_contextid = "unstable";
                    else appid_contextid = aid + "-" + cid;
                    switch (appid_contextid)
                    {
                        case "730-2":
                            if (my.Count > 0 && !offer.IsOurOffer) //if the offer is bad we decline it. 
                            {
                                offer.Decline();
                                Log.Error("Offer failed. Invalid trade request. (not issued by me, has my items there)");
                                return;
                            } else { 
                                var tradeAccept = offer.Accept();
                                if (tradeAccept.Accepted) {
                                    string st = "Offer completed.";
                                    if (their.Count != 0)
                                        st += " Received: " + their.Count + " items.";
                                    if (my.Count != 0)
                                        st += " Lost:     " + my.Count + " items.";
                                    Log.Warn(st);
                                    if (my.Count != 0) {
                                        //Log.Info("Sending confirmation in 1 second [Deprecated, trying to log this]");
                                        //Task.Delay(1000).
                                        //    ContinueWith(tsk => Bot.AcceptAllMobileTradeConfirmations());
                                    }
                                    return;
                                } else {
                                    Log.Error($"Could not accept offer: {tradeAccept.TradeError}");
                                    return;
                                }
                            }
                        case "unstable":
                            break;
                        default:
                            break;
                    }
                    return;
                case TradeOfferState.TradeOfferStateNeedsConfirmation:
                    return;
                case TradeOfferState.TradeOfferStateInEscrow:
                    return;
                case TradeOfferState.TradeOfferStateCountered:
                    Log.Info($"Trade offer {offer.TradeOfferId} was countered");
                    return;
                case TradeOfferState.TradeOfferStateCanceled:
                    return;
                case TradeOfferState.TradeOfferStateDeclined:
                    return;
                default:
                    Log.Info($"Trade offer {offer.TradeOfferId} failed, status is {offer.OfferState}");
                    return;
            }
        }

        public override void OnTradeAccept() 
        {
            if (IsAdmin)
            {
                //Even if it is successful, AcceptTrade can fail on
                //trades with a lot of items so we use a try-catch
                try {
                    if (Trade.AcceptTrade())
                        Log.Success("Trade Accepted!");
                }
                catch {
                    Log.Warn ("The trade might have failed, but we can't be sure.");
                }
            }
        }        
    }
 
}


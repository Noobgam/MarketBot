using SteamKit2;
using System.Collections.Generic;
using SteamTrade;
using SteamTrade.TradeOffer;
using SteamTrade.TradeWebAPI;
using System;
using System.Threading;
using System.Threading.Tasks;

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
            //just because I'm sick of it.
            //SendChatMessage(Bot.ChatResponse);
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
            if (!ready)
            {
                Trade.SetReady (false);
            }
            else
            {
                if(Validate ())
                {
                    Trade.SetReady (true);
                }
                SendTradeMessage("Scrap: {0}", AmountAdded.ScrapTotal);
            }
        }

        public override void OnTradeAwaitingConfirmation(long tradeOfferID)
        {
            Log.Warn("Trade ended awaiting confirmation");
            //SendChatMessage("Please complete the confirmation to finish the trade");
        }

        public override void OnTradeOfferUpdated(TradeOffer offer)
        {
            switch (offer.OfferState)
            {
                case TradeOfferState.TradeOfferStateAccepted:
                        return;
                case TradeOfferState.TradeOfferStateActive: {
                        var their = offer.Items.GetTheirItems();
                        var my = offer.Items.GetMyItems();
                        long aid = -1, cid = -1;
                        bool unstable = false;
                        foreach (var item in their) {
                            if (aid == -1) {
                                aid = item.AppId;
                            } else {
                                if (aid != item.AppId) {
                                    unstable = true;
                                }
                                aid = item.AppId;
                            }

                            if (cid == -1) {
                                cid = item.ContextId;
                            } else {
                                if (cid != item.ContextId) {
                                    unstable = true;
                                }
                                cid = item.ContextId;
                            }
                        }
                        foreach (var item in my) {
                            if (aid == -1) {
                                aid = item.AppId;
                            } else {
                                if (aid != item.AppId) {
                                    unstable = true;
                                }
                                aid = item.AppId;
                            }

                            if (cid == -1) {
                                cid = item.ContextId;
                            } else {
                                if (cid != item.ContextId) {
                                    unstable = true;
                                }
                                cid = item.ContextId;
                            }
                        }

                        string appid_contextid;
                        if (unstable) appid_contextid = "unstable";
                        else appid_contextid = aid + "-" + cid;
                        switch (appid_contextid) {
                            case "730-2": { 
                                    if (my.Count > 0 && !offer.IsOurOffer) //if the offer is bad we decline it. 
                                    {
                                        offer.Decline();
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        Console.WriteLine("Offer failed.");
                                        Console.WriteLine("[Reason]: Invalid trade request.");
                                        Console.ForegroundColor = ConsoleColor.White;
                                        return;
                                    } else if (offer.Accept().Accepted) {
                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                        Console.WriteLine("Offer completed.");
                                        if (their.Count != 0)
                                            Console.WriteLine("Received: " + their.Count + " items.");
                                        if (my.Count != 0)
                                            Console.WriteLine("Lost:     " + my.Count + " items.");
                                        Console.ForegroundColor = ConsoleColor.White;
                                        if (offer.Items.GetMyItems().Count != 0) {
                                            Thread.Sleep(1000);
                                            var task = Task.Run(() => {
                                                Bot.AcceptAllMobileTradeConfirmations();
                                            });
                                            if (task.Wait(TimeSpan.FromSeconds(2)))
                                                return;
                                            else
                                                return;
                                        }
                                        return;
                                    } else {
                                        Console.ForegroundColor = ConsoleColor.Red;
                                        Console.WriteLine("Offer failed.");
                                        Console.WriteLine("[Reason]: Unknown error.");
                                        Console.ForegroundColor = ConsoleColor.White;
                                        return;
                                    }
                                }
                            case "unstable": {
                                break;
                            }
                            default: {
                                    break;
                            }
                        }



                        return;
                    }
                case TradeOfferState.TradeOfferStateNeedsConfirmation: {
                        Thread.Sleep(1000);
                        var task = Task.Run(() => {
                            Bot.AcceptAllMobileTradeConfirmations();
                        });
                        if (task.Wait(TimeSpan.FromSeconds(2)))
                            return;
                        else
                            return;
                    }
                case TradeOfferState.TradeOfferStateInEscrow:
                case TradeOfferState.TradeOfferStateCountered:
                    Log.Info($"Trade offer {offer.TradeOfferId} was countered");
                    return;
                case TradeOfferState.TradeOfferStateCanceled:
                    return;
                case TradeOfferState.TradeOfferStateDeclined:
                    return;
                default:
                    Log.Info($"Trade offer {offer.TradeOfferId} failed");
                    return;
            }
        }

        public override void OnTradeAccept() 
        {
            if (Validate() || IsAdmin)
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

        public bool Validate ()
        {            
            AmountAdded = TF2Value.Zero;
            
            List<string> errors = new List<string> ();
            
            foreach (TradeUserAssets asset in Trade.OtherOfferedItems)
            {
                var item = Trade.OtherInventory.GetItem(asset.assetid);
                if (item.Defindex == 5000)
                    AmountAdded += TF2Value.Scrap;
                else if (item.Defindex == 5001)
                    AmountAdded += TF2Value.Reclaimed;
                else if (item.Defindex == 5002)
                    AmountAdded += TF2Value.Refined;
                else
                {
                    var schemaItem = Trade.CurrentSchema.GetItem (item.Defindex);
                    errors.Add ("Item " + schemaItem.Name + " is not a metal.");
                }
            }
            
            if (AmountAdded == TF2Value.Zero)
            {
                errors.Add ("You must put up at least 1 scrap.");
            }
            
            // send the errors
            if (errors.Count != 0)
                SendTradeMessage("There were errors in your trade: ");
            foreach (string error in errors)
            {
                SendTradeMessage(error);
            }
            
            return errors.Count == 0;
        }
        
    }
 
}


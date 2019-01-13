using SteamBot;
using SteamBot.MarketBot.CS.Bot;
using SteamBot.MarketBot.Utility.VK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Utility {
    public class Tasking {
        static NewMarketLogger taskLog = new NewMarketLogger("Tasking");
        public static async Task<bool> WaitForFalseOrTimeout(Func<bool> condition) {
            await Task.Run(async () => {
                while (condition()) await Task.Delay(80);
            });
            return true;
        }

        public static async Task<bool> WaitForFalseOrTimeout(Func<bool> condition, int timeout) {
            if (timeout <= 3000) {
                await Task.Delay(timeout);
                return !condition();
            }            
            Console.WriteLine($"Timeout: {timeout}");
            var tokenSource2 = new CancellationTokenSource();
            CancellationToken ct = tokenSource2.Token;

            Task waitTask = Task.Run(async () => {
                while (condition()) {
                    await Task.Delay(1000, ct);
                }
            });
            Task delayTask = Task.Run(async () => {
                await Task.Delay(timeout, ct);
            });
            Task temp = await Task.WhenAny(waitTask, delayTask);
            tokenSource2.Cancel();
            return temp == waitTask;
        }

        public static void Run(Action runnable, string botName = "EXTRA") {
            taskLog.Info(botName, $"[{runnable.Method}] started");
            Task.Run(() => {
                try {
                    runnable();;
                } catch (Exception e) {
                    taskLog.Crash($"Message: {e.Message} \n {e.StackTrace}");
                }
            }).ContinueWith(tsk => taskLog.Info(botName, $"[{runnable.Method}] ended"));
        }
    }
}

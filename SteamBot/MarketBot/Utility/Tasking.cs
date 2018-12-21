using SteamBot;
using SteamBot.MarketBot.CS.Bot;
using SteamBot.MarketBot.Utility.VK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility {
    public class Tasking {
        static NewMarketLogger taskLog = new NewMarketLogger();
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
            Task waitTask = Task.Run(async () => {
                while (condition()) await Task.Delay(1000);
            });

            Task temp = await Task.WhenAny(waitTask, Task.Delay(timeout));
            if (temp == waitTask)
                Console.WriteLine("Emergency stop");
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

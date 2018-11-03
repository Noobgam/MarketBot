using SteamBot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility {
    class Tasking {
        static Log taskLog = new Log("task.log");
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

        public static void Run(Action x) {
            taskLog.Info($"[{x.Method}] started");
            Task.Run(x).ContinueWith(tsk => taskLog.Info($"[{x.Method}] ended"));
        }
    }
}

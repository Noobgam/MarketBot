using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility
{
    class Tasking
    {
        public static async Task<bool> WaitForFalseOrTimeout(Func<bool> condition, int frequency = 25, int timeout = -1)
        {
            Task waitTask = Task.Run(async () =>
            {
                while (condition()) await Task.Delay(frequency);
            });

            Task temp = await Task.WhenAny(waitTask, Task.Delay(timeout));
            return temp == waitTask;
        }
    }
}

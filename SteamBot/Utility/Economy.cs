using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility {
    public static class Economy {
        static double CachedRatio = 66.2165;
        static DateTime LastCache = DateTime.MinValue;
        static int lastId = 0;
        public enum Currency {
            RUB,
            USD
        }
        static private Random R = new Random();

        public static JObject GetData() {
            string[] apiKeys = { "01e1f0a1c3a65ded676e69cc09dea8bc",
                                 "618104c6516893d35cb5cc33e92b345c",
                                 "9de598203ec40accd8ef57e5bb6c6987", };
            lastId = R.Next(apiKeys.Length);
            string api = apiKeys[lastId];
            return JObject.Parse(Request.Get($"http://apilayer.net/api/live?access_key={api}&currencies=RUB&source=USD&format=1"));
        }

        public static bool UpdateCache() {
            try {
                JObject temp = GetData();
                if ((bool)temp["success"]) {
                    CachedRatio = (double)temp["quotes"]["USDRUB"];
                    LastCache = DateTime.Now;
                }
                return true;
            } catch (Exception e) {
                VK.VK.Alert($"Could not update Economy cache. Last used id is {lastId}");
                return false;
            }

        }

        /// <summary>
        /// Converts specific amount of one currency into the other.
        /// </summary>
        /// <param name="from">Currency to convert from.</param>
        /// <param name="to">Currency to convert to.</param>
        /// <param name="amount">Amount of currency to convert from.</param>
        /// <returns>Converted value of new currency.</returns>
        public static double ConvertCurrency(Currency from, Currency to, double amount) {
            if (DateTime.Now.Subtract(LastCache).TotalMinutes > 18) {
                UpdateCache();
            }
            if (from == Currency.RUB && to == Currency.USD) {
                return amount / CachedRatio;
            }
            throw new NotImplementedException("Unable to convert this stuff");
        }
    }
}
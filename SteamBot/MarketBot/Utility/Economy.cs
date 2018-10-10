using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility {
    static class Economy {
        static double CachedRatio = 66.316902;
        static DateTime LastCache = DateTime.MinValue;
        public enum Currency {
            RUB,
            USD
        }

        /// <summary>
        /// Converts specific amount of one currency into the other.
        /// </summary>
        /// <param name="from">Currency to convert from.</param>
        /// <param name="to">Currency to convert to.</param>
        /// <param name="amount">Amount of currency to convert from.</param>
        /// <returns>Converted value of new currency.</returns>
        public static double ConvertCurrency(Currency from, Currency to, double amount) {
            if (DateTime.Now.Subtract(LastCache).TotalMinutes > 30) {
                try {
                    JObject temp = JObject.Parse(Request.Get("http://apilayer.net/api/live?access_key=618104c6516893d35cb5cc33e92b345c&currencies=RUB&source=USD&format=1"));
                    if ((bool)temp["success"]) {
                        CachedRatio = (double)temp["quotes"]["USDRUB"];
                        LastCache = DateTime.Now;
                    }
                } catch (Exception e) { 

                }
            }
            if (from == Currency.RUB && to == Currency.USD) {
                return amount / CachedRatio;
            }
            throw new NotImplementedException("Unable to convert this stuff");
        }
    }
}
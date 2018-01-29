using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility {
    static class Economy {
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
        static double ConvertCurrency(Currency from, Currency to, double amount) {
            //TODO: implement currency evaluator.
            return 0;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility {
    interface Item {
        /// <summary>
        /// Determine if GetName() can be applied. Should be true in most cases.
        /// </summary>
        /// <returns>True if GetName() can be called, false otherwise.</returns>
        bool HasName();

        /// <summary>
        /// Get name of the item (or unspecified value if item currently has no name).
        /// </summary>
        /// <returns>String - english name of the item</returns>
        string GetName();

        /// <summary>
        /// Evaluates current price of the item in given currency.
        /// </summary>
        /// <param name="currency">Currency to calculate value of the item in.</param>
        /// <returns>Double value - exact price of specific item in given currency on the corresponding market.</returns>
        double GetPrice(Economy.Currency currency);
    }
}
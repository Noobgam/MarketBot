using System.Globalization;
using System.Text.RegularExpressions;

namespace Utility {
    public class Encode {
        public static string DecodeEncodedNonAsciiCharacters(string value) {
            return Regex.Replace(
                value,
                @"\\u(?<Value>[a-zA-Z0-9]{4})",
                m => {
                    return ((char)int.Parse(m.Groups["Value"].Value, NumberStyles.HexNumber)).ToString();
                });
        }
    }
}

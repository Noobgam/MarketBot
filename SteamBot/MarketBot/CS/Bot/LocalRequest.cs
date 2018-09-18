using Newtonsoft.Json.Linq;
using System.Collections.Specialized;
using System.Net;

namespace CSGOTM {
    public static class LocalRequest {
        public static JToken RawGet(string endpoint, WebHeaderCollection headers) {
            return JToken.Parse(Utility.Request.Get(Consts.Endpoints.localhost + endpoint, headers));
        }

        public static JToken RawGet(string endpoint, string botname) {
            WebHeaderCollection headers = new WebHeaderCollection {
                ["botname"] = botname
            };
            return RawGet(endpoint, headers);
        }

        public static void RawPut(string endpoint, string botname, string data) {
            WebHeaderCollection headers = new WebHeaderCollection {
                ["botname"] = botname,
                ["data"] = data
            };
            RawGet(endpoint, headers);
        }
    }
}

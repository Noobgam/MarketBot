using SteamBot.MarketBot.CS.Bot;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Utility {
    public static class Request {
        public static NewMarketLogger Log = new NewMarketLogger();
        public static HttpClient httpClient = new HttpClient();

        static Request() {
        }

        public static async Task<string> GetAsync(string uri) {
            using (HttpResponseMessage result = await httpClient.GetAsync(uri)) {
                return await result.Content.ReadAsStringAsync();
            }
        }

        public static string RawGet(string uri) {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.Proxy = null;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream)) {
                return reader.ReadToEnd();
            }
        }

        public static string Get(string uri) {
            try {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
                request.Proxy = null;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream)) {
                    return reader.ReadToEnd();
                }
            } catch (Exception) {
                Log.Error($"Error happened during GET {uri}");
                return "";
            }
        }

        public static string RawGet(string uri, WebHeaderCollection headers) {
            Log.Info($"Executing {uri} with headers {headers}");
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.Proxy = null;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Headers = headers;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream)) {
                return reader.ReadToEnd();
            }
        }

        public static string Get(string uri, WebHeaderCollection headers) {
            try {
                Log.Info($"Executing {uri} with headers {headers}");
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
                request.Proxy = null;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                request.Headers = headers;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream)) {
                    return reader.ReadToEnd();
                }
            } catch (Exception e) {
                Log.Error($"Error happened during GET {uri}");
                return "";
                //throw;
            }
        }

        public static string Post(string uri, string data, string contentType = "application/x-www-form-urlencoded", string method = "POST") {
            try {
                byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
                request.Proxy = null;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                request.ContentLength = dataBytes.Length;
                request.ContentType = contentType;
                request.Method = method;

                using (Stream requestBody = request.GetRequestStream()) {
                    requestBody.Write(dataBytes, 0, dataBytes.Length);
                }

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream)) {
                    return reader.ReadToEnd();
                }
            } catch (Exception) {
                Log.Error($"Error happened during POST {uri}");
                return "";
            }
        }
    }
}

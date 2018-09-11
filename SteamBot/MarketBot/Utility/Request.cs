using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Utility {
    public static class Request {

        public static HttpClient httpClient = new HttpClient();

        public static async Task<string> NewGet(string uri) {
            
            return await (await httpClient.GetAsync(uri)).Content.ReadAsStringAsync();
        }

        public static string Get(string uri)
        {
            var temp = NewGet(uri);
            temp.Wait();
            return temp.Result;
        }

        public static string Post(string uri, string data, string contentType = "application/x-www-form-urlencoded", string method = "POST") {
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
        }
    }
}

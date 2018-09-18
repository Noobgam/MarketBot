using CSGOTM;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace MarketBot.Server {
    public class Core : IDisposable {
        private HttpListener server;

        public Core() {
            server = new HttpListener();
            CurSizes = new Dictionary<string, int>();
            server.Prefixes.Add(Consts.Endpoints.localhost + Consts.Endpoints.GetBestToken);
            server.Prefixes.Add(Consts.Endpoints.localhost + Consts.Endpoints.PutCurrentInventory);
            Console.Error.WriteLine("Starting!");
            server.Start();
            Console.Error.WriteLine("Started!");
            Task.Run((Action)Listen);
            JObject temp;
            try {
                temp = (JObject)LocalRequest.RawGet(Consts.Endpoints.GetBestToken, "grim2");
                Console.WriteLine(temp.ToString());
            } catch {
                Console.Error.WriteLine("Could not get a response from local server");
            }
        }

        private void Listen() {
            while (!disposed) {
                ThreadPool.QueueUserWorkItem(Process, server.GetContext());
            }
        }

        private Dictionary<string, int> CurSizes;

        private void Process(object o) {
            var context = o as HttpListenerContext;
            JObject resp = null;
            try {
                if (context.Request.RawUrl == Consts.Endpoints.GetBestToken) {
                    KeyValuePair<string, int> kv = CurSizes.OrderBy(t => t.Value)
                            .FirstOrDefault();
                    if (kv.Key == null) {
                        throw new Exception("Don't know about bot inventory sizes");
                    }
                    resp = new JObject {
                        ["success"] = true,
                        ["token"] = Consts.TokenCache[kv.Key],
                        ["botname"] = kv.Key,
                        ["dictionary"] = JToken.FromObject(CurSizes)
                    };
                } else if (context.Request.RawUrl == Consts.Endpoints.PutCurrentInventory) {
                    string[] usernames = context.Request.Headers.GetValues("botname");
                    if (usernames.Length != 1) {
                        throw new Exception($"You have to provide 1 username, {usernames.Length} were provided");
                    }
                    string[] data = context.Request.Headers.GetValues("data");
                    if (data.Length != 1) {
                        throw new Exception($"You have to provide 1 data, {data.Length} were provided");
                    }
                    CurSizes[usernames[0]] = int.Parse(data[0]);
                }
            } catch (Exception ex) {
                resp = new JObject {
                    ["success"] = false,
                    ["error"] = ex.Message
                };

            } finally {
                if (resp == null)
                    resp = new JObject {
                        ["success"] = false,
                        ["error"] = "Unsupported request"
                    };
                Respond(context, resp);
            }
        }

        // process request and make response

        private void Respond(HttpListenerContext ctx, JObject json) {
            //TODO(noobgam): prettify if User-Agent is defined
            string resp = json.ToString(
                ctx.Request.Headers.GetValues("User-Agent") == null
                ? Newtonsoft.Json.Formatting.None
                : Newtonsoft.Json.Formatting.Indented);
            byte[] buffer = Encoding.UTF8.GetBytes(resp);
            HttpListenerResponse response = ctx.Response;

            response.ContentLength64 = buffer.Length;
            Stream output = response.OutputStream;
            output.Write(buffer, 0, buffer.Length);

            output.Close();
        }

        public void Stop() {
            Dispose(true);
        }

        #region IDisposable Support
        private bool disposed = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing) {
            if (!disposed) {
                if (disposing) {
                    if (server.IsListening)
                        server.Stop();
                }

                disposed = true;
            }
        }

        public void Dispose() {
            Dispose(true);
        }
        #endregion
    }
}

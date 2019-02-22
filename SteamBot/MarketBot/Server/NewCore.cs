using Newtonsoft.Json.Linq;
using SteamBot.MarketBot.CS.Bot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Utility.VK;

namespace Server
{
    public class NewCore
    {
        private HttpListener httpListener;
        private readonly string localhost;
        private Dictionary<string, Tuple<object, System.Reflection.MethodInfo>> containerStorage;
        private NewMarketLogger logger;
        private Task listener;
        private CancellationTokenSource tokenSource;
        private CancellationToken ct;
        private int ReqId = 1;

        public NewCore(int port)
        {
            localhost = "http://+:" + port;
            logger = new NewMarketLogger("NewCore");
            httpListener = new HttpListener();
            containerStorage = new Dictionary<string, Tuple<object, System.Reflection.MethodInfo>>();
        }

        public void Initialize()
        {
            logger.Info("Initializing core");
            InitializeEndpoints();
            httpListener.Start();
            VK.Init();
            logger.Success("Started serving");
            tokenSource = new CancellationTokenSource();
            ct = tokenSource.Token;
            listener = Task.Factory.StartNew(Listen, ct);
        }

        ~NewCore()
        {
            if (listener != null)
            {
                if (listener.Status == TaskStatus.Running)
                {
                    tokenSource.Cancel();
                }
            }
        }

        private void Listen()
        {
            while (true)
            {
                try
                {
                    ThreadPool.QueueUserWorkItem(Process, httpListener.GetContext());
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }

        private readonly JObject unknownError = new JObject
        {
            ["success"] = false,
            ["exception"] = "Unknown error"
        };

        private void Process(object o)
        {
            HttpListenerContext context = o as HttpListenerContext;
            if (context == null)
            {
                logger.Warn("Process was called with something other than HttpListenerContext.");
                return;
            }
            string Endpoint = context.Request.Url.AbsolutePath;
            Stopwatch sw = new Stopwatch();
            int requestid = ReqId++;
            try
            {
                logger.Info($"[Request {requestid}] Executing {context.Request.Url}");
                sw.Start();
                if (!containerStorage.TryGetValue(Endpoint, out var c_m))
                {
                    Respond(
                        context,
                        new JObject
                        {
                            ["success"] = false,
                            ["error"] = "Unrecognized endpoint"
                        });
                    return;
                }
                var container = c_m.Item1;
                var method = c_m.Item2;
                if (method.ReturnType == typeof(JObject))
                {
                    var PathParams =
                        method
                            .GetParameters()
                            .Where(param => 
                                param.GetCustomAttributes(typeof(PathParam), false).Length == 1 ||
                                param.ParameterType == typeof(HttpListenerContext)
                             );
                    string queryString = context.Request.Url.Query;
                    var queryDictionary = System.Web.HttpUtility.ParseQueryString(queryString);
                    var ParsedParams = PathParams.Select(param => {
                        if (param.ParameterType == typeof(HttpListenerContext))
                        {
                            // not really cool I know, but nothing I can do about it now.
                            return context;
                        }
                        string path = ((PathParam)param.GetCustomAttributes(typeof(PathParam), false)[0]).path ?? param.Name;
                        string val = queryDictionary[path];
                        if (val == null)
                        {
                            string[] data = context.Request.Headers.GetValues(path) ?? new string[0];
                            if (data.Length == 0)
                            {
                                if (param.HasDefaultValue)
                                {
                                    return param.DefaultValue;
                                }
                                throw new ArgumentException($"Required parameter {path} is not present.");
                            }
                            val = data[0];
                        }
                        return ParseParam(val, param.ParameterType);
                    }).ToArray();
                    Respond(context, method, container, ParsedParams);
                    return;
                }
            }
            catch (ArgumentException aex)
            {
                try
                {
                    Respond(context, new JObject
                    {
                        ["success"] = false,
                        ["error"] = aex.Message
                    });
                }
                catch (Exception ex2)
                {
                    logger.Error($"[Reqeust {requestid}] Could not respond. {ex2}");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"[Request {requestid}] Error occured during {context.Request.Url}. {ex}");
                try
                {
                    Respond(context, unknownError);
                }
                catch (Exception ex2)
                {
                    logger.Error($"[Reqeust {requestid}] Could not respond. {ex2}");
                }
            }
            finally
            {
                sw.Stop();
                logger.Info($"[Request {requestid}] took {sw.ElapsedMilliseconds}ms");
            }
        }

        private void Respond(HttpListenerContext context, System.Reflection.MethodInfo method, object container, object[] ParsedParams) {
            try {
                Respond(context, (JObject)method.Invoke(container, ParsedParams));
            } catch (System.Reflection.TargetInvocationException ex) {
                var aex = ex.InnerException as ArgumentException;
                var eex = ex.InnerException as Exception;
                if (aex != null) {
                    Respond(context, new JObject {
                        ["success"] = false,
                        ["error"] = aex.Message
                    });
                } else {
                    Respond(context, new JObject {
                        ["success"] = false,
                        ["exception"] = eex.Message,
                        ["trace"] = eex.Message
                    });
                }
            } catch (Exception ex) {
                logger.Crash($"Respond threw an exception. {ex.Message}. Trace: {ex.StackTrace}");
            }
        }

        private void Respond(HttpListenerContext ctx, JObject json)
        {
            if ((bool)json["success"] != true)
            {
                if (json["exception"] != null)
                {
                    ctx.Response.StatusCode = 500;
                }
                if (json["error"] != null)
                {
                    ctx.Response.StatusCode = 400;
                }
            }
            else
            {
                ctx.Response.StatusCode = 200;
            }
            string resp = json.ToString(
                ctx.Request.Headers.GetValues("User-Agent") == null
                ? Newtonsoft.Json.Formatting.None
                : Newtonsoft.Json.Formatting.Indented);
            RawRespond(ctx, resp);
        }

        private void RawRespond(HttpListenerContext ctx, string resp)
        {
            string[] acceptEncoding = new string[0];
            try
            {
                acceptEncoding = ctx.Request.Headers["Accept-Encoding"].Split(',').Select(s => s.Trim()).ToArray();
            } catch
            {

            }
            byte[] buffer = Encoding.UTF8.GetBytes(resp);
            if (acceptEncoding.Contains("gzip"))
            {
                ctx.Response.AddHeader("Content-Encoding", "gzip");

                var varByteStream = new MemoryStream(buffer);

                GZipStream refGZipStream = new GZipStream(ctx.Response.OutputStream, CompressionMode.Compress, false);

                varByteStream.CopyTo(refGZipStream);
                refGZipStream.Close();
            }
            else
            {
                HttpListenerResponse response = ctx.Response;

                response.ContentLength64 = buffer.Length;
                Stream output = response.OutputStream;
                output.Write(buffer, 0, buffer.Length);

                output.Close();
            }
        }

        private object ParseParam(string value, Type type)
        {
            if (type == typeof(int))
            {
                return int.Parse(value);
            }
            else if (type == typeof(long))
            {
                return long.Parse(value);
            }
            else if (type == typeof(string))
            {
                return value;
            }
            else if (type == typeof(List<int>))
            {
                return value.Split(',').Select(x => int.Parse(x)).ToList();
            }
            else if (type == typeof(bool))
            {
                return bool.Parse(value);
            }
            else if (type == typeof(double))
            {
                return double.Parse(value);
            }
            else
            {
                throw new ArgumentException($"Unrecognized type {type}");
            }
        }

        private void InitializeEndpoints()
        {
            var root = typeof(ApiEndpointContainer);
            var containers = AppDomain.CurrentDomain.GetAssemblies()
                 .SelectMany(s => s.GetTypes())
                 .Where(p => root.IsAssignableFrom(p) && !p.IsAbstract);
            foreach (var container in containers)
            {
                var methods = container.GetMethods();
                var containerInstance = Activator.CreateInstance(container);
                string containerPrefix = "";
                if (container.GetCustomAttributes(typeof(ApiContainer), false).Length > 0)
                {
                    containerPrefix = ((ApiContainer)container.GetCustomAttributes(typeof(ApiContainer), false)[0]).path;
                }
                foreach (var method in methods)
                {
                    var endpoints = method.GetCustomAttributes(typeof(ApiEndpoint), false);
                    if (endpoints.Length == 0)
                        continue;
                    if (method.IsPrivate)
                    {
                        throw new ArgumentException("Api methods must be marked public.");
                    }
                    if (method.ReturnType != typeof(JObject))
                    {
                        throw new ArgumentException("Api methods with return types other than jobject are not supported.");
                    }
                    if (method.GetParameters().Any(param => param.GetCustomAttributes(typeof(PathParam), false).Length > 1))
                    {
                        throw new ArgumentException("Api parameters should not have duped [PathParam] attribute.");
                    }
                    if (method.GetParameters().Any(param => param.ParameterType != typeof(HttpListenerContext) && param.GetCustomAttributes(typeof(PathParam), false).Length != 1))
                    {
                        throw new ArgumentException("Api endpoints with non-[PathParam] attribute are not allowed.");
                    }
                    string endpoint = containerPrefix + ((ApiEndpoint)endpoints[0]).path;
                    httpListener.Prefixes.Add(localhost + endpoint);
                    containerStorage.Add(endpoint,
                        new Tuple<object, System.Reflection.MethodInfo>(containerInstance, method));
                    Console.WriteLine(endpoint);
                }
            }
        }
    }
}
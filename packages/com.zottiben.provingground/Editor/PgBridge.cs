using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using ProvingGround.Actuation;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// A local HTTP endpoint that lets an agent harness call Proving Ground inside a live
    /// Editor, including during play mode.
    ///
    /// Off by default and bound to the loopback interface only. It executes named methods
    /// on <see cref="PgApi"/> and nothing else: there is no arbitrary code execution here,
    /// because a port that can run any C# in your Editor is a different and much larger
    /// thing to be responsible for.
    /// </summary>
    [InitializeOnLoad]
    public static class PgBridge
    {
        public const string EnabledKey = "ProvingGround.Bridge.Enabled";
        public const string PortKey = "ProvingGround.Bridge.Port";
        const string ServingKey = "ProvingGround.Bridge.Serving";
        const string ShutdownRouteKey = "ProvingGround.Bridge.AllowShutdown";
        const string SessionPortKey = "ProvingGround.Bridge.SessionPort";
        public const int DefaultPort = 8787;

        static HttpListener _listener;
        static Thread _thread;
        static readonly ConcurrentQueue<Action> MainThread = new ConcurrentQueue<Action>();

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, false);
            set
            {
                EditorPrefs.SetBool(EnabledKey, value);
                if (value) Start();
                else Stop();
            }
        }

        public static int Port
        {
            // Serve mode keeps its port in SessionState so a headless run on a custom port
            // does not rewrite the port the user's Editor will use next time.
            get => Serving
                ? SessionState.GetInt(SessionPortKey, DefaultPort)
                : EditorPrefs.GetInt(PortKey, DefaultPort);
            set
            {
                if (Serving) SessionState.SetInt(SessionPortKey, value);
                else EditorPrefs.SetInt(PortKey, value);
            }
        }

        public static bool IsRunning => _listener != null && _listener.IsListening;

        /// <summary>
        /// Enables POST /shutdown. Only set by the headless serve entry point: an Editor a
        /// person is working in should not be closable from a socket.
        /// </summary>
        public static bool AllowShutdownRoute
        {
            get => SessionState.GetBool(ShutdownRouteKey, false);
            set => SessionState.SetBool(ShutdownRouteKey, value);
        }

        /// <summary>
        /// Headless serve mode, remembered across domain reloads but not across Editor
        /// restarts.
        ///
        /// Compiling a script reloads the app domain, which aborts the listener thread and
        /// wipes every static field. The GUI path recovers because Enabled lives in
        /// EditorPrefs, but serve mode must not write EditorPrefs - that would silently
        /// leave the bridge switched on in the user's own Editor afterwards. SessionState
        /// has exactly the right lifetime.
        /// </summary>
        public static bool Serving
        {
            get => SessionState.GetBool(ServingKey, false);
            set => SessionState.SetBool(ServingKey, value);
        }

        static PgBridge()
        {
            EditorApplication.update += PumpMainThread;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;

            // Restart after a domain reload for either reason it might have been running.
            if (Enabled || Serving) EditorApplication.delayCall += Start;
        }

        [MenuItem("Tools/Proving Ground/Agent Bridge/Enable", priority = 120)]
        static void EnableBridge() => Enabled = true;

        [MenuItem("Tools/Proving Ground/Agent Bridge/Enable", validate = true)]
        static bool EnableBridgeValidate() => !Enabled;

        [MenuItem("Tools/Proving Ground/Agent Bridge/Disable", priority = 121)]
        static void DisableBridge() => Enabled = false;

        [MenuItem("Tools/Proving Ground/Agent Bridge/Disable", validate = true)]
        static bool DisableBridgeValidate() => Enabled;

        public static void Start()
        {
            if (IsRunning) return;

            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Start();

                _thread = new Thread(Listen) { IsBackground = true, Name = "ProvingGround.Bridge" };
                _thread.Start();

                Debug.Log($"[ProvingGround] Agent bridge listening on http://127.0.0.1:{Port}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ProvingGround] Could not start the agent bridge on port {Port}: {e.Message}");
                _listener = null;
            }
        }

        public static void Stop()
        {
            if (_listener == null) return;

            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch (Exception)
            {
                // Shutting down a listener that has already faulted is not worth reporting.
            }

            _listener = null;
            _thread = null;
        }

        static void Listen()
        {
            while (_listener != null && _listener.IsListening)
            {
                try
                {
                    var context = _listener.GetContext();
                    Handle(context);
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ProvingGround] Bridge error: {e.Message}");
                }
            }
        }

        static void Handle(HttpListenerContext context)
        {
            var path = context.Request.Url.AbsolutePath.Trim('/').ToLowerInvariant();

            // /methods is pure reflection over PgApi and touches no Unity API, so it is the
            // only route that may be answered from the listener thread.
            if (path == "methods")
            {
                Respond(context, 200, JsonConvert.SerializeObject(Methods().Select(Describe)));
                return;
            }

            if (path == "health")
            {
                RespondFromMainThread(context, Health);
                return;
            }

            // GET as well as POST: HttpListener rejects a bodyless POST with 411 before the
            // request ever reaches here, which makes `curl -X POST /shutdown` fail in a way
            // that looks like the route is broken.
            if (path == "shutdown" && AllowShutdownRoute)
            {
                Respond(context, 200, "{\"ok\":true,\"shuttingDown\":true}");
                MainThread.Enqueue(() => EditorApplication.Exit(0));
                return;
            }

            if (path != "call" || context.Request.HttpMethod != "POST")
            {
                Respond(context, 404, "{\"ok\":false,\"error\":\"Use POST /call, GET /health or GET /methods.\"}");
                return;
            }

            string body;
            using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                body = reader.ReadToEnd();

            RespondFromMainThread(context, () => Invoke(body));
        }

        /// <summary>
        /// Runs <paramref name="work"/> on the Editor's main thread and answers the request
        /// with whatever it returns.
        ///
        /// Every Unity API, down to Application.unityVersion, throws when called from a
        /// background thread. Answering directly from the listener thread therefore fails
        /// silently: the exception is swallowed by the accept loop and the caller simply
        /// waits until it gives up.
        /// </summary>
        static void RespondFromMainThread(HttpListenerContext context, Func<string> work)
        {
            var done = new ManualResetEventSlim(false);
            string result = null;
            string error = null;

            MainThread.Enqueue(() =>
            {
                try
                {
                    result = work();
                }
                catch (Exception e)
                {
                    error = e.InnerException?.Message ?? e.Message;
                }
                finally
                {
                    done.Set();
                }
            });

            if (!done.Wait(TimeSpan.FromMinutes(10)))
            {
                Respond(context, 504, "{\"ok\":false,\"error\":\"The Editor did not respond within ten minutes.\"}");
                return;
            }

            if (error != null)
            {
                Respond(context, 500, JsonConvert.SerializeObject(new { ok = false, error }));
                return;
            }

            Respond(context, 200, result ?? "{\"ok\":true}");
        }

        static string Health() => JsonConvert.SerializeObject(new
        {
            ok = true,
            unity = Application.unityVersion,
            project = Application.productName,
            isPlaying = EditorApplication.isPlaying,
            isCompiling = EditorApplication.isCompiling,
            initialised = PgSetup.IsInitialised
        });

        static string Invoke(string body)
        {
            var request = JObject.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var name = request["method"]?.ToString();

            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Pass a 'method' name. GET /methods lists them.");

            return InvokeMethod(name, request["args"] as JObject ?? new JObject());
        }

        /// <summary>
        /// Dispatches to a named <see cref="PgApi"/> method. Shared with
        /// <see cref="PgApi.Batch"/> so a batched call and a direct call resolve and bind
        /// arguments identically.
        /// </summary>
        public static string InvokeMethod(string name, JObject args)
        {
            var method = Methods().FirstOrDefault(m =>
                string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

            if (method == null)
                throw new ArgumentException(
                    $"Unknown method '{name}'. Known: {string.Join(", ", Methods().Select(m => m.Name))}");

            args ??= new JObject();
            var parameters = method.GetParameters();
            var values = new object[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var token = args[parameter.Name] ??
                            args.Properties().FirstOrDefault(p =>
                                string.Equals(p.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))?.Value;

                if (token != null && token.Type != JTokenType.Null)
                    values[i] = token.ToObject(parameter.ParameterType);
                else if (parameter.HasDefaultValue)
                    values[i] = parameter.DefaultValue;
                else
                    throw new ArgumentException($"'{parameter.Name}' is required by {method.Name}.");
            }

            var returned = method.Invoke(null, values);
            return returned as string ?? JsonConvert.SerializeObject(new { ok = true, result = returned });
        }

        static IEnumerable<MethodInfo> Methods() =>
            typeof(PgApi).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(PgApi));

        static object Describe(MethodInfo method) => new
        {
            name = method.Name,
            parameters = method.GetParameters().Select(p => new
            {
                name = p.Name,
                type = p.ParameterType.Name,
                required = !p.HasDefaultValue,
                defaultValue = p.HasDefaultValue ? p.DefaultValue?.ToString() : null
            })
        };

        static void PumpMainThread()
        {
            // Bounded per tick so a burst of requests cannot stall the Editor's own update.
            for (var i = 0; i < 4 && MainThread.TryDequeue(out var action); i++)
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ProvingGround] Bridge call failed: {e.Message}");
                }
            }
        }

        static void Respond(HttpListenerContext context, int status, string body)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                context.Response.OutputStream.Close();
            }
            catch (Exception)
            {
                // The client disconnecting mid-response is normal and not worth logging.
            }
        }
    }
}

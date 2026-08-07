using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using Newtonsoft.Json.Linq;
using ProvingGround.EditorTools;

namespace ProvingGround.Tests
{
    /// <summary>
    /// Exercises the agent bridge over a real socket. Mocking the transport here would
    /// prove nothing: the thing worth testing is that an external process can reach the
    /// Editor and get an answer back.
    /// </summary>
    public class PgBridgeTests
    {
        const int TestPort = 8799;

        // One listener for the whole fixture. Starting and stopping it around every test
        // churns the port faster than the OS releases it, which shows up as a request that
        // connects to a socket nobody is accepting on any more.
        static readonly HttpClient Client = new HttpClient { Timeout = System.TimeSpan.FromSeconds(20) };
        static bool _wasEnabled;
        static int _previousPort;
        static string _previousDataHome;
        static string _sandbox;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _wasEnabled = PgBridge.Enabled;
            _previousPort = PgBridge.Port;

            // Publish into a sandbox: these tests run on a machine that may have real
            // Editors registered, and pointing an agent at a test port would be a nasty
            // way to find out that the suite had run.
            _previousDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            _sandbox = Path.Combine(Path.GetTempPath(), "pg-bridge-tests-" + Guid.NewGuid().ToString("n"));
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", _sandbox);

            PgBridge.Stop();
            PgBridge.Port = TestPort;
            PgBridge.Start();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            PgBridge.Shutdown();
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", _previousDataHome);

            try
            {
                if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing the run over.
            }

            PgBridge.Port = _previousPort;
            if (_wasEnabled) PgBridge.Start();
        }

        [Test]
        public void TheBridgeBindsItsPort()
        {
            Assert.IsTrue(PgBridge.IsRunning, "the bridge did not start listening");
        }

        /// <summary>
        /// The port is not always the default, and a client that assumes it is reports a
        /// running Editor as unreachable. Writing the address down is what makes it
        /// findable.
        /// </summary>
        [Test]
        public void StartingPublishesTheAddressItIsListeningOn()
        {
            Assert.IsTrue(File.Exists(PgBridgeRegistry.EntryPath),
                $"no entry was published at {PgBridgeRegistry.EntryPath}");

            var entry = JObject.Parse(File.ReadAllText(PgBridgeRegistry.EntryPath));
            Assert.AreEqual(TestPort, entry["port"].Value<int>());
            Assert.AreEqual($"http://127.0.0.1:{TestPort}", entry["url"].Value<string>());
        }

        [Test]
        public void ThePublishedAddressNamesItsProject()
        {
            var entry = JObject.Parse(File.ReadAllText(PgBridgeRegistry.EntryPath));
            var recorded = entry["project"].Value<string>();

            Assert.AreEqual(
                Path.GetFullPath(PgPaths.ProjectRoot).Replace('\\', '/').TrimEnd('/'),
                recorded,
                "a client tells one Editor from another by project path");
            Assert.Greater(entry["pid"].Value<int>(), 0);
        }

        /// <summary>
        /// A compile reloads the domain, which tears the listener down and rebuilds it on
        /// the same port. A client polling across that window must keep the address, or it
        /// falls back to the default port and never reconnects.
        /// </summary>
        [Test]
        public void StoppingForADomainReloadKeepsTheAddress()
        {
            PgBridge.Stop();
            try
            {
                Assert.IsTrue(File.Exists(PgBridgeRegistry.EntryPath),
                    "the address was withdrawn during a reload the bridge comes back from");
            }
            finally
            {
                PgBridge.Start();
            }
        }

        /// <summary>
        /// The shutdown route is checked on the listener thread, where reading the flag used
        /// to throw; the accept loop swallowed it, so POST /shutdown answered nothing and
        /// the caller hung. Calling the route here would exit the Editor mid-run, so this
        /// covers the read that was actually broken.
        /// </summary>
        [UnityTest]
        public IEnumerator TheShutdownFlagIsReadableFromTheListenerThread()
        {
            var previous = PgBridge.AllowShutdownRoute;
            PgBridge.AllowShutdownRoute = true;

            bool observed = false;
            Exception failure = null;
            var reader = new Thread(() =>
            {
                try { observed = PgBridge.AllowShutdownRoute; }
                catch (Exception e) { failure = e; }
            });

            reader.Start();
            while (reader.IsAlive) yield return null;

            PgBridge.AllowShutdownRoute = previous;

            Assert.IsNull(failure, failure?.Message);
            Assert.IsTrue(observed, "the listener thread could not see the flag");
        }

        [Test]
        public void ShuttingDownWithdrawsTheAddress()
        {
            PgBridge.Shutdown();
            try
            {
                Assert.IsFalse(File.Exists(PgBridgeRegistry.EntryPath),
                    "a bridge that is gone for good must not leave clients an address");
            }
            finally
            {
                PgBridge.Start();
            }
        }

        [UnityTest]
        public IEnumerator HealthReportsTheEditorState()
        {
            var task = Client.GetStringAsync($"http://127.0.0.1:{TestPort}/health");
            while (!task.IsCompleted) yield return null;

            Assert.IsFalse(task.IsFaulted, task.Exception?.GetBaseException().Message);

            var json = JObject.Parse(task.Result);
            Assert.IsTrue(json["ok"].Value<bool>());
            Assert.IsNotEmpty(json["unity"].Value<string>());
        }

        [UnityTest]
        public IEnumerator MethodsListsTheCallableSurface()
        {
            var task = Client.GetStringAsync($"http://127.0.0.1:{TestPort}/methods");
            while (!task.IsCompleted) yield return null;

            Assert.IsFalse(task.IsFaulted, task.Exception?.GetBaseException().Message);

            var methods = JArray.Parse(task.Result);
            var names = methods.Select(m => m["name"].Value<string>()).ToList();

            CollectionAssert.Contains(names, "CheckProject");
            CollectionAssert.Contains(names, "RunScenario");
            CollectionAssert.Contains(names, "Digest");
        }

        [UnityTest]
        public IEnumerator CallRunsARealCheckAndReturnsItsReport()
        {
            var task = Post(new JObject { ["method"] = "CheckProject" });
            while (!task.IsCompleted) yield return null;

            Assert.IsFalse(task.IsFaulted, task.Exception?.GetBaseException().Message);

            var report = JObject.Parse(task.Result);
            Assert.AreEqual("project", report["tool"].Value<string>());
            Assert.IsNotNull(report["findings"]);
        }

        [UnityTest]
        public IEnumerator AnUnknownMethodIsRejectedWithTheAvailableNames()
        {
            var task = PostRaw(new JObject { ["method"] = "DefinitelyNotAMethod" });
            while (!task.IsCompleted) yield return null;

            var response = task.Result;
            Assert.AreEqual(500, (int)response.StatusCode);

            var bodyTask = response.Content.ReadAsStringAsync();
            while (!bodyTask.IsCompleted) yield return null;

            StringAssert.Contains("Unknown method", bodyTask.Result);
        }

        [UnityTest]
        public IEnumerator ArgumentsAreBoundToNamedParameters()
        {
            var task = Post(new JObject
            {
                ["method"] = "Norms",
                ["args"] = new JObject { ["genre"] = "platformer" }
            });

            while (!task.IsCompleted) yield return null;

            Assert.IsFalse(task.IsFaulted, task.Exception?.GetBaseException().Message);
            StringAssert.Contains("jump.coyoteTime", task.Result);
        }

        static Task<string> Post(JObject body)
        {
            var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
            return Client.PostAsync($"http://127.0.0.1:{TestPort}/call", content)
                .ContinueWith(t => t.Result.Content.ReadAsStringAsync().Result);
        }

        static Task<HttpResponseMessage> PostRaw(JObject body)
        {
            var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
            return Client.PostAsync($"http://127.0.0.1:{TestPort}/call", content);
        }
    }
}

using System.Collections;
using System.Linq;
using System.Net.Http;
using System.Text;
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

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _wasEnabled = PgBridge.Enabled;
            _previousPort = PgBridge.Port;
            PgBridge.Stop();
            PgBridge.Port = TestPort;
            PgBridge.Start();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            PgBridge.Stop();
            PgBridge.Port = _previousPort;
            if (_wasEnabled) PgBridge.Start();
        }

        [Test]
        public void TheBridgeBindsItsPort()
        {
            Assert.IsTrue(PgBridge.IsRunning, "the bridge did not start listening");
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

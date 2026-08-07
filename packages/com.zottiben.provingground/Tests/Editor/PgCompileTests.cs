using System.Collections;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using ProvingGround.EditorTools;
using UnityEditor;
using UnityEngine.TestTools;

namespace ProvingGround.Tests
{
    /// <summary>
    /// Covers the signal every caller polls to know when it may proceed.
    ///
    /// Getting this wrong is expensive in a way that is hard to diagnose: a caller waiting
    /// on a status that never settles cannot tell an Editor that is busy from one that is
    /// idle, and reports a healthy Editor as hung.
    /// </summary>
    public class PgCompileTests
    {
        [TearDown]
        public void TearDown() => PgCompile.Reset();

        /// <summary>
        /// Refreshing when no script has changed compiles nothing, so the generation never
        /// moves. Waiting for it to move meant `settled` stayed false against a completely
        /// idle Editor, and every caller polled until it timed out.
        /// </summary>
        [UnityTest]
        public IEnumerator ARefreshWithNothingToRebuildSettles()
        {
            PgCompile.Reset();
            PgCompile.Request();

            // A rebuild starts synchronously inside the refresh, so a few ticks is plenty
            // for anything real to have shown up as busy.
            for (var i = 0; i < 10 && PgCompile.IsBusy; i++) yield return null;

            var status = JObject.FromObject(PgCompile.Status());

            Assert.IsFalse(status["isCompiling"].Value<bool>(), "nothing should have been compiling");
            Assert.IsTrue(status["settled"].Value<bool>(),
                "an idle Editor with nothing to rebuild never reported settled, " +
                "so a caller polls until it gives up");
        }

        [Test]
        public void AnUnrequestedStatusIsSettledWhenIdle()
        {
            PgCompile.Reset();

            var status = JObject.FromObject(PgCompile.Status());

            Assert.AreEqual(-1, status["requestedAt"].Value<int>());
            Assert.AreEqual(!EditorApplication.isCompiling && !EditorApplication.isUpdating,
                status["settled"].Value<bool>());
        }

        [Test]
        public void ResetClearsTheOutstandingRequest()
        {
            PgCompile.Request();
            PgCompile.Reset();

            var status = JObject.FromObject(PgCompile.Status());
            Assert.AreEqual(-1, status["requestedAt"].Value<int>());
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace ProvingGround.EditorTools
{
    /// <summary>One compiler message, reduced to what a caller needs to fix it.</summary>
    [Serializable]
    public sealed class PgCompileMessage
    {
        [JsonProperty("file")] public string File;
        [JsonProperty("line")] public int Line;
        [JsonProperty("column")] public int Column;
        [JsonProperty("message")] public string Message;
        [JsonProperty("assembly")] public string Assembly;

        public override string ToString() => $"{File}({Line},{Column}): {Message}";
    }

    /// <summary>
    /// Tracks compilation so a caller can wait for it properly instead of sleeping.
    ///
    /// This is the single most-reported friction in every other Unity agent bridge: after
    /// editing a script there is no way to know when the Editor has finished rebuilding,
    /// so agents insert fixed sleeps that are simultaneously too long and not long enough.
    ///
    /// The awkward part is that compiling reloads the app domain, which destroys every
    /// static field and drops the agent's connection. State is therefore kept in
    /// SessionState, which survives the reload, and a monotonically increasing generation
    /// number lets a caller tell "still compiling the thing I asked for" from "finished and
    /// came back".
    /// </summary>
    [InitializeOnLoad]
    public static class PgCompile
    {
        const string GenerationKey = "ProvingGround.Compile.Generation";
        const string ErrorsKey = "ProvingGround.Compile.Errors";
        const string RequestedKey = "ProvingGround.Compile.Requested";

        static readonly List<PgCompileMessage> Pending = new List<PgCompileMessage>();

        // Statics, not SessionState: they only describe a request that is still in flight,
        // and a domain reload can only happen because the compile they were tracking
        // started - at which point the generation answers the question instead.
        static bool _requestOutstanding;
        static bool _compileStarted;
        static bool _forcedCompile;

        static PgCompile()
        {
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;

            // An import that turns out to need no rebuild leaves the request unresolved,
            // and nothing else will ever come along to close it.
            EditorApplication.update += ResolveSatisfiedRequest;
        }

        /// <summary>Increments every time a compilation completes. Survives domain reload.</summary>
        public static int Generation => SessionState.GetInt(GenerationKey, 0);

        /// <summary>True while Unity is compiling or importing.</summary>
        public static bool IsBusy => EditorApplication.isCompiling || EditorApplication.isUpdating;

        /// <summary>Errors from the most recent compilation.</summary>
        public static List<PgCompileMessage> Errors
        {
            get
            {
                var json = SessionState.GetString(ErrorsKey, "[]");
                try
                {
                    return JsonConvert.DeserializeObject<List<PgCompileMessage>>(json) ??
                           new List<PgCompileMessage>();
                }
                catch (Exception)
                {
                    return new List<PgCompileMessage>();
                }
            }
        }

        /// <summary>
        /// Asks Unity to import changed files and rebuild. Records the generation at the
        /// time of the request so a caller can tell when its own compile has landed.
        /// </summary>
        public static int Request(bool forceRecompile = false)
        {
            var before = Generation;
            SessionState.SetInt(RequestedKey, before);

            _requestOutstanding = true;
            _compileStarted = false;
            _forcedCompile = forceRecompile;

            AssetDatabase.Refresh(ImportAssetOptions.Default);
            if (forceRecompile) CompilationPipeline.RequestScriptCompilation();

            ResolveSatisfiedRequest();
            return before;
        }

        /// <summary>
        /// Closes a request that no compilation is ever going to satisfy.
        ///
        /// A refresh imports and starts any rebuild synchronously, so an Editor that is idle
        /// afterwards, having started nothing, had nothing to rebuild. Left open, the
        /// request waits on a generation bump that is not coming: `settled` stays false
        /// against a perfectly healthy Editor and every caller polls until it times out,
        /// which reads exactly like Unity has hung.
        ///
        /// Also runs on the update loop, for the case where the import is still draining
        /// when the refresh returns and no compile follows it.
        /// </summary>
        static void ResolveSatisfiedRequest()
        {
            if (!_requestOutstanding) return;
            if (_forcedCompile || _compileStarted || IsBusy) return;

            SessionState.SetInt(RequestedKey, -1);
            _requestOutstanding = false;
        }

        /// <summary>
        /// The state a caller polls. <c>settled</c> means it is safe to proceed: nothing is
        /// compiling, and any requested compile has completed.
        /// </summary>
        public static object Status()
        {
            var requested = SessionState.GetInt(RequestedKey, -1);
            var generation = Generation;
            var errors = Errors;

            // A requested compile is done when the generation has moved past the value
            // captured at request time. When nothing was requested, being idle is enough.
            var settled = !IsBusy && (requested < 0 || generation > requested);

            return new
            {
                settled,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                generation,
                requestedAt = requested,
                hasErrors = errors.Count > 0,
                errorCount = errors.Count,
                errors = errors.Take(25).Select(e => e.ToString()).ToList()
            };
        }

        /// <summary>Clears the recorded compile state, so a fresh request starts clean.</summary>
        public static void Reset()
        {
            SessionState.SetString(ErrorsKey, "[]");
            SessionState.SetInt(RequestedKey, -1);
            _requestOutstanding = false;
            _compileStarted = false;
            _forcedCompile = false;
            Pending.Clear();
        }

        static void OnCompilationStarted(object context)
        {
            _compileStarted = true;
            Pending.Clear();
        }

        static void OnAssemblyFinished(string assemblyPath, CompilerMessage[] messages)
        {
            foreach (var message in messages)
            {
                if (message.type != CompilerMessageType.Error) continue;

                Pending.Add(new PgCompileMessage
                {
                    File = message.file,
                    Line = message.line,
                    Column = message.column,
                    Message = message.message,
                    Assembly = System.IO.Path.GetFileNameWithoutExtension(assemblyPath)
                });
            }
        }

        static void OnCompilationFinished(object context)
        {
            SessionState.SetString(ErrorsKey, JsonConvert.SerializeObject(Pending));
            SessionState.SetInt(GenerationKey, Generation + 1);
            _requestOutstanding = false;
            Pending.Clear();
        }
    }
}

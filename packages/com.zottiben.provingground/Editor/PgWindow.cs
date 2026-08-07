using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ProvingGround.Actuation;
using ProvingGround.Verification;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// The Editor front end. Deliberately a thin shell over <see cref="PgApi"/>: everything
    /// here is a button that calls the same method an agent calls, so a person and an agent
    /// never disagree about what a check does.
    /// </summary>
    public sealed class PgWindow : EditorWindow
    {
        Vector2 _scroll;
        PgReport _report;
        string _selectedScenario;
        string _recordingName = "recorded";
        float _probeSeconds = 30f;
        PgSeverity _minSeverity = PgSeverity.Info;

        public static void Open()
        {
            var window = GetWindow<PgWindow>("Proving Ground");
            window.minSize = new Vector2(420, 480);
            window.Show();
        }

        void OnEnable() => LoadLatestReport();

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawStatus();
            EditorGUILayout.Space(6);
            DrawSetup();
            EditorGUILayout.Space(6);
            DrawChecks();
            EditorGUILayout.Space(6);
            DrawPlayMode();
            EditorGUILayout.Space(6);
            DrawPerception();
            EditorGUILayout.Space(10);
            DrawReport();

            EditorGUILayout.EndScrollView();
        }

        void DrawStatus()
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);

            if (!PgSetup.IsInitialised)
            {
                EditorGUILayout.HelpBox(
                    "This project has not been initialised. Initialise writes starter contracts to " +
                    "ProvingGround/ next to Assets. Nothing is added to the asset database.",
                    MessageType.Info);
                return;
            }

            var contracts = Directory.Exists(PgPaths.Contracts)
                ? Directory.GetFiles(PgPaths.Contracts, "*.json").Length
                : 0;
            var scenarios = PgScenario.All().Count();

            EditorGUILayout.LabelField($"Contracts: {contracts}    Scenarios: {scenarios}");
            EditorGUILayout.LabelField($"Input backend: {PgInput.BackendName}");
            EditorGUILayout.LabelField($"UI collectors: {(PgUi.All.Count == 0 ? "none registered (enter play mode)" : string.Join(", ", PgUi.All.Select(c => c.Name)))}");
        }

        void DrawSetup()
        {
            EditorGUILayout.LabelField("Setup", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Initialise Project")) Run(PgSetup.Initialise());
                if (GUILayout.Button("Design Docs")) { PgApi.InitProcess(); LoadLatestReport(); }
                if (GUILayout.Button("Survey")) Run(PgBaseline.Survey());
            }
        }

        void DrawChecks()
        {
            EditorGUILayout.LabelField("Checks (edit mode)", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Project")) Run(PgProjectAudit.Run());
                if (GUILayout.Button("Content")) Run(PgContentAudit.Run());
                if (GUILayout.Button("Audio assets")) Run(PgAudioAssetCheck.Run());
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scene truth")) Run(PgSceneTruth.Analyze());
                if (GUILayout.Button("UI conformance")) Run(PgUiConformance.Check());
                if (GUILayout.Button("Quality gate"))
                {
                    PgApi.Gate();
                    _report = PgJson.Read<PgReport>(PgPaths.Report("gate"));
                }
            }

            EditorGUILayout.LabelField("Milestones", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var milestone in PgProcess.Load().Take(4))
                    if (GUILayout.Button(milestone.Id, EditorStyles.miniButton))
                        Run(PgProcess.Evaluate(milestone.Id));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                foreach (var milestone in PgProcess.Load().Skip(4))
                    if (GUILayout.Button(milestone.Id, EditorStyles.miniButton))
                        Run(PgProcess.Evaluate(milestone.Id));
            }
        }

        void DrawPlayMode()
        {
            EditorGUILayout.LabelField("Play mode", EditorStyles.boldLabel);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Scenarios, the probe bot and baseline capture drive the running game, so they need play mode.",
                    MessageType.None);

                if (GUILayout.Button("Enter Play Mode")) EditorApplication.EnterPlaymode();
                return;
            }

            if (PgRunner.IsBusy)
            {
                EditorGUILayout.HelpBox("A run is in progress.", MessageType.Info);
                Repaint();
                return;
            }

            var scenarios = PgScenario.All().Select(Path.GetFileNameWithoutExtension).ToArray();

            if (scenarios.Length > 0)
            {
                var index = Mathf.Max(0, System.Array.IndexOf(scenarios, _selectedScenario));
                index = EditorGUILayout.Popup("Scenario", index, scenarios);
                _selectedScenario = scenarios[index];

                if (GUILayout.Button("Run scenario"))
                {
                    var scenario = PgScenario.LoadByName(_selectedScenario);
                    if (scenario == null) Debug.LogError($"[ProvingGround] Could not load '{_selectedScenario}'.");
                    else PgRunner.Play(scenario, report => { _report = report; PgApi.Emit(report); Repaint(); });
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No scenarios defined. Initialise the project to get a starter.", MessageType.None);
            }

            _probeSeconds = EditorGUILayout.Slider("Probe seconds", _probeSeconds, 5f, 300f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Run probe bot"))
                    PgRunner.Probe(_probeSeconds, 12345, report => { _report = report; PgApi.Emit(report); Repaint(); });

                if (GUILayout.Button("Capture baseline")) Run(PgBaseline.Capture(LastFeelMetrics()));
            }

            if (GUILayout.Button("Watch audio (infer events from AudioSources)"))
            {
                PgAudio.Watch();
                Debug.Log("[ProvingGround] Audio watcher attached. Play through the systems you want captured.");
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Record a session", EditorStyles.miniBoldLabel);

            if (!PgRecording.IsAvailable)
            {
                EditorGUILayout.HelpBox("Recording needs com.unity.inputsystem.", MessageType.None);
                return;
            }

            if (PgRecording.IsRecording)
            {
                _recordingName = EditorGUILayout.TextField("Save as", _recordingName);
                if (GUILayout.Button("Stop and save")) Run(PgJson.Read<PgReport>(SaveRecording()));
                Repaint();
            }
            else if (GUILayout.Button("Start recording"))
            {
                PgRecording.Start();
                Debug.Log("[ProvingGround] Recording. Play until the thing you want to reproduce happens.");
            }
        }

        void DrawPerception()
        {
            EditorGUILayout.LabelField("Perception", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scene digest")) Debug.Log(PgApi.Digest());
                if (GUILayout.Button("Camera view")) Debug.Log(PgApi.View());
                if (GUILayout.Button("Annotated capture")) Debug.Log(PgApi.Capture());
                if (GUILayout.Button("Events")) Debug.Log(PgApi.Events());
            }
        }

        void DrawReport()
        {
            EditorGUILayout.LabelField("Last report", EditorStyles.boldLabel);

            if (_report == null)
            {
                EditorGUILayout.HelpBox("Run a check to see findings here.", MessageType.None);
                return;
            }

            _report.Summarise();

            var type = !_report.Passed ? MessageType.Error
                : _report.CountAtLeast(PgSeverity.Warn) > 0 ? MessageType.Warning
                : MessageType.Info;
            EditorGUILayout.HelpBox(_report.Summary, type);

            _minSeverity = (PgSeverity)EditorGUILayout.EnumPopup("Show at least", _minSeverity);

            foreach (var finding in _report.Findings
                         .Where(f => f.Severity >= _minSeverity)
                         .OrderByDescending(f => f.Severity)
                         .Take(200))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"[{finding.Severity}] {finding.Id}", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(finding.Message, EditorStyles.wordWrappedLabel);

                    if (!string.IsNullOrEmpty(finding.Subject))
                        EditorGUILayout.LabelField("at " + finding.Subject, EditorStyles.miniLabel);

                    if (finding.Expected != null || finding.Actual != null)
                        EditorGUILayout.LabelField($"expected {finding.Expected}, got {finding.Actual}",
                            EditorStyles.miniLabel);

                    if (!string.IsNullOrEmpty(finding.Remedy))
                        EditorGUILayout.LabelField(finding.Remedy, EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        static System.Collections.Generic.Dictionary<string, double> LastFeelMetrics()
        {
            var report = PgRunner.LastReport;
            var metrics = new System.Collections.Generic.Dictionary<string, double>();
            if (report?.Data == null) return metrics;

            foreach (var pair in report.Data)
            {
                if (!pair.Key.StartsWith("feel.")) continue;
                if (double.TryParse(pair.Value?.ToString(), out var value))
                    metrics[pair.Key.Substring("feel.".Length)] = value;
            }

            return metrics;
        }

        void Run(PgReport report)
        {
            if (report == null) return;
            _report = report;
            PgApi.Emit(report);
            Repaint();
        }

        /// <summary>Stops the recording and returns the path of the report it wrote.</summary>
        string SaveRecording()
        {
            PgApi.StopRecording(_recordingName);
            return PgPaths.Report("recording");
        }

        void LoadLatestReport()
        {
            var directory = Path.Combine(PgPaths.Artifacts, "reports");
            if (!Directory.Exists(directory)) return;

            var latest = Directory.GetFiles(directory, "*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (latest != null) _report = PgJson.Read<PgReport>(latest);
        }
    }
}

using System;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Facet.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Facet.Game
{
    /// <summary>Metrics 3/4 observer, sampled after every tick so task/wave transitions are not lost
    /// when several simulation ticks happen in one rendered frame.</summary>
    public sealed class ProgressMetricsReporter : MonoBehaviour
    {
        private readonly ProgressMetricsState _state = new ProgressMetricsState();
        private readonly string _session = Guid.NewGuid().ToString("N").Substring(0, 10);
        private SimWorld _world;
        private long _readEvents;
        private int _lastTick;
        private bool _restartPending;
        private bool _finished;
        private float _lastHp;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void FacetAnalyticsPost(string url, string body);
#endif

        private void Awake()
        {
            _state.TaskRecorded += Report;
            _state.WaveRecorded += Report;
        }

        // Called only in a playable frame, after menu/pause handling. Speed gear does not scale time.
        public void BeginFrame(SimWorld world, float activeSeconds)
        {
            if (_restartPending) return;
            EnsureRun(world);
            if (world.Status == GameStatus.Playing && !world.Paused)
                _state.AddActiveTime(activeSeconds);
        }

        public void ExpectRestart() { _restartPending = true; }

        public void AfterTick(SimWorld world, bool restarted)
        {
            if (restarted || (_restartPending && world.TickCount == 0))
            {
                _state.Finish(_lastHp, "restarted");
                _world = null;
                _restartPending = false;
            }
            EnsureRun(world);
            if (_finished) return;
            _state.ObserveTutorial(world.Tutorial.StepsCompleted, world.Tutorial.Active);
            ReadEvents(world);
            _lastTick = world.TickCount;
            _lastHp = world.Core.Hp;
            if (world.Status != GameStatus.Playing)
            {
                _state.Finish(world.Core.Hp, world.Status == GameStatus.Won ? "won" : "lost");
                _finished = true;
            }
        }

        public void FinishRun(string outcome)
        {
            if (_world != null) _state.Finish(_world.Core.Hp, outcome);
            _world = null;
            _restartPending = false;
        }

        private void EnsureRun(SimWorld world)
        {
            if (world == null) return;
            if (ReferenceEquals(world, _world) && world.TickCount >= _lastTick) return;
            if (_world != null) _state.Finish(_lastHp, "map_changed");
            _world = world;
            _finished = false;
            _lastTick = world.TickCount;
            _lastHp = world.Core.Hp;
            _readEvents = (long)world.Events.Dropped + world.Events.Count;
            var tasks = new MetricTaskDefinition[world.Tutorial.Count];
            for (int i = 0; i < tasks.Length; i++)
            {
                TutorialTask task = world.Tutorial.TaskAt(i);
                string label = string.IsNullOrEmpty(task.Title) ? task.Text : task.Title;
                tasks[i] = new MetricTaskDefinition(task.Step.ToString(), label, IsKeyTask(task.Step));
            }
            _state.Begin(world.Map.Name, world.Core.MaxHp, tasks,
                world.Tutorial.StepsCompleted, world.Tutorial.Active);
            // A normal run is observed before its first tick. A mid-wave attach is explicitly partial.
            if (world.Waves.CurrentWave > 0)
                _state.StartWave(world.Waves.CurrentWave, world.Core.Hp, false);
        }

        public static bool IsKeyTask(TutorialStep step)
        {
            if (!Enum.IsDefined(typeof(TutorialStep), step)) return false;
            switch (step)
            {
                case TutorialStep.Acknowledge:
                case TutorialStep.SelectDrill:
                case TutorialStep.SelectBelt:
                case TutorialStep.SelectTurret:
                case TutorialStep.SelectCutter:
                case TutorialStep.SelectRotator:
                case TutorialStep.StartWave:
                case TutorialStep.ClearWave:
                    return false;
                default: return true;
            }
        }

        private void ReadEvents(SimWorld world)
        {
            SimEventBuffer events = world.Events;
            long end = (long)events.Dropped + events.Count;
            bool gap = _readEvents < events.Dropped;
            if (gap) _state.NoteEventGap();
            for (long sequence = Math.Max(_readEvents, events.Dropped); sequence < end; sequence++)
            {
                SimEvent e = events[(int)(sequence - events.Dropped)];
                if (e.Kind == SimEventKind.WaveStarted)
                    _state.StartWave((int)e.Amount, world.Core.Hp, !gap);
                else if (e.Kind == SimEventKind.WaveCleared)
                    _state.ClearWave((int)e.Amount, world.Core.Hp, world.Core.Alive);
            }
            // If a start was dropped, capture only the remaining portion and flag it.
            if (gap && world.Waves.CurrentWave > 0)
                _state.StartWave(world.Waves.CurrentWave, world.Core.Hp, false);
            _readEvents = end;
        }

        private void Report(TutorialTaskMetricRow row)
        {
            string[] values = { _session, row.Run, row.Map, row.TaskIndex.ToString(Invariant),
                row.TaskKind, row.TaskLabel, row.Seconds.ToString("0.000", Invariant),
                row.Outcome, ProgressMetricsConfig.BuildLabel };
            Submit("3", ProgressMetricsConfig.TaskFormUrl, ProgressMetricsConfig.TaskEntries,
                values, JsonUtility.ToJson(row));
        }

        private void Report(WaveCoreMetricRow row)
        {
            string[] values = { _session, row.Run, row.Map, row.Wave.ToString(Invariant),
                row.StartHp.ToString("0.###", Invariant), row.EndHp.ToString("0.###", Invariant),
                row.LostHp.ToString("0.###", Invariant), row.MaxHp.ToString("0.###", Invariant),
                row.LostPercentOfMax.ToString("0.###", Invariant), row.Outcome,
                row.CompleteCoverage ? "true" : "false", ProgressMetricsConfig.BuildLabel };
            Submit("4", ProgressMetricsConfig.CoreFormUrl, ProgressMetricsConfig.CoreEntries,
                values, JsonUtility.ToJson(row));
        }

        private void Submit(string metric, string url, string[] entries, string[] values, string log)
        {
            bool configured = !string.IsNullOrWhiteSpace(url) && entries.Length == values.Length;
            for (int i = 0; configured && i < entries.Length; i++)
                configured = !string.IsNullOrWhiteSpace(entries[i]) && entries[i].StartsWith("entry.");
            if (!configured || (Application.isEditor && !ProgressMetricsConfig.SendFromEditor))
            {
                Debug.Log("FACET Metric " + metric + " (local only): " + log, this);
                return;
            }
            var body = new StringBuilder();
            for (int i = 0; i < entries.Length; i++)
            {
                if (i > 0) body.Append('&');
                body.Append(entries[i]).Append('=').Append(UnityWebRequest.EscapeURL(values[i] ?? ""));
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            FacetAnalyticsPost(url, body.ToString());
#else
            StartCoroutine(Post(url, body.ToString()));
#endif
        }

        private static IEnumerator Post(string url, string body)
        {
            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                    Debug.LogWarning("FACET Metrics 3/4 submission failed: " + request.error);
            }
        }

        private void OnApplicationQuit() { FinishRun("quit"); }
        private void OnDestroy() { FinishRun("closed"); }
    }
}

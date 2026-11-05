using System;

namespace Facet.Game
{
    [Serializable]
    public sealed class TutorialTaskMetricRow
    {
        public string Run, Map, TaskKind, TaskLabel, Outcome;
        public int TaskIndex;
        public double Seconds;
    }

    [Serializable]
    public sealed class WaveCoreMetricRow
    {
        public string Run, Map, Outcome;
        public int Wave;
        public float StartHp, EndHp, LostHp, MaxHp, LostPercentOfMax;
        public bool CompleteCoverage;
    }

    public readonly struct MetricTaskDefinition
    {
        public readonly string Kind, Label;
        public readonly bool Include;
        public MetricTaskDefinition(string kind, string label, bool include)
        { Kind = kind; Label = label; Include = include; }
    }

    /// <summary>Read-only analytics state. All time is active real seconds supplied by the driver,
    /// never simulation ticks. No reference here can change the world or its tutorial.</summary>
    public sealed class ProgressMetricsState
    {
        public event Action<TutorialTaskMetricRow> TaskRecorded;
        public event Action<WaveCoreMetricRow> WaveRecorded;
        public bool Running { get; private set; }
        private string _run, _map;
        private MetricTaskDefinition[] _tasks;
        private int _taskIndex, _wave;
        private double _seconds;
        private float _waveStart, _maxHp;
        private bool _taskOpen, _coverage;

        public void Begin(string map, float maxHp, MetricTaskDefinition[] tasks,
            int completed, bool tutorialActive)
        {
            if (Running) throw new InvalidOperationException("Finish the previous metrics run first.");
            _run = Guid.NewGuid().ToString("N").Substring(0, 12);
            _map = map;
            _maxHp = Math.Max(0f, maxHp);
            _tasks = tasks ?? Array.Empty<MetricTaskDefinition>();
            _wave = 0;
            Running = true;
            OpenTask(completed, tutorialActive);
        }

        public void AddActiveTime(double seconds)
        {
            if (Running && _taskOpen && !double.IsNaN(seconds) && !double.IsInfinity(seconds))
                _seconds += Math.Max(0d, seconds);
        }

        public void ObserveTutorial(int completed, bool active)
        {
            if (!Running) return;
            if (!active)
            {
                CloseTask("skipped");
                return;
            }
            if (completed == _taskIndex) return;
            // Normal sampling is once per tick, so a transition is exactly one step. If an external
            // caller jumps ahead, do not invent precise durations for steps we did not observe.
            CloseTask(completed == _taskIndex + 1 ? "completed" : "timing_unavailable");
            OpenTask(completed, true);
        }

        public void StartWave(int wave, float hp, bool fullCoverage = true)
        {
            if (!Running || wave <= 0 || _wave == wave) return;
            if (_wave > 0) CloseWave(hp, "boundary_missing");
            _wave = wave;
            _waveStart = ClampHp(hp);
            _coverage = fullCoverage;
        }

        public void NoteEventGap() { if (_wave > 0) _coverage = false; }

        public void ClearWave(int wave, float hp, bool coreAlive)
        {
            if (_wave != wave || !Running) return;
            CloseWave(hp, coreAlive ? "cleared" : "lost");
        }

        public void Finish(float hp, string outcome)
        {
            if (!Running) return;
            CloseTask(outcome);
            CloseWave(hp, outcome);
            Running = false;
        }

        private void OpenTask(int index, bool active)
        {
            _taskIndex = index;
            _seconds = 0d;
            _taskOpen = active && index >= 0 && index < _tasks.Length && _tasks[index].Include;
        }

        private void CloseTask(string outcome)
        {
            if (!_taskOpen) return;
            _taskOpen = false;
            MetricTaskDefinition task = _tasks[_taskIndex];
            TaskRecorded?.Invoke(new TutorialTaskMetricRow {
                Run = _run, Map = _map, TaskIndex = _taskIndex + 1, TaskKind = task.Kind,
                TaskLabel = task.Label, Seconds = _seconds, Outcome = outcome
            });
        }

        private float ClampHp(float hp) => Math.Max(0f, Math.Min(_maxHp, hp));

        private void CloseWave(float hp, string outcome)
        {
            if (_wave <= 0) return;
            float end = ClampHp(hp);
            float lost = Math.Max(0f, _waveStart - end);
            var row = new WaveCoreMetricRow {
                Run = _run, Map = _map, Wave = _wave, StartHp = _waveStart, EndHp = end,
                LostHp = lost, MaxHp = _maxHp,
                LostPercentOfMax = _maxHp > 0f ? 100f * lost / _maxHp : 0f,
                Outcome = outcome, CompleteCoverage = _coverage
            };
            _wave = 0;
            WaveRecorded?.Invoke(row);
        }
    }
}

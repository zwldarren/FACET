using System.Collections.Generic;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;

namespace Facet.Tests
{
    public sealed class ProgressMetricsTests
    {
        private ProgressMetricsState _state;
        private List<TutorialTaskMetricRow> _tasks;
        private List<WaveCoreMetricRow> _waves;

        [SetUp]
        public void SetUp()
        {
            _state = new ProgressMetricsState();
            _tasks = new List<TutorialTaskMetricRow>();
            _waves = new List<WaveCoreMetricRow>();
            _state.TaskRecorded += _tasks.Add;
            _state.WaveRecorded += _waves.Add;
        }

        private void Begin()
        {
            _state.Begin("Test map", 100f, new[] {
                new MetricTaskDefinition("Acknowledge", "Read", false),
                new MetricTaskDefinition("ArmTurret", "Supply", true),
                new MetricTaskDefinition("MakeHalf", "Cut", true)
            }, 0, true);
        }

        [Test]
        public void ExplanationsAreExcludedAndTasksHaveSeparateClocks()
        {
            Begin();
            _state.AddActiveTime(50);
            _state.ObserveTutorial(1, true);
            _state.AddActiveTime(12.5);
            _state.ObserveTutorial(2, true);
            _state.AddActiveTime(4);
            _state.ObserveTutorial(3, true);
            Assert.That(_tasks.Count, Is.EqualTo(2));
            Assert.That(_tasks[0].TaskKind, Is.EqualTo("ArmTurret"));
            Assert.That(_tasks[0].TaskIndex, Is.EqualTo(2));
            Assert.That(_tasks[0].Seconds, Is.EqualTo(12.5));
            Assert.That(_tasks[1].Seconds, Is.EqualTo(4));
            Assert.That(_tasks[1].Outcome, Is.EqualTo("completed"));
        }

        [Test]
        public void PauseDoesNotAdvanceWhenNoActiveTimeIsFedAndPollingDoesNotDuplicate()
        {
            Begin();
            _state.ObserveTutorial(1, true);
            _state.AddActiveTime(3);
            for (int i = 0; i < 200; i++) _state.ObserveTutorial(1, true);
            _state.AddActiveTime(2);
            _state.ObserveTutorial(2, true);
            _state.ObserveTutorial(2, true);
            Assert.That(_tasks.Count, Is.EqualTo(1));
            Assert.That(_tasks[0].Seconds, Is.EqualTo(5));
        }

        [Test]
        public void SkippingAndLeavingAreNotSuccessfulCompletions()
        {
            Begin();
            _state.ObserveTutorial(1, true);
            _state.AddActiveTime(10);
            _state.ObserveTutorial(1, false);
            _state.Finish(100, "left_map");
            Assert.That(_tasks.Count, Is.EqualTo(1));
            Assert.That(_tasks[0].Outcome, Is.EqualTo("skipped"));
        }

        [Test]
        public void AbandonedTaskHasElapsedTimeAndDoesNotCountAsCompleted()
        {
            Begin();
            _state.ObserveTutorial(1, true);
            _state.AddActiveTime(7);
            _state.Finish(100, "left_map");
            _state.Finish(100, "closed");
            Assert.That(_tasks.Count, Is.EqualTo(1));
            Assert.That(_tasks[0].Outcome, Is.EqualTo("left_map"));
            Assert.That(_tasks[0].Seconds, Is.EqualTo(7));
        }

        [Test]
        public void MissingTaskSamplesAreNotInventedAsCompletedDurations()
        {
            Begin();
            _state.ObserveTutorial(1, true);
            _state.ObserveTutorial(3, true);
            Assert.That(_tasks.Count, Is.EqualTo(1));
            Assert.That(_tasks[0].Outcome, Is.EqualTo("timing_unavailable"));
        }

        [Test]
        public void RestartGetsNewRunAndResetsTiming()
        {
            Begin();
            _state.ObserveTutorial(1, true);
            _state.AddActiveTime(20);
            _state.Finish(100, "restarted");
            Begin();
            _state.ObserveTutorial(1, true);
            _state.AddActiveTime(2);
            _state.ObserveTutorial(2, true);
            Assert.That(_tasks[0].Run, Is.Not.EqualTo(_tasks[1].Run));
            Assert.That(_tasks[1].Seconds, Is.EqualTo(2));
        }

        [Test]
        public void ActualWaveNumbersAndZeroDamageAreRecordedWithoutFixedWaveCount()
        {
            Begin();
            _state.StartWave(5, 80);
            _state.ClearWave(5, 65, true);
            _state.StartWave(6, 65);
            _state.ClearWave(6, 65, true);
            _state.ClearWave(6, 65, true);
            Assert.That(_waves.Count, Is.EqualTo(2));
            Assert.That(_waves[0].Wave, Is.EqualTo(5));
            Assert.That(_waves[0].LostHp, Is.EqualTo(15));
            Assert.That(_waves[0].LostPercentOfMax, Is.EqualTo(15));
            Assert.That(_waves[1].LostHp, Is.Zero);
        }

        [Test]
        public void LethalOverkillIsCappedAndClearOnLethalTickIsLost()
        {
            Begin();
            _state.StartWave(1, 30);
            _state.ClearWave(1, -20, false);
            _state.Finish(-20, "lost");
            Assert.That(_waves.Count, Is.EqualTo(1));
            Assert.That(_waves[0].EndHp, Is.Zero);
            Assert.That(_waves[0].LostHp, Is.EqualTo(30));
            Assert.That(_waves[0].Outcome, Is.EqualTo("lost"));
        }

        [Test]
        public void AbandonedWaveAndEventGapRemainIdentifiable()
        {
            Begin();
            _state.StartWave(2, 90);
            _state.NoteEventGap();
            _state.Finish(70, "left_map");
            Assert.That(_waves[0].Outcome, Is.EqualTo("left_map"));
            Assert.That(_waves[0].CompleteCoverage, Is.False);
        }

        [Test]
        public void MidWaveAttachIsPartialAndNoWaveIsInventedBeforeAStart()
        {
            Begin();
            _state.ClearWave(1, 80, true);
            Assert.That(_waves, Is.Empty);
            _state.StartWave(3, 80, false);
            _state.ClearWave(3, 75, true);
            Assert.That(_waves[0].CompleteCoverage, Is.False);
        }

        [TestCase(TutorialStep.Acknowledge, false)]
        [TestCase(TutorialStep.SelectTurret, false)]
        [TestCase(TutorialStep.StartWave, false)]
        [TestCase(TutorialStep.ClearWave, false)]
        [TestCase(TutorialStep.ArmTurret, true)]
        [TestCase(TutorialStep.MakeHalf, true)]
        [TestCase(TutorialStep.BeltFork, true)]
        public void OnlyOperationalTutorialTasksAreIncluded(TutorialStep step, bool expected)
            => Assert.That(ProgressMetricsReporter.IsKeyTask(step), Is.EqualTo(expected));
    }
}

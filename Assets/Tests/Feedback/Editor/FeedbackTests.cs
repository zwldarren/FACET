using Facet.Core;
using Facet.Game;
using NUnit.Framework;

namespace Facet.Tests
{
    public sealed class FeedbackTests
    {
        private const int AllKinds = -1;

        [Test]
        public void SameTickEventsAreCapturedExactlyOnce()
        {
            var events = new SimEventBuffer();
            var history = new FeedbackEvents();
            history.Reset(events);
            events.Tick = 70;
            events.EnemyDamaged(1, EnemyKind.Runner, new Vec2(1, 2), 4);
            events.EnemyDamaged(2, EnemyKind.Runner, new Vec2(3, 4), 2);
            history.Capture(events, 2.5f, AllKinds, true);
            history.Capture(events, 2.6f, AllKinds, true);
            Assert.That(history.Count, Is.EqualTo(2));
            Assert.That(history[0].Event.EnemyId, Is.EqualTo(2));
            Assert.That(history[1].Event.EnemyId, Is.EqualTo(1));
            Assert.That(history[0].Seconds, Is.EqualTo(2.5f));
            Assert.That(events.Count, Is.EqualTo(2), "The view must not clear the source.");
        }

        [Test]
        public void WrappedSourceAndBoundedHistoryKeepNewestEvents()
        {
            var events = new SimEventBuffer(3);
            var history = new FeedbackEvents(2);
            history.Reset(events);
            for (int i = 0; i < 6; i++) events.Built(new Int2(i, 1), i);
            history.Capture(events, 1f, AllKinds, true);
            Assert.That(history.Count, Is.EqualTo(2));
            Assert.That(history[0].Event.Amount, Is.EqualTo(5f));
            Assert.That(history[1].Event.Amount, Is.EqualTo(4f));
            events.Built(new Int2(6, 1), 6);
            history.Capture(events, 2f, AllKinds, true);
            Assert.That(history[0].Event.Amount, Is.EqualTo(6f));
            Assert.That(history[1].Event.Amount, Is.EqualTo(5f));
        }

        [Test]
        public void DisableConsumesEventsAndDoesNotReplayThemWhenEnabled()
        {
            var events = new SimEventBuffer();
            var history = new FeedbackEvents();
            history.Reset(events);
            events.Built(new Int2(1, 1), 1);
            history.Capture(events, 1f, AllKinds, true);
            events.Built(new Int2(2, 1), 1);
            history.Capture(events, 2f, AllKinds, false);
            Assert.That(history.Count, Is.Zero);
            history.Capture(events, 3f, AllKinds, true);
            Assert.That(history.Count, Is.Zero);
        }

        [Test]
        public void RebindDropsOldEventsAndFilterKeepsOnlyRequestedKinds()
        {
            var first = new SimEventBuffer();
            var second = new SimEventBuffer();
            var history = new FeedbackEvents();
            history.Reset(first);
            first.CoreDamaged(5);
            history.Capture(first, 1f, AllKinds, true);
            second.CoreDamaged(10);
            history.Reset(second);
            history.Capture(second, 2f, AllKinds, true);
            Assert.That(history.Count, Is.Zero);
            second.Built(new Int2(0, 1), 1);
            second.CoreDamaged(15);
            history.Capture(second, 3f, 1 << (int)SimEventKind.CoreDamaged, true);
            Assert.That(history.Count, Is.EqualTo(1));
            Assert.That(history[0].Event.Amount, Is.EqualTo(15f));
        }

        [Test]
        public void SameWorldRestartExplicitlyResetsTheReadCursor()
        {
            var events = new SimEventBuffer();
            var history = new FeedbackEvents();
            history.Reset(events);
            events.Built(new Int2(1, 1), 1);
            history.Capture(events, 1f, AllKinds, true);
            events.Clear();
            history.Reset(events); // Driver rebinds views on a run restart.
            events.Built(new Int2(2, 2), 2);
            history.Capture(events, 2f, AllKinds, true);
            Assert.That(history.Count, Is.EqualTo(1));
            Assert.That(history[0].Event.Cell, Is.EqualTo(new Int2(2, 2)));
        }

        [TestCase(8f, 8f, 2)]
        [TestCase(4f, 8f, 1)]
        [TestCase(3f, 8f, 0)]
        [TestCase(4f, 0f, 0)]
        public void DamageMarksFollowBalanceAndCustomBaseDamage(float amount, float baseDamage, int marks)
            => Assert.That(ViewFeedback.DamageMarks(amount, baseDamage), Is.EqualTo(marks));

        [Test]
        public void EasingSettlesAndReducedMotionDisablesTheAddedEffects()
        {
            Assert.That(ViewFeedback.EaseOutBack(0f), Is.EqualTo(0f).Within(0.00001f));
            Assert.That(ViewFeedback.EaseOutBack(1f), Is.EqualTo(1f));
            Assert.That(ViewFeedback.EaseOutBack(0.7f), Is.GreaterThan(1f));
            Assert.That(ViewFeedback.EaseOutQuad(0f), Is.Zero);
            Assert.That(ViewFeedback.EaseOutQuad(1f), Is.EqualTo(1f));
            var look = new Palette.FeedbackLook();
            Assert.That(look.Active, Is.True);
            look.ReducedMotion = true;
            Assert.That(look.Active, Is.False);
            look.ReducedMotion = false;
            look.Enabled = false;
            Assert.That(look.Active, Is.False);
        }

        [Test]
        public void ReadingEffectsOnOrOffPreservesTheSameSimulationRun()
        {
            Shape round = Shape.Full(Quad.Circle);
            var wave = new WaveDefinition(0f,
                new SpawnGroup(EnemyKind.Runner, 4, 0, 0f, 0.5f, round));
            var map = new MapDefinition("Feedback test", 16, 12, 100,
                new ShapePatch[0], new[] { new Int2(0, 6) }, new[] { wave }, seed: 37);
            var a = new SimWorld(map, new SimConfig());
            var b = new SimWorld(map, new SimConfig());
            var on = new FeedbackEvents();
            var off = new FeedbackEvents();
            on.Reset(a.Events);
            off.Reset(b.Events);
            a.RequestNextWave();
            b.RequestNextWave();
            for (int tick = 0; tick < 360; tick++)
            {
                a.Tick(InputCommand.None);
                b.Tick(InputCommand.None);
                if (tick % 60 == 20 && a.Enemies.AliveCount > 0)
                {
                    int id = a.Enemies.EnemyAt(0).Id;
                    a.Enemies.ApplyDamage(id, 4, round);
                    b.Enemies.ApplyDamage(id, 4, round);
                }
                on.Capture(a.Events, tick * 0.02f, AllKinds, true);
                off.Capture(b.Events, tick * 0.05f, AllKinds, false);
                Assert.That(a.TickCount, Is.EqualTo(b.TickCount));
                Assert.That(a.Core, Is.EqualTo(b.Core));
                Assert.That(a.Status, Is.EqualTo(b.Status));
                Assert.That(a.Economy.Shapes, Is.EqualTo(b.Economy.Shapes));
                Assert.That(a.Enemies.AliveCount, Is.EqualTo(b.Enemies.AliveCount));
                for (int i = 0; i < a.Enemies.AliveCount; i++)
                    Assert.That(a.Enemies.EnemyAt(i), Is.EqualTo(b.Enemies.EnemyAt(i)));
                Assert.That(a.Events.Count, Is.EqualTo(b.Events.Count));
                Assert.That(a.Events.Dropped, Is.EqualTo(b.Events.Dropped));
                for (int i = 0; i < a.Events.Count; i++)
                    Assert.That(a.Events[i], Is.EqualTo(b.Events[i]));
            }
        }
    }
}

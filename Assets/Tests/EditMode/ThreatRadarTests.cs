using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The out-of-view locator's geometry, pinned without a camera, resolution or scene. Its rules are each
    /// one a player could notice broken: what is out of view (under a card or off the display), where the arrow
    /// goes (on the sight line, at the window edge, never under a card), when two enemies fold into one arrow
    /// (a wave in a door), which face an arrow wears (the nearest), and that a full array still counts true.
    /// A viewport here is a rectangle and two panel boxes, and a wave a list of snapshots - the same interface
    /// <see cref="ThreatRadarView"/> and <see cref="HudView"/> both ask, which is the point.
    /// </summary>
    public class ThreatRadarTests
    {
        private static readonly Vector2 Centre = new Vector2(20f, 20f);

        /// <summary>The markers' clearance from the window's edge and from any card's, in world units.</summary>
        private const float Margin = 0.5f;

        /// <summary>A 20-tile-square window centred on (20,20), so the screen runs (10,10)-(30,30), with two
        /// cards over it: a status card in the top-left corner and a build bar along the whole bottom. Those are
        /// the real HUD's shapes - one panel owning a corner, one an edge - a pair a single inset-per-side number cannot tell apart.</summary>
        private static readonly Rect[] Panels =
        {
            new Rect(10f, 22f, 4f, 8f),      // status card: x 10..14, y 22..30
            new Rect(10f, 10f, 20f, 3f),     // build bar: the whole bottom, y 10..13
        };

        private static readonly ThreatRadar.Viewport Screen =
            new ThreatRadar.Viewport(Centre, new Vector2(10f, 10f), Margin, Panels, Panels.Length);

        private static readonly EnemySnapshot[] NoEnemies = new EnemySnapshot[0];
        private static readonly Int2[] NoJams = new Int2[0];

        [Test]
        public void OutOfView_IsTheWindowMinusTheCards_NotTheCardsStrips()
        {
            Assert.IsTrue(Screen.IsOpen(Centre), "the point the camera is looking at is open");
            Assert.IsTrue(Screen.IsOpen(new Vector2(12f, 15f)),
                "the ground below the status card is free, and a walker there is perfectly findable");
            Assert.IsTrue(Screen.IsOpen(new Vector2(20f, 21f)), "and so is the ground above the build bar");

            Assert.IsFalse(Screen.IsOpen(new Vector2(12f, 26f)), "a walker under the status card is not");
            Assert.IsFalse(Screen.IsOpen(new Vector2(20f, 11f)), "nor one under the build bar");
            Assert.IsFalse(Screen.IsOpen(new Vector2(35f, 20f)), "and of course not one off the window");
        }

        /// <summary>A window smaller than the clearance its markers ask for collapses the ring onto the middle
        /// rather than turning it inside out - a left edge right of its own right edge reads as "nothing is ever visible".</summary>
        [Test]
        public void AWindowSmallerThanItsOwnMargin_CollapsesInsteadOfTurningInsideOut()
        {
            var squeezed = new ThreatRadar.Viewport(Centre, new Vector2(1f, 1f), 9f, null, 0);

            Assert.LessOrEqual(squeezed.RingLeft, Centre.x);
            Assert.GreaterOrEqual(squeezed.RingRight, Centre.x);
            Assert.AreEqual(Centre.x, squeezed.RingLeft, 1e-4f, "the ring collapses onto the centre, not past it");
            Assert.AreEqual(Centre.y, squeezed.RingBottom, 1e-4f);
            Assert.IsTrue(squeezed.IsOpen(Centre), "and the middle is still somewhere the player can look");
        }

        /// <summary>An arrow sits where the line from the camera through the subject crosses the window edge -
        /// the edge it leaves through, on the sight line, and nowhere else.</summary>
        [Test]
        public void AnArrowSitsOnTheEdgeItsSightLineLeavesThrough()
        {
            AssertEdge(new Vector2(40f, 20f), 29.5f, 20f);       // straight out the right edge
            AssertEdge(new Vector2(20f, 40f), 20f, 29.5f);       // straight out the top
            AssertEdge(new Vector2(0f, 20f), 10.5f, 20f);        // straight out the left, below the card

            void AssertEdge(Vector2 at, float edgeX, float edgeY)
            {
                int arrows = ThreatRadar.Collect(Screen, new[] { Enemy(EnemyKind.Runner, at.x, at.y) },
                    NoJams, Centre, 1f, Markers(4, out ThreatRadar.Marker[] into), out _);

                Assert.AreEqual(1, arrows, "one enemy, one arrow, at " + at);
                Assert.AreEqual(edgeX, into[0].At.x, 1e-3f, "the arrow's x for a subject at " + at);
                Assert.AreEqual(edgeY, into[0].At.y, 1e-3f, "the arrow's y for a subject at " + at);
                Assert.IsFalse(Screen.Covers(into[0].At), "and it is not under a card");

                Vector2 d = into[0].Direction;
                Assert.AreEqual(1f, d.magnitude, 1e-3f, "the direction is a direction, not a distance");
                Assert.AreEqual(0f, (at.x - Centre.x) * d.y - (at.y - Centre.y) * d.x, 1e-2f,
                    "and it lies on the line from the camera to the subject");
            }
        }

        /// <summary>
        /// The rule the whole feature hangs on: a subject under a card is out of view, and its arrow is stepped
        /// back along its own sight line until clear of the card - because the sight line is what the arrow
        /// means, and an arrow slid sideways off it points at the wrong place.
        /// </summary>
        [Test]
        public void AnArrowIsSteppedClearOfTheCardItWouldOtherwiseSitUnder()
        {
            int arrows = ThreatRadar.Collect(Screen, new[] { Enemy(EnemyKind.Runner, 12f, 26f) },
                NoJams, Centre, 1f, Markers(4, out ThreatRadar.Marker[] into), out ThreatRadar.Summary summary);

            Assert.AreEqual(1, summary.Threats, "the walker under the status card is out of view");
            Assert.AreEqual(1, arrows);
            Assert.IsFalse(Screen.Covers(into[0].At), "but its arrow is not under it");
            Assert.Greater(into[0].At.x, 14f, "it stands clear of the card's right edge");

            Vector2 fromCentre = into[0].At - Centre;
            Vector2 toSubject = new Vector2(12f, 26f) - Centre;
            Assert.AreEqual(0f, fromCentre.x * toSubject.y - fromCentre.y * toSubject.x, 1e-3f,
                "and it never left the line between the camera and the walker");
        }

        /// <summary>The same, on the edge the build bar owns along its whole length: the arrow for a wave
        /// coming up from the south has to clear the bar, not hide behind it.</summary>
        [Test]
        public void AnArrowAboveTheBuildBar_ClearsItsWholeLength()
        {
            ThreatRadar.Collect(Screen, new[] { Enemy(EnemyKind.Runner, 20f, 5f) }, NoJams, Centre,
                1f, Markers(4, out ThreatRadar.Marker[] into), out _);

            Assert.IsFalse(Screen.Covers(into[0].At));
            Assert.AreEqual(13.5f, into[0].At.y, 1e-3f, "just clear of the bar, with the marker's own margin");
            Assert.AreEqual(20f, into[0].At.x, 1e-3f, "and still on the way south");
        }

        /// <summary>
        /// The same rule applied to the one panel that is only sometimes there: the info card's band while a
        /// sorter's picker is on it. A reader has to be <em>told</em> about it - it is a parameter of the panel
        /// list, not something the card's place implies - so a reader that was not told draws its arrow under the
        /// picker, which is the one outcome the band exists to prevent. Pinned on the reader's side of that
        /// contract, where the arrow's answer actually lives.
        /// </summary>
        [Test]
        public void AnArrowIsSteppedClearOfTheGrownInfoCard_WhileTheSorterPickerIsUp()
        {
            // The band the picker grows by: the info card's own strip, floating above the build bar's right end.
            var band = new Rect(16f, 13f, 6f, 4f);
            var panels = new[] { Panels[0], Panels[1], band };
            var screen = new ThreatRadar.Viewport(Centre, new Vector2(10f, 10f), Margin, panels, panels.Length);
            var wave = new[] { Enemy(EnemyKind.Runner, 20f, 5f) };      // due south, off the window's bottom

            Assert.IsTrue(screen.Covers(new Vector2(20f, 15f)), "the test's premise: the band is a panel");
            Assert.IsFalse(Screen.Covers(new Vector2(20f, 15f)),
                "and without it that tile is free, which is why the reader has to be told");

            // Without the band the arrow is clear of the build bar and nothing else.
            ThreatRadar.Collect(Screen, wave, NoJams, Centre, 1f,
                Markers(4, out ThreatRadar.Marker[] under), out _);
            Assert.AreEqual(13.5f, under[0].At.y, 1e-3f, "the bar alone puts it just above itself");

            int arrows = ThreatRadar.Collect(screen, wave, NoJams, Centre, 1f,
                Markers(4, out ThreatRadar.Marker[] into), out _);

            Assert.AreEqual(1, arrows);
            Assert.IsFalse(screen.Covers(into[0].At), "the band is not where the arrow is left");
            Assert.Greater(into[0].At.y, under[0].At.y,
                "it was stepped further up the sight line to clear the picker");
            Assert.AreEqual(20f, into[0].At.x, 1e-3f, "and it never left the way south to get there");
        }

        /// <summary>The warning frame is drawn on the free area - the ring with any side pushed in past the card
        /// eating the middle of that side. A frame the build bar covers along its whole length reads as broken.</summary>
        [Test]
        public void TheWarningFrameStepsAroundTheCardsThatEatItsSides()
        {
            Rect free = Screen.Free;

            Assert.AreEqual(13.5f, free.yMin, 1e-3f, "the bottom is carried up past the build bar");
            Assert.AreEqual(29.5f, free.yMax, 1e-3f, "the top is not: no card reaches the middle of it");
            Assert.AreEqual(10.5f, free.xMin, 1e-3f, "nor is the left, below the status card");
            Assert.AreEqual(29.5f, free.xMax, 1e-3f);
        }

        [Test]
        public void NothingOffTheOpenScreen_IsNothingToPointAt()
        {
            int arrows = ThreatRadar.Collect(Screen, new[]
            {
                Enemy(EnemyKind.Runner, 20f, 20f),
                Enemy(EnemyKind.Tank, 27f, 27f),      // on the window, clear of every card
                Enemy(EnemyKind.Tank, 12f, 15f),      // on the free ground under the status card
            }, NoJams, Centre, 1f, Markers(4, out _), out ThreatRadar.Summary summary);

            Assert.AreEqual(0, arrows);
            Assert.IsFalse(summary.HasAnything);
            Assert.AreEqual(0, summary.Threats);
            Assert.IsFalse(summary.CoreIsOut, "the Core is the point the camera is on, so it is in view");
        }

        /// <summary>The distance an arrow carries is the tiles between the player's eyes and the thing it points
        /// at - the same number the status card prints, so arrow and sentence cannot disagree about range.</summary>
        [Test]
        public void AnArrowKnowsHowFarAway_WhatItPointsAtIs()
        {
            ThreatRadar.Collect(Screen, new[] { Enemy(EnemyKind.Runner, 40f, 20f) }, NoJams, Centre,
                1f, Markers(4, out _), out ThreatRadar.Summary summary);

            Assert.AreEqual(20f, summary.Nearest, 1e-3f);
        }

        /// <summary>A wave from one door is one piece of news: without the fold, sixteen enemies put sixteen
        /// arrows on one window edge and the useful part - which way - is what the player has to count to find.</summary>
        [Test]
        public void AWaveFromOneDoor_IsOneArrowCountingTheWave()
        {
            int arrows = ThreatRadar.Collect(Screen, new[]
            {
                Enemy(EnemyKind.Runner, 40f, 19.6f),
                Enemy(EnemyKind.Runner, 40f, 20.2f),
                Enemy(EnemyKind.Tank, 41f, 20.4f),
            }, NoJams, Centre, 2f, Markers(4, out ThreatRadar.Marker[] into), out ThreatRadar.Summary summary);

            Assert.AreEqual(1, arrows, "the three of them fold into one arrow");
            Assert.AreEqual(3, summary.Threats, "and the summary still counts the wave");
            Assert.AreEqual(3, into[0].Count);
        }

        /// <summary>Two doors are two pieces of news, and the fold must not merge them into one: the merge is
        /// measured along the edge, not through the map.</summary>
        [Test]
        public void TwoDoorsAtOppositeEndsOfTheWindow_AreTwoArrows()
        {
            int arrows = ThreatRadar.Collect(Screen, new[]
            {
                Enemy(EnemyKind.Runner, 40f, 20f),         // east
                Enemy(EnemyKind.Runner, 0f, 20f),          // west
            }, NoJams, Centre, 2f, Markers(4, out _), out _);

            Assert.AreEqual(2, arrows);
        }

        /// <summary>
        /// What an arrow wears when the things behind it disagree: the colour of the nearest and the shout of
        /// any - the glance is the nearest enemy (the one reaching the Core first), the urgency whichever is
        /// already hitting something, so a nearer latecomer cannot hide the fact the arrow exists to report.
        /// </summary>
        [Test]
        public void AClusterWearsItsNearestEnemy_ButShoutsForAnyOfThem()
        {
            var near = Enemy(EnemyKind.Tank, 35f, 20f);
            var farButHitting = Enemy(EnemyKind.Runner, 60f, 20f, attacking: true);

            int arrows = ThreatRadar.Collect(Screen, new[] { farButHitting, near }, NoJams, Centre,
                8f, Markers(4, out ThreatRadar.Marker[] into), out ThreatRadar.Summary summary);

            Assert.AreEqual(1, arrows);
            Assert.AreEqual(EnemyKind.Tank, into[0].Kind, "the nearer of the two is the face it wears");
            Assert.AreEqual(15f, into[0].Distance, 1e-3f, "and the number it fades by");
            Assert.IsTrue(into[0].Urgent, "but the one hitting something is what makes it shout");
            Assert.IsTrue(summary.Urgent);
        }

        /// <summary>A cap that silently lost enemies would be the same lie as "nothing out of view" while a wave
        /// marches, so a full array folds the rest in and the summary keeps counting every one.</summary>
        [Test]
        public void WhenThereIsNoRoomForAnotherArrow_TheCountStaysTrue()
        {
            int arrows = ThreatRadar.Collect(Screen, new[]
            {
                Enemy(EnemyKind.Runner, 40f, 20f),
                Enemy(EnemyKind.Runner, 20f, 2f),
                Enemy(EnemyKind.Runner, 0f, 20f),
                Enemy(EnemyKind.Runner, 20f, 40f),
            }, NoJams, Centre, 0.1f, Markers(1, out ThreatRadar.Marker[] into), out ThreatRadar.Summary summary);

            Assert.AreEqual(1, arrows, "one arrow, because one is what there is room for");
            Assert.AreEqual(4, summary.Threats, "and four enemies, because that is what there is");
            Assert.AreEqual(4, into[0].Count, "all of them accounted for by the arrow that stands");
        }

        /// <summary>A jam is the one thing off-screen the player can fix with a single right-click, so it is
        /// located for the same reason a wave is: not to be warned about, but to be found.</summary>
        [Test]
        public void JamsAreLocated_AsLongAsTheyAreOutOfReachOfTheCursor()
        {
            int arrows = ThreatRadar.Collect(Screen, NoEnemies,
                new[] { new Int2(35, 20), new Int2(12, 26), new Int2(20, 20) },   // off, under a card, in view
                Centre, 1f, Markers(4, out ThreatRadar.Marker[] into), out ThreatRadar.Summary summary);

            Assert.AreEqual(2, summary.Jams, "the two the player can neither see nor click");
            Assert.AreEqual(2, arrows);
            Assert.AreEqual(ThreatRadar.Subject.Jam, into[0].What);
            Assert.AreEqual(ThreatRadar.Subject.Jam, into[1].What);
        }

        /// <summary>The Core is the one place every arrow is only useful for getting back to, so it is
        /// marked when - and only when - it is out of view.</summary>
        [Test]
        public void TheCoreIsMarkedOnlyWhenItIsOutOfView()
        {
            ThreatRadar.Collect(Screen, NoEnemies, NoJams, new Vector2(20f, 20f), 1f,
                Markers(4, out _), out ThreatRadar.Summary inView);
            Assert.IsFalse(inView.CoreIsOut);

            int arrows = ThreatRadar.Collect(Screen, NoEnemies, NoJams, new Vector2(6f, 6f), 1f,
                Markers(4, out ThreatRadar.Marker[] into), out ThreatRadar.Summary outOfView);

            Assert.IsTrue(outOfView.CoreIsOut);
            Assert.AreEqual(1, arrows);
            Assert.AreEqual(ThreatRadar.Subject.Core, into[0].What);
        }

        /// <summary>The fold keeps both members' urgency and the nearer one's face, whichever order they arrive
        /// in - a wave is listed in the field's slot order, which nobody gets to choose.</summary>
        [Test]
        public void FoldingTwoArrowsAtOnePlace_KeepsTheCountAndTheShout()
        {
            var idle = new ThreatRadar.Marker(ThreatRadar.Subject.Threat, new Vector2(28f, 20f),
                Vector2.right, 1, 12f, false, EnemyKind.Tank);
            var hitting = new ThreatRadar.Marker(ThreatRadar.Subject.Threat, new Vector2(28f, 20f),
                Vector2.right, 1, 30f, true, EnemyKind.Runner);

            ThreatRadar.Marker folded = idle.Fold(in hitting);
            Assert.AreEqual(2, folded.Count);
            Assert.IsTrue(folded.Urgent, "a cluster that starts hitting keeps shouting");
            Assert.AreEqual(12f, folded.Distance, "and keeps the nearer distance");
            Assert.AreEqual(EnemyKind.Tank, folded.Kind);

            ThreatRadar.Marker nearerLast = hitting.Fold(in idle);
            Assert.AreEqual(12f, nearerLast.Distance, "the fold does not care which came first");
            Assert.AreEqual(EnemyKind.Tank, nearerLast.Kind);
            Assert.AreEqual(2, nearerLast.Count);
        }

        /// <summary>The hit window the danger frame and the health bar's flash share: one age, read off the
        /// stream rather than watched for an edge, and closed by the run's own clock.</summary>
        [Test]
        public void TheCoreHitWindow_AgesWithTheRun_AndCloses()
        {
            SimWorld world = Sim.NewWorld(16, 10);
            Assert.AreEqual(-1, ThreatRadar.CoreHitAge(world, 6), "nothing has hit it yet");

            world.Events.CoreDamaged(4f);
            Sim.Tick(world, 2);

            Assert.AreEqual(2, ThreatRadar.CoreHitAge(world, 6), "the hit carries the tick it happened on");
            Assert.IsTrue(ThreatRadar.CoreIsBeingHit(world, 6));
            Assert.IsFalse(ThreatRadar.CoreIsBeingHit(world, 1), "the window is the reader's, not the world's");
            Assert.IsTrue(ThreatRadar.TryCoreHit(world, 6, out float damage, out _), "and says what it cost");
            Assert.AreEqual(4f, damage, 1e-4f);

            Sim.Tick(world, 6);
            Assert.AreEqual(-1, ThreatRadar.CoreHitAge(world, 6), "an old hit stops being news");
        }

        private static EnemySnapshot Enemy(EnemyKind kind, float x, float y, bool attacking = false)
            => new EnemySnapshot
            {
                Id = 1,
                Kind = kind,
                Position = new Vec2(x, y),
                PreviousPosition = new Vec2(x, y),
                HealthFraction = 1f,
                Attacking = attacking,
            };

        /// <summary>An array to collect into, handed back as `into` so a test can read what was written: markers
        /// live in the caller's buffer, exactly as they do in the view and the card.</summary>
        private static ThreatRadar.Marker[] Markers(int room, out ThreatRadar.Marker[] into)
        {
            into = new ThreatRadar.Marker[room];
            return into;
        }
    }
}

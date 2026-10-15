using System;
using System.Collections.Generic;
using Facet.Core;
using Facet.Game;
using NUnit.Framework;
using UnityEngine;

namespace Facet.Tests
{
    /// <summary>
    /// The format nails. The enums' ordinals are keys into authored assets
    /// (<c>Assets/Data/ContentDatabase.asset</c>, the map assets' tile and wave arrays) and the hotkey
    /// layout, so inserting a value mid-list silently re-points stored numbers at the wrong content -
    /// permanently, without a throw. Every ordinal here is pinned by exact slot, so changing one means
    /// editing this file on purpose and facing what the stored assets then mean. The second half is the
    /// fallback audit: views that used to answer an unknown enum with the shipped default now name every
    /// kind and throw, and these tests walk every value through so a new kind without a look, colour or
    /// badge is a red test rather than a quiet re-skin.
    /// </summary>
    public class ContentFormatTests
    {
        // ------------------------------------------------------------------ the stored ordinals

        [Test]
        public void BuildKind_Ordinals_AreTheShippedFormat()
        {
            // The ordinal is the hotkey order and the content-table index. The rework reshaped this list
            // rather than appending to it - the operators landed where the two splitters used to be - so it
            // is pinned here in full, and every authored row in the content asset is keyed by id for exactly
            // this reason.
            Assert.AreEqual("Belt", ((BuildKind)0).ToString());
            Assert.AreEqual("Drill", ((BuildKind)1).ToString());
            Assert.AreEqual("Cutter", ((BuildKind)2).ToString());
            Assert.AreEqual("Rotator", ((BuildKind)3).ToString());
            Assert.AreEqual("Stacker", ((BuildKind)4).ToString());
            Assert.AreEqual("Pipe", ((BuildKind)5).ToString());
            Assert.AreEqual("Splitter", ((BuildKind)6).ToString());
            Assert.AreEqual("Sorter", ((BuildKind)7).ToString());
            Assert.AreEqual("Wall", ((BuildKind)8).ToString());
            Assert.AreEqual("Turret", ((BuildKind)9).ToString());

            Assert.AreEqual(10, Enum.GetValues(typeof(BuildKind)).Length,
                "the count is pinned too - a name in the wrong slot is a fail, a name added anywhere " +
                "is this fail plus the hotkey test");
        }

        [Test]
        public void TileKind_Ordinals_AreTheShippedFormat_RetiredSlotsIncluded()
        {
            Assert.AreEqual("Empty", ((TileKind)0).ToString());
            Assert.AreEqual("Belt", ((TileKind)1).ToString());
            Assert.AreEqual("Core", ((TileKind)2).ToString());

            // 3 was ShapePatch, retired when terrain became its own layer. It is dead on purpose: re-using it
            // would make every authored TileKind value stored as 3 - in maps, in the content asset - mean
            // something else, and this test is the one that says so.
            Assert.IsFalse(Enum.IsDefined(typeof(TileKind), (byte)3),
                "slot 3 is retired and must never be re-used");

            Assert.AreEqual("Drill", ((TileKind)4).ToString());

            // 5 was Decomposer and 10 was an earlier Cutter: retired by the rework, dead for the same reason.
            Assert.IsFalse(Enum.IsDefined(typeof(TileKind), (byte)5),
                "slot 5 (the decomposer) is retired and must never be re-used");
            Assert.IsFalse(Enum.IsDefined(typeof(TileKind), (byte)10),
                "slot 10 is retired and must never be re-used");

            Assert.AreEqual("Pipe", ((TileKind)6).ToString());
            Assert.AreEqual("Splitter", ((TileKind)7).ToString());
            Assert.AreEqual("Turret", ((TileKind)8).ToString());
            Assert.AreEqual("Sorter", ((TileKind)9).ToString());

            // 11 was Mortar and 13 was Assembler: one universal chamber replaced both guns, and combining is
            // the stacker's job now.
            Assert.IsFalse(Enum.IsDefined(typeof(TileKind), (byte)11),
                "slot 11 (the mortar) is retired and must never be re-used");
            Assert.IsFalse(Enum.IsDefined(typeof(TileKind), (byte)13),
                "slot 13 (the assembler) is retired and must never be re-used");

            Assert.AreEqual("Wall", ((TileKind)12).ToString());

            // The three operators took the tail, past every retired value, so nothing an author stored moved.
            Assert.AreEqual("Cutter", ((TileKind)14).ToString());
            Assert.AreEqual("Rotator", ((TileKind)15).ToString());
            Assert.AreEqual("Stacker", ((TileKind)16).ToString());
            Assert.AreEqual(12, Enum.GetValues(typeof(TileKind)).Length);
        }

        [Test]
        public void BehaviorKind_Slots_AreTheShippedFormat_RetiredSlotsIncluded()
        {
            // Indexed by MachineSystem's behaviour registry and by MachineRenderer's drawing switch - which
            // is why the retired numbers matter twice over: not only must they stay dead, the registry must
            // be sized for the HIGHEST value rather than the member count (see the test below).
            Assert.AreEqual("None", ((BehaviorKind)0).ToString());
            Assert.AreEqual("Drill", ((BehaviorKind)1).ToString());

            Assert.IsFalse(Enum.IsDefined(typeof(BehaviorKind), (byte)2),
                "slot 2 (the converter, which ran a recipe) is retired and must never be re-used");
            Assert.IsFalse(Enum.IsDefined(typeof(BehaviorKind), (byte)7),
                "slot 7 (the assembler, which fused two inputs) is retired and must never be re-used");

            Assert.AreEqual("Pipe", ((BehaviorKind)3).ToString());
            Assert.AreEqual("Splitter", ((BehaviorKind)4).ToString());
            Assert.AreEqual("Turret", ((BehaviorKind)5).ToString());
            Assert.AreEqual("Sorter", ((BehaviorKind)6).ToString());
            Assert.AreEqual("Cutter", ((BehaviorKind)8).ToString());
            Assert.AreEqual("Rotator", ((BehaviorKind)9).ToString());
            Assert.AreEqual("Stacker", ((BehaviorKind)10).ToString());
            Assert.AreEqual(9, Enum.GetValues(typeof(BehaviorKind)).Length,
                "nine members over eleven slots: the member count is NOT the slot count");
        }

        [Test]
        public void Quad_Slots_AreTheShippedFormat()
        {
            // What replaced the six-value ShapeType, and what the map assets store per ore cell. A mineral
            // is one quadrant's content; an item's identity is four of them, which is the encoding pinned
            // underneath.
            Assert.AreEqual("Empty", ((Quad)0).ToString());
            Assert.AreEqual("Circle", ((Quad)1).ToString());
            Assert.AreEqual("Square", ((Quad)2).ToString());
            Assert.AreEqual(3, Enum.GetValues(typeof(Quad)).Length);
        }

        [Test]
        public void Shape_Encoding_IsTwoBitsPerQuadrant_InCompassOrder()
        {
            // The item encoding itself, which is what a save, a hash or a snapshot would carry: two bits per
            // quadrant, NW then NE then SE then SW. A full circle is 0x55 and a full square 0xAA, and the
            // upper bits are reserved for the second layer the design leaves room for and does not build.
            Assert.AreEqual(0x55u, Shape.Full(Quad.Circle).Code, "four circle quadrants, two bits each");
            Assert.AreEqual(0xAAu, Shape.Full(Quad.Square).Code, "and the square's");
            Assert.AreEqual(0u, Shape.None.Code, "nothing is no code at all");
            Assert.AreEqual(1u, Shape.Quarter(Shape.NW, Quad.Circle).Code, "NW is the lowest pair");
            Assert.AreEqual(0x40u, Shape.Quarter(Shape.SW, Quad.Circle).Code, "and SW the highest");

            // A code with a 3 in a quadrant is not an item, and the algebra never mints one: the state space
            // is 3^4 - 1, not 4^4 - 1.
            Assert.AreEqual(80, ShapePool.All.Count);
            foreach (Shape shape in ShapePool.All)
                for (int q = 0; q < Shape.QuadrantCount; q++)
                    Assert.AreNotEqual((Quad)3, shape.At(q), shape + " holds an impossible quadrant");
        }

        [Test]
        public void EnemyKind_Slots_AreTheShippedFormat()
        {
            // Stored in the wave groups of every authored map.
            Assert.AreEqual("Runner", ((EnemyKind)0).ToString());
            Assert.AreEqual("Tank", ((EnemyKind)1).ToString());
            Assert.AreEqual(2, Enum.GetValues(typeof(EnemyKind)).Length);
        }

        [Test]
        public void EventKinds_Slots_AreTheShippedFormat()
        {
            // The stream is in-memory, but readers switch on it across assemblies, and a retuned middle
            // changes what every one of them counts. Pinning it costs nothing.
            Assert.AreEqual("Built", ((SimEventKind)0).ToString());
            Assert.AreEqual("Removed", ((SimEventKind)1).ToString());
            Assert.AreEqual("Jammed", ((SimEventKind)2).ToString());
            Assert.AreEqual("JamCleared", ((SimEventKind)3).ToString());
            Assert.AreEqual("Banked", ((SimEventKind)4).ToString());
            Assert.AreEqual("ShotFired", ((SimEventKind)5).ToString());
            Assert.AreEqual("EnemyDamaged", ((SimEventKind)6).ToString());
            Assert.AreEqual("EnemyKilled", ((SimEventKind)7).ToString());
            Assert.AreEqual("CoreDamaged", ((SimEventKind)8).ToString());
            Assert.AreEqual("WaveStarted", ((SimEventKind)9).ToString());
            Assert.AreEqual("WaveCleared", ((SimEventKind)10).ToString());
            Assert.AreEqual("RunWon", ((SimEventKind)11).ToString());
            Assert.AreEqual("RunLost", ((SimEventKind)12).ToString());
            Assert.AreEqual("BuildingDamaged", ((SimEventKind)13).ToString());
            Assert.AreEqual("BuildingDestroyed", ((SimEventKind)14).ToString());
            Assert.AreEqual("PlacementRejected", ((SimEventKind)15).ToString());
            Assert.AreEqual(16, Enum.GetValues(typeof(SimEventKind)).Length);

            Assert.AreEqual("NoFunds", ((RejectionReason)0).ToString());
            Assert.AreEqual("Occupied", ((RejectionReason)1).ToString());
            Assert.AreEqual("BadGround", ((RejectionReason)2).ToString());
            Assert.AreEqual("OffMap", ((RejectionReason)3).ToString());
            Assert.AreEqual("Doorway", ((RejectionReason)4).ToString());
            Assert.AreEqual("Locked", ((RejectionReason)5).ToString(),
                "the unlock ladder's refusal, appended so every stored reason keeps its number");
            Assert.AreEqual(6, Enum.GetValues(typeof(RejectionReason)).Length);
        }

        [Test]
        public void Operators_AreAFlagsSet_AndTheLadderIsTheirOrder()
        {
            // One bit per operator, in the order the campaign hands them over, and `All` is the three of
            // them: the map assets store this set as a number, so the bits are format.
            Assert.AreEqual(1, (int)Operators.Cutter);
            Assert.AreEqual(2, (int)Operators.Rotator);
            Assert.AreEqual(4, (int)Operators.Stacker);
            Assert.AreEqual(Operators.All,
                Operators.Cutter | Operators.Rotator | Operators.Stacker);

            // And each operator machine is the operator it is, which is what a map's unlock set gates.
            Assert.AreEqual(Operators.Cutter, ContentDatabase.Default.Machine(BuildKind.Cutter).Requires);
            Assert.AreEqual(Operators.Rotator, ContentDatabase.Default.Machine(BuildKind.Rotator).Requires);
            Assert.AreEqual(Operators.Stacker, ContentDatabase.Default.Machine(BuildKind.Stacker).Requires);
            Assert.AreEqual(Operators.None, ContentDatabase.Default.Machine(BuildKind.Turret).Requires,
                "the scaffolding needs no unlocking");
        }

        // ------------------------------------------------------------------ the registry's size

        [Test]
        public void TheBehaviourRegistry_IsSizedForTheHighestOrdinal_NotTheMemberCount()
        {
            // The regression this test exists for: `BehaviorKind` has nine members over eleven slots, so a
            // registry sized by `Enum.GetValues(...).Length` is short by exactly its two holes - and the
            // rotator (9) and the stacker (10) sit past both of them. Every world died in MachineSystem's
            // constructor with an IndexOutOfRangeException, which no compiler can see.
            SimWorld world = null;
            Assert.DoesNotThrow(() => world = Sim.NewWorld(20, 12), "building a world registers every behaviour");

            Sim.LayRun(world, new Int2(4, 4), Dir.East, 1);
            Sim.Place(world, BuildKind.Rotator, new Int2(6, 4), Dir.East);
            Sim.LayRun(world, new Int2(6, 5), Dir.North, 1);
            Sim.Place(world, BuildKind.Stacker, new Int2(14, 8), Dir.East);   // clear of the Core's own block

            Assert.DoesNotThrow(() => Sim.Tick(world, 8),
                "and every one of the eleven slots the enum can name is either a behaviour or empty");
        }

        // ------------------------------------------------------------------ reach of the controls

        [Test]
        public void Hotkeys_ReachEveryBuildKind()
        {
            // The digit-to-kind mapping is positional (BuildInput.BuildKeys): an eleventh kind would have no
            // key and bar tiles whose labels wrap onto a used digit, both silently. This is the fail-loud -
            // extend BuildKeys and the labels before extending the table.
            BuildKind[] kinds = ContentDatabase.Default.BuildKinds;
            Assert.LessOrEqual(kinds.Length, BuildInput.HotkeyCount,
                kinds.Length + " build kinds against " + BuildInput.HotkeyCount + " hotkeys");

            Assert.AreEqual("1", BuildInput.HotkeyLabel(0));
            Assert.AreEqual("9", BuildInput.HotkeyLabel(8));
            Assert.AreEqual("0", BuildInput.HotkeyLabel(9), "the tenth kind is key 0");
            Assert.AreEqual("1-9 and 0", BuildInput.KeyRangeText(),
                "the shipped ten kinds are keys 1-9 and 0 - and the bar's hint says exactly that, in the " +
                "characters a built player's HUD font actually carries");
        }

        [Test]
        public void IsMachine_CoversEveryBuiltKind_AndNothingElse()
        {
            // The range MachineField's registry and TerrainField's walkable rule read: a built kind left out
            // silently never gets a MachineState - no ports, no HP, no tick.
            Assert.IsFalse(TileKind.Empty.IsMachine());
            Assert.IsFalse(TileKind.Belt.IsMachine(), "the belt lives on its own layer, unregistered");
            Assert.IsFalse(TileKind.Core.IsMachine(), "the Core is placed by the map, not built");

            foreach (TileKind kind in (TileKind[])Enum.GetValues(typeof(TileKind)))
            {
                if (kind < TileKind.Drill) continue;
                Assert.IsTrue(kind.IsMachine(), kind + " is built and must carry a MachineState");
            }
        }

        // ------------------------------------------------------------------ the fallback audit

        [Test]
        public void EveryEnemyKind_HasAnExplicitLook_AndColour()
        {
            // Palette.EnemyColor and EnemyVisuals.For used to answer an unknown kind with the Runner's look,
            // so a third enemy walked the map wearing the first one's face. They now throw, and this test
            // walks the roster - a new EnemyKind without both a look and a colour is a red test here.
            var palette = ScriptableObject.CreateInstance<Palette>();
            var visuals = new EnemyVisuals();

            try
            {
                foreach (EnemyKind kind in (EnemyKind[])Enum.GetValues(typeof(EnemyKind)))
                {
                    Assert.DoesNotThrow(() => palette.EnemyColor(kind), kind + " has a body colour");
                    Assert.IsNotNull(visuals.For(kind), kind + " has a shipped look");
                }

                Assert.AreNotEqual(palette.EnemyColor(EnemyKind.Runner), palette.EnemyColor(EnemyKind.Tank),
                    "colour is the out-of-the-box way the two kinds tell each other apart");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(palette);
            }
        }

        [Test]
        public void EveryMineral_HasAnExplicitColour_AndMark()
        {
            // The same audit for minerals. The look is per MINERAL now, not per item: an item is a figure
            // assembled from its quadrants, so a mineral's mark is a quadrant-shaped one - a quarter disc for
            // a circle, a cell for a square - and the two must stay distinct, or two minerals are drawn
            // identically and the shape algebra is invisible.
            var palette = ScriptableObject.CreateInstance<Palette>();

            try
            {
                Assert.DoesNotThrow(() => palette.MineralColor(Quad.Circle), "circle has a colour");
                Assert.DoesNotThrow(() => palette.MineralColor(Quad.Square), "square has one too");
                Assert.AreNotEqual(palette.MineralColor(Quad.Circle), palette.MineralColor(Quad.Square),
                    "and they are not the same colour");

                ShapeIconSet icons = palette.ShapeIcons(0.22f, 2f);
                for (int quadrant = 0; quadrant < Shape.QuadrantCount; quadrant++)
                {
                    IReadOnlyList<Vector2> circle = icons.Points(Quad.Circle, quadrant);
                    IReadOnlyList<Vector2> square = icons.Points(Quad.Square, quadrant);

                    Assert.IsNotNull(circle, "a circle quadrant has a mark (" + Shape.CornerCode(quadrant) + ")");
                    Assert.IsNotNull(square, "and so does a square one (" + Shape.CornerCode(quadrant) + ")");
                    Assert.AreNotEqual(circle.Count, square.Count,
                        "a quarter disc and a cell are different figures in " + Shape.CornerCode(quadrant));
                }

                // A hole has neither: asking for one has lost the quadrant it meant, so it throws rather
                // than drawing the outline and hiding the mistake.
                Assert.Throws<ArgumentOutOfRangeException>(() => palette.MineralColor(Quad.Empty));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(palette);
            }
        }

        [Test]
        public void EveryMachineRow_Dispatches_AndIsDrawn_AndAnswersForItsPorts()
        {
            // The three Core switches that used to answer unknown behaviours with "nothing" - the badge and
            // work fraction and MachineDelivery's port rule - are reached through real snapshots. Placing
            // every kind and pulling every snapshot walks each behaviour through them: an unnamed behaviour
            // throws with its name, not a player wondering where its badge went.
            SimWorld world = Sim.NewWorld(24, 12);

            world.Terrain.SetOre(new Int2(2, 8), Quad.Circle);
            Sim.Place(world, BuildKind.Drill, new Int2(2, 8), Dir.East);          // mining, clear of row 2
            Sim.LayRun(world, new Int2(3, 8), Dir.East, 1);

            // The cutter's own rule, wired the way it has to be wired: a cutter facing east cuts left from
            // right, so its two outlets are the east and west sides - which means its feeds can only come
            // from the perpendicular axis. This is the one machine whose ports a player must think about.
            Sim.Place(world, BuildKind.Cutter, new Int2(4, 2), Dir.East);
            Sim.LayRun(world, new Int2(5, 2), Dir.East, 1);                       // front outlet
            Sim.LayRun(world, new Int2(3, 2), Dir.West, 1);                       // back outlet
            Sim.LayRun(world, new Int2(4, 1), Dir.North, 1);                      // fed from the south
            Sim.LayRun(world, new Int2(4, 3), Dir.South, 1);                      // and from the north

            Sim.Place(world, BuildKind.Rotator, new Int2(8, 2), Dir.East);        // one outlet
            Sim.LayRun(world, new Int2(9, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Stacker, new Int2(12, 8), Dir.East);       // any wired side
            Sim.Place(world, BuildKind.Pipe, new Int2(15, 2), Dir.East);          // carried badge
            Sim.Place(world, BuildKind.Splitter, new Int2(17, 2), Dir.East);      // hub badge
            Sim.Place(world, BuildKind.Sorter, new Int2(19, 2), Dir.East);        // filter badge
            Sim.Place(world, BuildKind.Turret, new Int2(21, 2), Dir.East);        // last-round badge
            Sim.Place(world, BuildKind.Wall, new Int2(23, 2), Dir.East);          // no badge at all
            Sim.Tick(world, 4);

            var seen = new HashSet<BuildKind>();
            for (int i = 0; i < world.Machines.Occupied.Count; i++)
            {
                Int2 cell = world.Machines.CellAt(i);
                Assert.IsTrue(world.Machines.TryGetSnapshot(cell, out MachineSnapshot m),
                    cell + " was placed and must snapshot");
                Assert.AreEqual(ContentDatabase.Default.Machine(m.Build).Behavior, m.Behavior);
                seen.Add(m.Build);
            }

            foreach (BuildKind kind in ContentDatabase.Default.BuildKinds)
            {
                if (kind == BuildKind.Belt) continue;   // belts are not machines and have no snapshot
                Assert.IsTrue(seen.Contains(kind), kind + " placed, snapshotted, and passed every switch");
            }

            // The port rule's own cases: a drill faces its output, a cutter's outlets are the two sides of
            // its cut line, a wired hub counts, and a turret never pushes.
            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(2, 8), out MachineSnapshot drill));
            Assert.IsTrue(drill.OutMask.Has(Dir.East));

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(4, 2), out MachineSnapshot cutter));
            Assert.IsTrue(cutter.OutMask.Has(Dir.East), "the cutter's front outlet is the side it faces");
            Assert.IsTrue(cutter.OutMask.Has(Dir.West), "and the one opposite it is the other");
            Assert.IsTrue(cutter.InMask.Has(Dir.North) && cutter.InMask.Has(Dir.South),
                "while it is fed from the sides perpendicular to its cut line - the two are different axes");

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(21, 2), out MachineSnapshot gun));
            Assert.IsTrue(gun.OutMask.IsEmpty, "and the turret never has one");
        }

        [Test]
        public void EveryMachineRow_IsNarrated_ByTheOneCatalogue()
        {
            // MachineReading is the behaviour dispatch the HUD card is written through - the copy that used to
            // live in HudView with silent "TRANSPORT" defaults. What the player would read is now assertable here,
            // through the same interface the HUD crosses: every shipped row gets a role, a catalogue line and its
            // numbers, and every placed machine answers for its live state, facing and ports by name.
            SimWorld world = Sim.NewWorld(24, 12);
            ContentDatabase content = world.Content;

            world.Terrain.SetOre(new Int2(2, 2), Quad.Circle);
            Sim.Place(world, BuildKind.Drill, new Int2(2, 2), Dir.East);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Cutter, new Int2(4, 2), Dir.East);
            Sim.Place(world, BuildKind.Rotator, new Int2(8, 2), Dir.East);
            Sim.Place(world, BuildKind.Stacker, new Int2(12, 2), Dir.East);
            Sim.Place(world, BuildKind.Pipe, new Int2(15, 2), Dir.East);
            Sim.Place(world, BuildKind.Splitter, new Int2(17, 2), Dir.East);
            Sim.Place(world, BuildKind.Sorter, new Int2(19, 2), Dir.East);
            Sim.Place(world, BuildKind.Turret, new Int2(21, 2), Dir.East);
            Sim.Place(world, BuildKind.Wall, new Int2(23, 2), Dir.East);
            Sim.Tick(world, 4);

            foreach (BuildKind kind in content.BuildKinds)
            {
                MachineDef def = content.Machine(kind);
                Assert.DoesNotThrow(() => MachineReading.RoleLabel(MachineReading.Role(def)),
                    kind + " is filed under a role the catalogue can print");
                Assert.DoesNotThrow(() => MachineReading.Blurb(def, content), kind + " has a catalogue line");
                Assert.DoesNotThrow(() => MachineReading.Stats(def, content, world.Config),
                    kind + " has its numbers");
            }

            // The claim the card makes about the shipped numbers, pinned at the same seam: if the HUD's copy
            // had drifted from the table (the old failure), this is where it shows.
            Assert.AreEqual("DEFENCE", MachineReading.RoleLabel(MachineReading.Role(content.Machine(BuildKind.Turret))));
            Assert.AreEqual("MINING", MachineReading.RoleLabel(MachineReading.Role(content.Machine(BuildKind.Drill))));
            Assert.AreEqual("TRANSPORT", MachineReading.RoleLabel(MachineReading.Role(content.Machine(BuildKind.Belt))));
            Assert.AreEqual("WALL", MachineReading.RoleLabel(MachineReading.Role(content.Machine(BuildKind.Wall))));
            Assert.AreEqual("PROCESSING",
                MachineReading.RoleLabel(MachineReading.Role(content.Machine(BuildKind.Stacker))),
                "the operators are one role - they transform an item rather than moving it");

            TurretDef gun = content.Turret(BuildKind.Turret);
            StringAssert.Contains("shots/s", MachineReading.Stats(content.Machine(BuildKind.Turret), content, world.Config));
            StringAssert.Contains(gun.ShotsPerSecond.ToString("0.##"),
                MachineReading.Stats(content.Machine(BuildKind.Turret), content, world.Config),
                "the line quotes the table, not a copy of it");

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(2, 2), out MachineSnapshot drill));
            ReadingNote mining = MachineReading.LiveNote(world, new Int2(2, 2), drill);
            Assert.AreEqual(NoteSeverity.Neutral, mining.Severity, "facing a belt, the drill is working");
            StringAssert.Contains("mining", mining.Text);

            // Point the same drill at bare ground and the card says so, with the colour that means it. (A drill
            // may only stand on ore, so the stall is about what it faces, not what it sits on.)
            world.Terrain.SetOre(new Int2(2, 8), Quad.Circle);
            Sim.Place(world, BuildKind.Drill, new Int2(2, 8), Dir.East);
            Sim.Tick(world, 1);
            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(2, 8), out MachineSnapshot stalled));
            ReadingNote stall = MachineReading.LiveNote(world, new Int2(2, 8), stalled);
            Assert.AreEqual(NoteSeverity.Warn, stall.Severity);
            StringAssert.Contains("nothing to push onto", stall.Text);

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(21, 2), out MachineSnapshot starved));
            ReadingNote dry = MachineReading.LiveNote(world, new Int2(21, 2), starved);
            Assert.AreEqual(NoteSeverity.Warn, dry.Severity, "a gun with no ammo is exactly the state to point at");
            StringAssert.Contains("starved", dry.Text);

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(23, 2), out MachineSnapshot wall));
            StringAssert.Contains("no facing",
                MachineReading.FacingNote(content.Machine(BuildKind.Wall), wall));
            StringAssert.Contains("no belts wired", MachineReading.Ports(wall));
        }

        [Test]
        public void TheNarration_RefusesAnUnknownBehaviour_ByName()
        {
            // The discipline the badges keep ("a new behaviour must declare here what badge it wears") now
            // holds for every narration question too: an unregistered behaviour throws naming itself.
            var alien = new MachineDef(new ContentId("alien"), BuildKind.Wall, TileKind.Wall,
                (BehaviorKind)99, cost: 1, name: "Alien", description: "unknown", interval: 0f);

            Assert.Throws<ArgumentOutOfRangeException>(() => MachineReading.Role(alien));
            Assert.Throws<ArgumentOutOfRangeException>(() => MachineReading.Blurb(alien, ContentDatabase.Default));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MachineReading.Stats(alien, ContentDatabase.Default, new SimConfig()));
            Assert.Throws<ArgumentOutOfRangeException>(() => MachineReading.Badge(default, alien));
            Assert.Throws<ArgumentOutOfRangeException>(() => MachineReading.Work(default, alien));
        }

        [Test]
        public void TheShippedTable_IsInternallyConsistent()
        {
            // ContentDatabase's constructor refuses the two silences the table used to ship: a turret row with
            // no gun behind it, and a sorter that routes for nothing. This pins that the shipped table passes,
            // row by row, so the rule is visible as a rule.
            ContentDatabase shipped = ContentDatabase.Default;

            foreach (MachineDef def in shipped.Machines)
            {
                if (def == null) continue;

                if (def.Behavior == BehaviorKind.Turret)
                    Assert.IsTrue(shipped.IsTurret(def.Build), def.Name + " has a gun definition");

                if (def.Behavior == BehaviorKind.Sorter)
                    Assert.IsFalse(def.Filter.IsNone, def.Name + " routes for something");
            }

            // And the universal chamber's own claim, which is content rather than code: nothing shipped
            // narrows a diet, so nothing in the shipped game jams.
            foreach (MachineDef def in shipped.Machines)
                if (def != null && def.Behavior != BehaviorKind.None)
                    Assert.AreEqual(ShapeFilter.Any.Describe(), def.Diet.Describe(),
                        def.Name + " eats anything, as every shipped machine does");
        }

        [Test]
        public void AnInconsistentTable_IsRefusedAtConstruction_NotAtRuntime()
        {
            // The two refusals, shown directly. Both used to surface as a machine that silently never fired
            // or never routed anything, in whichever world happened to be built from the broken table.
            var gunless = new MachineDef(new ContentId("gunless"), BuildKind.Splitter, TileKind.Splitter,
                BehaviorKind.Turret, cost: 10, name: "Gunless", description: "", interval: 0f);

            Assert.Throws<ArgumentException>(() => BuildTable(machine: gunless),
                "a turret row with no turret definition cannot be constructed - it would stand built, " +
                "unfireable, and uncomplaining (a splitter slot: no shipped gun lives there to copy)");

            var blind = new MachineDef(new ContentId("blind"), BuildKind.Sorter, TileKind.Sorter,
                BehaviorKind.Sorter, cost: 12, name: "Blind", description: "", interval: 0f,
                filter: ShapeFilter.None);

            Assert.Throws<ArgumentException>(() => BuildTable(machine: blind),
                "and a sorter with no filter routes by no rule at all - an item it holds forever");
        }

        /// <summary>The shipped table with one machine row swapped (mirroring CustomContentTests'
        /// Rewrite, but not reaching into another suite's helper).</summary>
        private static ContentDatabase BuildTable(MachineDef machine)
        {
            ContentDatabase shipped = ContentDatabase.Default;

            var machines = new MachineDef[shipped.BuildKinds.Length];
            for (int i = 0; i < shipped.BuildKinds.Length; i++)
                machines[i] = shipped.Machine(shipped.BuildKinds[i]);
            machines[(int)machine.Build] = machine;

            var turrets = new TurretDef[shipped.BuildKinds.Length];
            for (int i = 0; i < shipped.BuildKinds.Length; i++)
                if (shipped.IsTurret(shipped.BuildKinds[i])) turrets[i] = shipped.Turret(shipped.BuildKinds[i]);

            var enemies = new EnemyDef[shipped.EnemyKinds.Length];
            for (int i = 0; i < shipped.EnemyKinds.Length; i++)
                enemies[i] = shipped.Enemy(shipped.EnemyKinds[i]);

            return new ContentDatabase(machines, turrets, enemies);
        }
    }
}

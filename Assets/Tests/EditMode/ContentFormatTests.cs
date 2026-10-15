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
            // The ordinal is the hotkey order and the content-table index. Append-only: this is where an
            // insertion says so.
            Assert.AreEqual("Belt", ((BuildKind)0).ToString());
            Assert.AreEqual("Drill", ((BuildKind)1).ToString());
            Assert.AreEqual("Decomposer", ((BuildKind)2).ToString());
            Assert.AreEqual("Pipe", ((BuildKind)3).ToString());
            Assert.AreEqual("Splitter", ((BuildKind)4).ToString());
            Assert.AreEqual("Cannon", ((BuildKind)5).ToString());
            Assert.AreEqual("Sorter", ((BuildKind)6).ToString());
            Assert.AreEqual("Mortar", ((BuildKind)7).ToString());
            Assert.AreEqual("Wall", ((BuildKind)8).ToString());
            Assert.AreEqual("Assembler", ((BuildKind)9).ToString());

            // There was a Cutter at 7 until the two splitters merged into the one decomposer that runs
            // every recipe; Mortar and Wall moved down a slot. A removal is exactly what the content
            // asset's id-keyed rows exist for. Additions append: the Assembler took the tail.
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
            Assert.AreEqual("Decomposer", ((TileKind)5).ToString());
            Assert.AreEqual("Pipe", ((TileKind)6).ToString());
            Assert.AreEqual("Splitter", ((TileKind)7).ToString());
            Assert.AreEqual("Turret", ((TileKind)8).ToString());
            Assert.AreEqual("Sorter", ((TileKind)9).ToString());

            // 10 was Cutter, retired when the two splitters merged - dead on purpose, like 3: re-using it
            // would make every authored TileKind value stored as 10 mean something else.
            Assert.IsFalse(Enum.IsDefined(typeof(TileKind), (byte)10),
                "slot 10 is retired and must never be re-used");

            Assert.AreEqual("Mortar", ((TileKind)11).ToString());
            Assert.AreEqual("Wall", ((TileKind)12).ToString());
            Assert.AreEqual("Assembler", ((TileKind)13).ToString());
            Assert.AreEqual(12, Enum.GetValues(typeof(TileKind)).Length);
        }

        [Test]
        public void BehaviorKind_Slots_AreTheShippedFormat()
        {
            // Indexed by MachineSystem's behaviour registry and by MachineRenderer's drawing switch.
            Assert.AreEqual("None", ((BehaviorKind)0).ToString());
            Assert.AreEqual("Drill", ((BehaviorKind)1).ToString());
            Assert.AreEqual("Converter", ((BehaviorKind)2).ToString());
            Assert.AreEqual("Pipe", ((BehaviorKind)3).ToString());
            Assert.AreEqual("Splitter", ((BehaviorKind)4).ToString());
            Assert.AreEqual("Turret", ((BehaviorKind)5).ToString());
            Assert.AreEqual("Sorter", ((BehaviorKind)6).ToString());
            Assert.AreEqual("Assembler", ((BehaviorKind)7).ToString());
            Assert.AreEqual(8, Enum.GetValues(typeof(BehaviorKind)).Length);
        }

        [Test]
        public void ShapeType_Slots_AreTheShippedFormat()
        {
            // Stored in the map assets' ore arrays and the content asset's rows.
            Assert.AreEqual("None", ((ShapeType)0).ToString());
            Assert.AreEqual("Circle", ((ShapeType)1).ToString());
            Assert.AreEqual("HalfCircle", ((ShapeType)2).ToString());
            Assert.AreEqual("Square", ((ShapeType)3).ToString());
            Assert.AreEqual("HalfSquare", ((ShapeType)4).ToString());
            Assert.AreEqual("Composite", ((ShapeType)5).ToString());
            Assert.AreEqual(6, Enum.GetValues(typeof(ShapeType)).Length);
        }

        [Test]
        public void EnemyKind_Slots_AreTheShippedFormat()
        {
            // Stored in the wave groups of every authored map.
            Assert.AreEqual("Spike", ((EnemyKind)0).ToString());
            Assert.AreEqual("Bulwark", ((EnemyKind)1).ToString());
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
            Assert.AreEqual(5, Enum.GetValues(typeof(RejectionReason)).Length);
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
            // Palette.EnemyColor and EnemyVisuals.For used to answer an unknown kind with the Spike's look, so a
            // third enemy walked the map wearing the first one's face. They now throw, and this test walks the
            // roster - a new EnemyKind without both a look and a colour is a red test here, not a playthrough re-skin.
            var palette = ScriptableObject.CreateInstance<Palette>();
            var visuals = new EnemyVisuals();

            foreach (EnemyKind kind in (EnemyKind[])Enum.GetValues(typeof(EnemyKind)))
            {
                Assert.DoesNotThrow(() => palette.EnemyColor(kind), kind + " has a body colour");
                VisualStyle style = visuals.For(kind);
                Assert.IsNotNull(style, kind + " has a shipped look");
            }

            Assert.AreNotEqual(palette.EnemyColor(EnemyKind.Spike), palette.EnemyColor(EnemyKind.Bulwark),
                "colour is the out-of-the-box way the two kinds tell each other apart");
        }

        [Test]
        public void EveryShape_HasAnExplicitColour_AndSilhouette()
        {
            // The same audit for minerals: Palette.ShapeColor and ProcShapeOf now name every shape (None
            // included) and throw on anything else - a new mineral silently drawn as a circle is two shapes
            // that look identical and a jam rule nobody can see coming.
            var palette = ScriptableObject.CreateInstance<Palette>();

            foreach (ShapeType shape in (ShapeType[])Enum.GetValues(typeof(ShapeType)))
            {
                Assert.DoesNotThrow(() => palette.ShapeColor(shape), shape + " has a colour");
                Assert.DoesNotThrow(() => palette.ProcShapeOf(shape), shape + " has a silhouette");
            }

            Assert.AreNotEqual(palette.ProcShapeOf(ShapeType.Circle),
                palette.ProcShapeOf(ShapeType.HalfCircle), "the shipped shapes stay distinct");
            Assert.AreNotEqual(palette.ProcShapeOf(ShapeType.Square),
                palette.ProcShapeOf(ShapeType.HalfSquare), "and the second chain's shapes too");
        }

        [Test]
        public void EveryMachineRow_Dispatches_AndIsDrawn_AndAnswersForItsPorts()
        {
            // The three Core switches that used to answer unknown behaviours with "nothing" - MachineField.ShapeOf
            // / WorkOf and MachineDelivery's port rule - are reached through real snapshots. Placing every kind and
            // pulling every snapshot walks each behaviour through them: an unnamed behaviour throws with its name,
            // not a player wondering where its badge went.
            SimWorld world = Sim.NewWorld(20, 12);

            world.Terrain.SetOre(new Int2(2, 2), ShapeType.Circle);
            Sim.Place(world, BuildKind.Drill, new Int2(2, 2), Dir.East);          // None badge
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Decomposer, new Int2(4, 2), Dir.East);     // Converter badge
            Sim.LayRun(world, new Int2(5, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Pipe, new Int2(6, 2), Dir.East);           // carried badge
            Sim.Place(world, BuildKind.Splitter, new Int2(8, 2), Dir.East);       // hub badge
            Sim.Place(world, BuildKind.Sorter, new Int2(10, 2), Dir.East);        // filter badge
            Sim.Place(world, BuildKind.Cannon, new Int2(12, 2), Dir.East);        // ammo badge
            Sim.Place(world, BuildKind.Mortar, new Int2(14, 2), Dir.East);
            Sim.Place(world, BuildKind.Wall, new Int2(16, 2), Dir.East);
            Sim.Place(world, BuildKind.Assembler, new Int2(18, 2), Dir.East);     // held-input badge
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

            // The port rule's own cases: a drill faces its output, a wired hub outlet counts,
            // a turret never pushes. (MachineRegistryTests holds the detail; this is the coverage.)
            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(2, 2), out MachineSnapshot drill));
            Assert.IsTrue(drill.OutMask.Has(Dir.East));

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(4, 2), out MachineSnapshot conv));
            Assert.IsTrue(conv.OutMask.Has(Dir.East), "the converter wired away on its facing");

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(12, 2), out MachineSnapshot gun));
            Assert.IsTrue(gun.OutMask.IsEmpty, "and the turret never has one");
        }

        [Test]
        public void EveryMachineRow_IsNarrated_ByTheOneCatalogue()
        {
            // MachineReading is the behaviour dispatch the HUD card is written through - the copy that used to
            // live in HudView with silent "TRANSPORT" defaults. What the player would read is now assertable here,
            // through the same interface the HUD crosses: every shipped row gets a role, a catalogue line and its
            // numbers, and every placed machine answers for its live state, facing and ports by name.
            SimWorld world = Sim.NewWorld(20, 12);
            ContentDatabase content = world.Content;

            world.Terrain.SetOre(new Int2(2, 2), ShapeType.Circle);
            Sim.Place(world, BuildKind.Drill, new Int2(2, 2), Dir.East);
            Sim.LayRun(world, new Int2(3, 2), Dir.East, 1);
            Sim.Place(world, BuildKind.Decomposer, new Int2(4, 2), Dir.East);
            Sim.Place(world, BuildKind.Pipe, new Int2(6, 2), Dir.East);
            Sim.Place(world, BuildKind.Splitter, new Int2(8, 2), Dir.East);
            Sim.Place(world, BuildKind.Sorter, new Int2(10, 2), Dir.East);
            Sim.Place(world, BuildKind.Cannon, new Int2(12, 2), Dir.East);
            Sim.Place(world, BuildKind.Mortar, new Int2(14, 2), Dir.East);
            Sim.Place(world, BuildKind.Wall, new Int2(16, 2), Dir.East);
            Sim.Place(world, BuildKind.Assembler, new Int2(18, 2), Dir.East);
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
            Assert.AreEqual("DEFENCE", MachineReading.RoleLabel(MachineReading.Role(content.Machine(BuildKind.Cannon))));
            Assert.AreEqual("MINING", MachineReading.RoleLabel(MachineReading.Role(content.Machine(BuildKind.Drill))));
            Assert.AreEqual("TRANSPORT", MachineReading.RoleLabel(MachineReading.Role(content.Machine(BuildKind.Belt))));
            Assert.AreEqual("WALL", MachineReading.RoleLabel(MachineReading.Role(content.Machine(BuildKind.Wall))));

            TurretDef cannon = content.Turret(BuildKind.Cannon);
            StringAssert.Contains("shots/s", MachineReading.Stats(content.Machine(BuildKind.Cannon), content, world.Config));
            StringAssert.Contains(cannon.ShotsPerSecond.ToString("0.##"),
                MachineReading.Stats(content.Machine(BuildKind.Cannon), content, world.Config),
                "the line quotes the table, not a copy of it");

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(2, 2), out MachineSnapshot drill));
            ReadingNote mining = MachineReading.LiveNote(world, new Int2(2, 2), drill);
            Assert.AreEqual(NoteSeverity.Neutral, mining.Severity, "facing a belt, the drill is working");
            StringAssert.Contains("mining", mining.Text);

            // Point the same drill at bare ground and the card says so, with the colour that means it. (A drill
            // may only stand on ore, so the stall is about what it faces, not what it sits on.)
            world.Terrain.SetOre(new Int2(2, 8), ShapeType.Circle);
            Sim.Place(world, BuildKind.Drill, new Int2(2, 8), Dir.East);
            Sim.Tick(world, 1);
            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(2, 8), out MachineSnapshot stalled));
            ReadingNote stall = MachineReading.LiveNote(world, new Int2(2, 8), stalled);
            Assert.AreEqual(NoteSeverity.Warn, stall.Severity);
            StringAssert.Contains("nothing to push onto", stall.Text);

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(12, 2), out MachineSnapshot starved));
            ReadingNote dry = MachineReading.LiveNote(world, new Int2(12, 2), starved);
            Assert.AreEqual(NoteSeverity.Warn, dry.Severity, "a gun with no ammo is exactly the state to point at");
            StringAssert.Contains("starved", dry.Text);

            Assert.IsTrue(world.Machines.TryGetSnapshot(new Int2(16, 2), out MachineSnapshot wall));
            StringAssert.Contains("no facing",
                MachineReading.FacingNote(content.Machine(BuildKind.Wall), wall, content));
            StringAssert.Contains("no belts wired", MachineReading.Ports(wall));
        }

        [Test]
        public void TheNarration_RefusesAnUnknownBehaviour_ByName()
        {
            // The discipline MachineField's badges keep ("a new behaviour must declare here what badge it
            // wears") now holds for every narration question too: an unregistered behaviour throws naming itself.
            var alien = new MachineDef(new ContentId("alien"), BuildKind.Wall, TileKind.Wall,
                (BehaviorKind)99, cost: 1, name: "Alien", description: "unknown",
                recipeId: ContentId.None, interval: 0f);

            Assert.Throws<ArgumentOutOfRangeException>(() => MachineReading.Role(alien));
            Assert.Throws<ArgumentOutOfRangeException>(() => MachineReading.Blurb(alien, ContentDatabase.Default));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MachineReading.Stats(alien, ContentDatabase.Default, new SimConfig()));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MachineReading.Badge(default, alien, ContentDatabase.Default));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => MachineReading.Work(default, alien, ContentDatabase.Default));
        }

        [Test]
        public void TheShippedTable_IsInternallyConsistent()
        {
            // ContentDatabase's constructor refuses converter rows without their recipe and turret rows without
            // their gun - the two silences the table used to ship. This pins that the shipped table passes, row
            // by row, so the rule is visible as a rule.
            ContentDatabase shipped = ContentDatabase.Default;

            foreach (MachineDef def in shipped.Machines)
            {
                if (def == null) continue;

                if (def.Behavior == BehaviorKind.Converter)
                {
                    if (def.RecipeId.IsNone)
                        Assert.IsNotEmpty(shipped.Recipes,
                            def.Name + " names no recipe, so it runs every recipe the table holds - " +
                            "which needs the table to hold some");
                    else
                        Assert.DoesNotThrow(() => shipped.Recipe(def.RecipeId),
                            def.Name + " names a recipe the table holds");
                }

                if (def.Behavior == BehaviorKind.Turret)
                    Assert.IsTrue(shipped.IsTurret(def.Build), def.Name + " has a gun definition");
            }
        }

        [Test]
        public void AnInconsistentTable_IsRefusedAtConstruction_NotAtRuntime()
        {
            // The two refusals, shown directly: a converter naming a recipe nothing carries, and a turret with
            // no gun. Both used to surface as a machine that silently never made or fired anything, in whichever
            // world happened to be built from the broken table.
            var converter = new MachineDef(new ContentId("ghost-converter"), BuildKind.Decomposer,
                TileKind.Decomposer, BehaviorKind.Converter, cost: 15, name: "Ghost",
                description: "", recipeId: new ContentId("no-such-recipe"), interval: 0f);

            Assert.Throws<ArgumentException>(() => BuildTable(machine: converter),
                "a converter naming a recipe the table does not hold cannot be constructed");

            var turret = new MachineDef(new ContentId("gunless"), BuildKind.Splitter, TileKind.Splitter,
                BehaviorKind.Turret, cost: 10, name: "Gunless", description: "",
                recipeId: ContentId.None, interval: 0f);

            Assert.Throws<ArgumentException>(() => BuildTable(machine: turret),
                "a turret row with no turret definition cannot be constructed - it would stand built, " +
                "unfireable, and uncomplaining (a splitter slot: no shipped gun lives there to copy)");
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

            var shapes = new ShapeDef[Enum.GetValues(typeof(ShapeType)).Length];
            foreach (ShapeType shape in (ShapeType[])Enum.GetValues(typeof(ShapeType)))
                shapes[(int)shape] = shipped.Shape(shape);

            var recipes = new[]
            {
                shipped.Recipe(ContentDatabase.DecomposeRecipeId),
                shipped.Recipe(ContentDatabase.BisectRecipeId),
            };

            return new ContentDatabase(machines, turrets, enemies, shapes, recipes);
        }
    }
}

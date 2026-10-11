using System.Collections.Generic;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// Draws every belt cell as one mesh of connected strips: each cell lays its centre hub, one
    /// lane toward its exit edge, and one lane from every side a feeding neighbour sits on - so a
    /// straight run reads as one continuous belt, a corner as a turn, and two lines meeting as one
    /// continuous shape, instead of a row of separate tiles. A lane that reaches a cell which eats (belt, machine,
    /// Core) docks into it; a lane to nowhere stops short, so a dangling end is visible.
    ///
    /// The bed only changes when a belt is laid, re-pointed or jammed, so this view rebuilds when
    /// the belt field's revision moves rather than every frame - its rebuild reason, declared in
    /// <see cref="Observe"/>.
    ///
    /// That is also why belts carry no outline: an outline is baked at a fixed pixel width, so it
    /// would be wrong at every zoom but the one it was built at. The gap between parallel lines
    /// separates the runs, and the chevron is what actually has to be read, so nothing is lost.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class BeltRenderer : MeshView
    {
        private static readonly Dir[] Sides = { Dir.North, Dir.East, Dir.South, Dir.West };

        private readonly List<BeltSnapshot> _belts = new List<BeltSnapshot>();
        private int _seenRevision = -1;

        /// <summary>A new world starts its belt revision at zero, which is a number this view may
        /// already have drawn - so the cache has to be dropped or the new map's belts would never
        /// appear.</summary>
        protected override void OnWorldRebound() => _seenRevision = -1;

        protected override Palette.Layer Layer => Colors.BeltLayer;

        /// <summary>Items moving deliberately do not bump the revision, so a rebuild means a cell
        /// was actually laid, re-pointed or jammed - not that the line is busy.</summary>
        protected override void Observe(in ViewFrame frame)
        {
            int revision = World.Belts.Revision;
            if (revision == _seenRevision) return;

            _seenRevision = revision;
            MarkDirty();
        }

        protected override void AppendFrame(in ViewFrame frame)
        {
            World.Belts.GetBelts(_belts);
            float w = Colors.Belts.HalfWidth;

            for (int i = 0; i < _belts.Count; i++)
            {
                BeltSnapshot belt = _belts[i];
                var centre = CellCentre(belt.Cell);
                Color bed = belt.Jammed ? Colors.Jam : Colors.Belt;

                // The hub every lane docks into. It is also the corner joint: an L's two lanes meet
                // under it, so a turn never shows a seam.
                AppendQuad(
                    new Vector2(centre.x - w, centre.y - w), new Vector2(centre.x + w, centre.y - w),
                    new Vector2(centre.x + w, centre.y + w), new Vector2(centre.x - w, centre.y + w), bed);

                // The exit lane: full length when the next cell eats the item, short when the run
                // just ends - a dangling half-lane is the "this goes nowhere" readout.
                Vec2 forward2 = belt.Direction.ToVec();
                var forward = new Vector2(forward2.X, forward2.Y);
                float exitLength = EatsFrom(belt.Cell + belt.Direction.Offset()) ? 0.5f : 0.32f;
                AppendLane(centre, centre + forward * exitLength, w, bed);

                // A lane from every side something feeds in from: belts pointing at me, and the
                // machines whose facing pushes onto me (drills and decomposers pushing out, a
                // splitter whose output port I am). A pipe lands on me from above instead - its own
                // tube, drawn by the machine view, is the visual for that hand-off.
                for (int s = 0; s < Sides.Length; s++)
                {
                    if (!DeliversInto(belt, Sides[s])) continue;
                    Vec2 side2 = Sides[s].ToVec();
                    var side = new Vector2(side2.X, side2.Y);
                    AppendLane(centre + side * 0.5f, centre, w, bed);
                }

                AppendChevron(belt);
            }
        }

        /// <summary>Would a belt on <paramref name="cell"/> deliver into this tile? Belts dock into
        /// each other whatever way the neighbour faces (a head-to-head pair is a visible mistake,
        /// not a gap), and into anything that eats: a machine or the Core. "Eats" is one rule in
        /// Core, so a new machine docks without this view being told about it.</summary>
        private bool EatsFrom(Int2 cell) => World.TileGrid.Get(cell).Eats();

        /// <summary>Does the neighbour on <paramref name="side"/> push items onto this belt?</summary>
        private bool DeliversInto(BeltSnapshot belt, Dir side)
        {
            Int2 neighbour = belt.Cell + side.Offset();

            if (World.Belts.TryGet(neighbour, out BeltState other) && other.Direction == side.Opposite())
                return true;

            // A machine pushes onto me when the side facing me is one of its computed output ports -
            // the same mask the simulation pushes through, not a second reading of the same rule.
            return World.Machines.TryGetSnapshot(neighbour, out MachineSnapshot machine)
                && machine.OutMask.Has(side.Opposite());
        }

        private void AppendChevron(BeltSnapshot belt)
        {
            var centre = CellCentre(belt.Cell);
            Palette.BeltLook look = Colors.Belts;
            Color color = belt.Jammed ? Colors.Outline : Colors.BeltArrow;

            AppendChevron(centre, belt.Direction, look.ChevronBack, look.ChevronHalfWidth,
                look.ChevronLength, color);
        }
    }
}

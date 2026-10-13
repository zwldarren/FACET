using Facet.Core;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>
    /// What a stroke paints. It is not a <see cref="TerrainKind"/> with extras bolted on: the Core and a
    /// door are not ground, they are the two things a map places on top of it, and erasing is a modifier
    /// rather than a brush - see <see cref="MapPainting.Stroke"/>.
    /// </summary>
    public enum MapBrush
    {
        Ground,
        Ore,
        Rock,
        Water,
        Void,
        Core,
        Door,
    }

    /// <summary>
    /// The one place a brush writes to a <see cref="MapAsset"/>. Two surfaces paint one - the
    /// <c>MapEditorWindow</c> grid and the Scene view's own editor - and a second answer to "what does the
    /// rock brush write" would be a map that reads differently depending on which one painted it.
    ///
    /// What is deliberately <em>not</em> here is anything about how a stroke is recorded or drawn: no
    /// <c>Undo</c>, no <c>EditorUtility</c>, no colour. Those belong to the surface that has buttons and a
    /// screen - and keeping them out is what lets this type live in <c>FACET.Game</c> beside the asset it
    /// writes, where the test assembly can reach it, instead of in the Editor assembly where only a scene
    /// view can.
    ///
    /// Two rules run through every call. A brush writes only inside the map: the ground brushes are
    /// bounded by the asset itself, and the two placements are bounded by the asset's own structural
    /// edits, which answer "changed" - a stroke dragged off the edge places nothing rather than a door
    /// the wave table could name and no wave could reach. And a vein is written in one place: its
    /// mineral's name goes into the asset's ore list through <c>PaintOre</c>, and the ground under it
    /// stays plain, because the ore list is what the conversion claims cells from. So the two states a
    /// half-write can leave a map in - an ore tile nothing can mine, and a mineral the list does not
    /// name - are the ones no brush here can reach.
    /// </summary>
    public static class MapPainting
    {
        /// <summary>Every brush, in the order both surfaces offer them: ground first, then the veil, then
        /// the two placements. Stated as one array so a brush added to the enum is added in one place -
        /// the alternative is two hand-written button rows that silently disagree about the set.</summary>
        public static readonly MapBrush[] Brushes =
        {
            MapBrush.Ground, MapBrush.Ore, MapBrush.Rock, MapBrush.Water, MapBrush.Void,
            MapBrush.Core, MapBrush.Door,
        };

        /// <summary>The brush's name, as a button and as a legend entry. The one place it is spelled.
        /// Exhaustive over the brush, so a brush the enum gains refuses to compile its way into
        /// borrowing another brush's name - the missing case is a missing label, not a door.</summary>
        public static string Label(MapBrush brush)
        {
            switch (brush)
            {
                case MapBrush.Ground: return "Ground";
                case MapBrush.Ore: return "Ore";
                case MapBrush.Rock: return "Rock";
                case MapBrush.Water: return "Water";
                case MapBrush.Void: return "Void";
                case MapBrush.Core: return "Core";
                case MapBrush.Door: return "Door";
                default: throw new ArgumentOutOfRangeException(nameof(brush), brush, "A brush beyond the ones the legend names.");
            }
        }

        /// <summary>True for the brushes that paint ground, which are the only ones a whole-map fill means
        /// anything for: a grid of doors, or of Core cells, is not something a map can hold.</summary>
        public static bool IsGround(MapBrush brush)
            => brush == MapBrush.Ground || brush == MapBrush.Ore || brush == MapBrush.Rock ||
               brush == MapBrush.Water || brush == MapBrush.Void;

        /// <summary>The mineral popup's label, the one word both surfaces spell for it. A GUIContent
        /// because the popup that filters the shapes takes one, and a field because a repaint must not
        /// allocate the label every pass.</summary>
        public static readonly GUIContent MineralLabel = new GUIContent("Mineral");

        /// <summary>The shapes an ore brush may be offered, as the popup filter both surfaces hand in.
        /// It is a data rule and not a styling choice: <see cref="MapAsset.PaintOre"/> writes nothing but
        /// a shape, so an enabled None in the popup would be a button for an empty stroke. Both surfaces
        /// drawing the brush read it here, so a shape the enum gains appears in the brush's hand exactly
        /// once.</summary>
        public static readonly Func<Enum, bool> ShapeChoices = shape => ((ShapeType)shape).IsShape();

        /// <summary>
        /// The ground a ground brush paints. Ore is not here: a vein is painted by its mineral's name,
        /// through <see cref="MapAsset.PaintOre"/>, which writes the list entry a vein is and leaves the
        /// ground plain. A brush that is not a ground brush answers plain ground, which is the only thing
        /// a caller that asked anyway could use - <see cref="Fill"/> refuses those brushes outright.
        /// </summary>
        public static TerrainKind GroundOf(MapBrush brush)
        {
            switch (brush)
            {
                case MapBrush.Rock: return TerrainKind.Rock;
                case MapBrush.Water: return TerrainKind.Water;
                case MapBrush.Void: return TerrainKind.Void;
                default: return TerrainKind.Ground;
            }
        }

        /// <summary>
        /// One cell of a brush, or - when <paramref name="erase"/> is set - "nothing here": plain ground,
        /// no mineral and no door. Erasing is a modifier rather than a brush of its own because that is
        /// what the middle of a map means, and because it is one answer for every brush in hand.
        ///
        /// The undo entry is the caller's, recorded once per stroke: a drag is one edit the designer made,
        /// and a record per cell would make Ctrl+Z step back a tile at a time.
        /// </summary>
        public static void Stroke(MapAsset asset, MapBrush brush, ShapeType ore, int x, int y, bool erase)
        {
            if (asset == null || !asset.InBounds(x, y)) return;

            if (erase)
            {
                asset.Erase(x, y);
                asset.TryRemoveDoor(x, y);
                return;
            }

            switch (brush)
            {
                case MapBrush.Core:
                    // The footprint's lower-left tile, which is what the field means and what the overlay
                    // of the Core that will be built is drawn from.
                    asset.SetCore(x, y);
                    break;

                case MapBrush.Door:
                    // A door on rock, water or void is a wave that spawns inside the ground - a map error
                    // the validator reports, and the brush allows it so the validator is the one place
                    // that has to say so. The dedupe and the bounds are the asset's own operation's:
                    // what a door is, has one answer whoever asks for one.
                    asset.TryAddDoor(x, y);
                    break;

                case MapBrush.Ore:
                    asset.PaintOre(x, y, ore);
                    break;

                default:
                    asset.Paint(x, y, GroundOf(brush));
                    break;
            }
        }

        /// <summary>
        /// Fill the whole map with the active ground brush. A brush that paints something other than
        /// ground fills nothing: the caller that offers the button states the same rule, and this is the
        /// half of it that holds even if a caller forgets.
        ///
        /// The writes are <see cref="Stroke"/>, cell by cell, inside one asset batch - so a fill and a
        /// stroke cannot produce different data, what a brush paints still has one answer - while the
        /// ore list the cells churn is written back once at the end of the sweep rather than once per
        /// cell painted, which is the difference between a fill and a scan inside a scan on a full map.
        /// </summary>
        public static void Fill(MapAsset asset, MapBrush brush, ShapeType ore)
        {
            if (asset == null || !IsGround(brush)) return;

            asset.BeginBatch();
            try
            {
                for (int y = 0; y < asset.Height; y++)
                {
                    for (int x = 0; x < asset.Width; x++) Stroke(asset, brush, ore, x, y, erase: false);
                }
            }
            finally
            {
                asset.EndBatch();
            }
        }

    }
}

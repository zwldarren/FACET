using Facet.Core;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>What a stroke paints. Not a <see cref="TerrainKind"/> with extras: the Core and a door are
    /// the two things a map places on top of ground, and erasing is a modifier rather than a brush - see
    /// <see cref="MapPainting.Stroke"/>.</summary>
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

    /// <summary>The one place a brush writes to a <see cref="MapAsset"/>. Two surfaces paint one - the
    /// <c>MapEditorWindow</c> grid and the Scene view's own editor - and a second answer to "what does the rock
    /// brush write" would be a map that reads differently depending on which painted it. What is deliberately
    /// <em>not</em> here is anything about how a stroke is recorded or drawn (no <c>Undo</c>, no
    /// <c>EditorUtility</c>, no colour): that belongs to the surface with buttons and a screen, and keeping it out
    /// lets this type live in <c>FACET.Game</c> beside the asset it writes, reachable from the test assembly.
    ///
    /// Two rules run through every call: a brush writes only inside the map - ground brushes bounded by the
    /// asset, placements bounded by the asset's own structural edits, which answer "changed" - so a stroke
    /// dragged off the edge places nothing; and a vein is written in one place, its mineral's name into the
    /// asset's ore list through <c>PaintOre</c> with the ground left plain, because the ore list is what the
    /// conversion claims cells from. The two states a half-write could leave (an unmineable ore tile, a
    /// mineral the list does not name) are therefore unreachable from here.</summary>
    public static class MapPainting
    {
        /// <summary>Every brush, in the order both surfaces offer them: ground first, then the veil, then
        /// the two placements. One array so a brush added to the enum is added in one place.</summary>
        public static readonly MapBrush[] Brushes =
        {
            MapBrush.Ground, MapBrush.Ore, MapBrush.Rock, MapBrush.Water, MapBrush.Void,
            MapBrush.Core, MapBrush.Door,
        };

        /// <summary>The brush's name, as a button and as a legend entry - the one place it is spelled.
        /// Exhaustive over the brush, so a new brush must be given a label rather than borrowing another's.</summary>
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

        /// <summary>True for the brushes that paint ground - the only ones a whole-map fill means anything
        /// for, since a grid of doors or Core cells is not something a map can hold.</summary>
        public static bool IsGround(MapBrush brush)
            => brush == MapBrush.Ground || brush == MapBrush.Ore || brush == MapBrush.Rock ||
               brush == MapBrush.Water || brush == MapBrush.Void;

        /// <summary>The mineral popup's label, the one word both surfaces spell for it. A GUIContent because
        /// the filtering popup takes one, and a field so a repaint does not allocate it every pass.</summary>
        public static readonly GUIContent MineralLabel = new GUIContent("Mineral");

        /// <summary>The minerals an ore brush may be offered, as the popup filter both surfaces hand in. A
        /// data rule, not styling: <see cref="MapAsset.PaintOre"/> writes nothing but a mineral, so an
        /// enabled Empty would be a button for an empty stroke. Both surfaces read it here, so a new
        /// mineral appears exactly once.</summary>
        public static readonly Func<Enum, bool> MineralChoices = mineral => (Quad)mineral != Quad.Empty;

        /// <summary>The ground a ground brush paints. Ore is not here: a vein is painted by its mineral's
        /// name through <see cref="MapAsset.PaintOre"/>, leaving the ground plain. A non-ground brush
        /// answers plain ground, the only thing a caller that asked anyway could use - <see cref="Fill"/>
        /// refuses those brushes.</summary>
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

        /// <summary>One cell of a brush, or - when <paramref name="erase"/> is set - "nothing here": plain
        /// ground, no mineral and no door. Erasing is a modifier because that is what the middle of a map
        /// means and because it is one answer for every brush in hand. The undo entry is the caller's,
        /// recorded once per stroke: a record per cell would make Ctrl+Z step back a tile at a time.</summary>
        public static void Stroke(MapAsset asset, MapBrush brush, Quad ore, int x, int y, bool erase)
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
                    // The footprint's lower-left tile - what the field means and what the built Core's overlay draws from.
                    asset.SetCore(x, y);
                    break;

                case MapBrush.Door:
                    // A door on rock, water or void is a wave spawning inside the ground - a map error the validator reports;
                    // the brush allows it so the validator is the one place that says so. Dedupe and bounds belong to the
                    // asset's own operation: what a door is has one answer whoever asks.
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

        /// <summary>Fill the whole map with the active ground brush. A non-ground brush fills nothing: the
        /// caller that offers the button states the same rule, and this is the half that holds even if it
        /// forgets. The writes are <see cref="Stroke"/>, cell by cell, inside one asset batch, so a fill and
        /// a stroke cannot produce different data; the ore list the cells churn is written back once at the
        /// sweep's end rather than once per cell.</summary>
        public static void Fill(MapAsset asset, MapBrush brush, Quad ore)
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

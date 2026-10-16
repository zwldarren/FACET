using System;

namespace Facet.Game
{
    /// <summary>How many fixed-step ticks the host feeds the simulation each frame, and at which speed gear.
    ///
    /// The gear is the host's, like pause is the player's: it scales how much real time is poured into the
    /// accumulator, never <see cref="Facet.Core.SimConfig.TickDt"/>, so the simulation still advances in the
    /// same 30 Hz steps and a run is the same tick sequence at 1x and 3x. Nothing in Core reads it.
    ///
    /// Plain C# with no engine reference, so the gear arithmetic is tested from EditMode without a scene.
    /// The gear outlives worlds: a map change or a restart drops the tick backlog but keeps the gear the
    /// player picked.</summary>
    public sealed class TickClock
    {
        /// <summary>The selectable gears, slowest first. 1x is the default.</summary>
        public static readonly int[] Gears = { 1, 2, 3 };

        /// <summary>The catch-up cap at 1x. Scaled by the gear, so a slow frame at 3x still runs its three
        /// frames' worth of ticks, while a long hitch is still dropped rather than repaid.</summary>
        public const int BaseMaxTicksPerFrame = 5;

        /// <summary>Position in <see cref="Gears"/>.</summary>
        public int GearIndex { get; private set; }

        /// <summary>The current speed multiplier: 1, 2 or 3.</summary>
        public int Gear => Gears[GearIndex];

        /// <summary>The gear as the HUD prints it ("1x").</summary>
        public string Label => Gear + "x";

        /// <summary>The most ticks one frame may run at this gear.</summary>
        public int MaxTicksPerFrame => BaseMaxTicksPerFrame * Gear;

        /// <summary>Simulation seconds owed but not yet ticked. Below one tick after every
        /// <see cref="Advance"/>, unless a hitch was dropped (then zero).</summary>
        public float Accumulator { get; private set; }

        /// <summary>The speed the run actually reached over the last second of real time, as a multiple of
        /// 1x. Below the gear when the frame rate cannot feed it (a slow browser at 3x): the cap drops the
        /// backlog, so the run plays slower than asked rather than spiralling.</summary>
        public float EffectiveSpeed { get; private set; } = 1f;

        /// <summary>Is the run clearly slower than the gear asks for? The HUD marks the gear when it is.</summary>
        public bool FallingBehind => EffectiveSpeed < Gear * 0.9f;

        /// <summary>The window <see cref="EffectiveSpeed"/> is measured over, in real seconds.</summary>
        public const float MeasureWindow = 1f;

        private float _windowReal;
        private int _windowTicks;

        /// <summary>One gear faster; stays at the top gear.</summary>
        public void Up() => SetGearIndex(GearIndex + 1);

        /// <summary>One gear slower; stays at 1x.</summary>
        public void Down() => SetGearIndex(GearIndex - 1);

        /// <summary>The next gear, wrapping from the top back to 1x: what the HUD button does.</summary>
        public void Cycle() => SetGearIndex((GearIndex + 1) % Gears.Length);

        /// <summary>Change gear, clamped to the table. A change restarts the speed measurement, so the
        /// HUD does not flag a fresh 3x as "behind" off a window that was mostly 1x.</summary>
        private void SetGearIndex(int index)
        {
            index = Math.Max(0, Math.Min(index, Gears.Length - 1));
            if (index == GearIndex) return;
            GearIndex = index;
            EffectiveSpeed = Gear;
            _windowReal = 0f;
            _windowTicks = 0;
        }

        /// <summary>Pour one frame of real time in, at the current gear, and say how many ticks to run.
        /// The caller runs exactly that many <c>SimWorld.Tick</c> calls.</summary>
        public int Advance(float realDeltaSeconds, float tickDt)
        {
            Accumulator += realDeltaSeconds * Gear;

            int ticks = 0;
            int max = MaxTicksPerFrame;
            while (Accumulator >= tickDt && ticks < max)
            {
                Accumulator -= tickDt;
                ticks++;
            }

            // Drop the leftover backlog rather than trying to catch up forever.
            if (ticks >= max) Accumulator = 0f;

            _windowReal += realDeltaSeconds;
            _windowTicks += ticks;
            if (_windowReal >= MeasureWindow)
            {
                EffectiveSpeed = _windowTicks * tickDt / _windowReal;
                _windowReal = 0f;
                _windowTicks = 0;
            }
            return ticks;
        }

        /// <summary>How far the views are between the last tick and the next, 0..1.</summary>
        public float Alpha(float tickDt) => Math.Max(0f, Math.Min(1f, Accumulator / tickDt));

        /// <summary>Forget any owed time: a new world starts on a tick boundary. The gear is kept.</summary>
        public void ResetBacklog() => Accumulator = 0f;
    }
}

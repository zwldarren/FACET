using System;
using Facet.Core;
using UnityEngine;

namespace Facet.Game
{
    /// <summary>View-only easing and damage classification. No random or simulation state is changed.</summary>
    public static class ViewFeedback
    {
        public static float EaseOutQuad(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t);
        }

        public static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t) - 1f;
            return 1f + 2.70158f * t * t * t + 1.70158f * t * t;
        }

        /// <summary>The current roster has one gun. Read THIS run's base damage, never a literal 4/2.
        /// Return 0 for an unknown amount, rather than mislabelling a custom damage source.</summary>
        public static int DamageMarks(float damage, float baseDamage)
        {
            if (baseDamage <= 0f) return 0;
            if (Mathf.Approximately(damage, baseDamage * Balance.WeaknessDamageMultiplier)) return 2;
            if (Mathf.Approximately(damage, baseDamage * Balance.ResistantDamageMultiplier)) return 1;
            return 0;
        }
    }

    /// <summary>A bounded view-owned event history. Ring positions identify events even when many share a
    /// tick or the simulation's ring wraps. Ticks are NOT animation time; Seconds is the render clock.
    /// Reading never clears or otherwise writes the simulation's stream.</summary>
    public sealed class FeedbackEvents
    {
        public readonly struct Entry
        {
            public readonly SimEvent Event;
            public readonly float Seconds;
            public Entry(SimEvent value, float seconds) { Event = value; Seconds = seconds; }
        }

        private readonly Entry[] _entries;
        private int _next;
        private int _count;
        private long _seen;
        private SimEventBuffer _source;
        public int Count => _count;
        public Entry this[int index] => _entries[(_next - 1 - index + _entries.Length) % _entries.Length];

        public FeedbackEvents(int capacity = 128) => _entries = new Entry[Math.Max(1, capacity)];

        /// <summary>Bind at the current end: a view opened mid-run must not replay old effects.</summary>
        public void Reset(SimEventBuffer events)
        {
            _next = _count = 0;
            _source = events;
            _seen = (long)events.Dropped + events.Count;
        }

        public void Capture(SimEventBuffer events, float seconds, int kinds, bool enabled)
        {
            long end = (long)events.Dropped + events.Count;
            if (!ReferenceEquals(_source, events) || end < _seen) Reset(events);
            int first = (int)Math.Max(0L, _seen - events.Dropped);
            for (int i = first; i < events.Count; i++)
            {
                SimEvent e = events[i];
                if (!enabled || (kinds & (1 << (int)e.Kind)) == 0) continue;
                _entries[_next] = new Entry(e, seconds);
                _next = (_next + 1) % _entries.Length;
                _count = Math.Min(_count + 1, _entries.Length);
            }
            _seen = end;
            if (!enabled) _next = _count = 0;
        }

        public bool Latest(SimEventKind kind, Int2 cell, out Entry found)
        {
            for (int i = 0; i < _count; i++)
            {
                Entry e = this[i];
                if (e.Event.Cell != cell) continue;
                if (kind == SimEventKind.ShotFired && (e.Event.Kind == SimEventKind.Built ||
                    e.Event.Kind == SimEventKind.Removed || e.Event.Kind == SimEventKind.BuildingDestroyed)) break;
                if (e.Event.Kind != kind) continue;
                found = e;
                return true;
            }
            found = default;
            return false;
        }

        public bool Hit(int enemyId, out Entry found)
        {
            for (int i = 0; i < _count; i++)
            {
                Entry e = this[i];
                if (e.Event.Kind != SimEventKind.EnemyDamaged || e.Event.EnemyId != enemyId) continue;
                found = e;
                return true;
            }
            found = default;
            return false;
        }
    }
}

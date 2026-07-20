/*
Ferram Aerospace Research
=========================
Fast per-thread slot assignment for the parallel editor sweep.

   This file is part of Ferram Aerospace Research.

   Ferram Aerospace Research is free software: you can redistribute it and/or modify
   it under the terms of the GNU General Public License as published by
   the Free Software Foundation, either version 3 of the License, or
   (at your option) any later version.
 */

using System;
using System.Collections.Concurrent;

// ReSharper disable once CheckNamespace
namespace ferram4
{
    /// <summary>
    /// Hands each parallel unit of work a small dense slot index so the aero solve can hold per-thread
    /// scratch in plain arrays indexed by <see cref="Slot"/>. This replaces <see cref="System.Threading.ThreadLocal{T}"/>
    /// on the hot path: under KSP's Mono runtime ThreadLocal.Value is slow enough that reading it
    /// millions of times per sweep erased (and reversed) the parallel speedup. A <c>[ThreadStatic]</c>
    /// int plus an array index is effectively free.
    ///
    /// Slots come from a POOL: each row acquires one with <see cref="AcquireSlot"/> on entry and returns
    /// it with <see cref="ReleaseSlot"/> on exit. So the live-slot count is bounded by the CONCURRENCY,
    /// not by how many distinct threads the pool cycles through — which matters because an
    /// allocation-heavy sweep makes the thread pool inject and retire workers over its lifetime. A
    /// per-thread counter would grow past the array size and two threads would end up sharing a slot,
    /// corrupting rows; the pool never hands the same slot to two live workers.
    ///
    /// Serial callers (the stability sweep, the static/stability tabs, flight aero) never acquire and
    /// simply read slot 0 — safe because they are single-threaded.
    /// </summary>
    public static class SimThreadContext
    {
        /// <summary>Upper bound the scratch arrays are sized to. The pool only uses ~concurrency of them.</summary>
        public const int MaxSlots = 64;

        [ThreadStatic] private static int slot;
        [ThreadStatic] private static bool holdsSlot;

        private static readonly ConcurrentStack<int> pool = new ConcurrentStack<int>();

        /// <summary>The calling thread's current slot; 0 when it holds none (serial contexts).</summary>
        public static int Slot => slot;

        /// <summary>
        /// (Re)fills the pool for a sweep with one slot per possible concurrent worker plus a small
        /// margin. Main thread, before the parallel loop. The margin means <see cref="AcquireSlot"/>
        /// never comes up empty, so no worker is ever forced to share.
        /// </summary>
        public static void BeginSweep()
        {
            int poolSize = Math.Max(1, Math.Min(MaxSlots, Environment.ProcessorCount + 2));
            while (pool.TryPop(out int _))
            {
            }

            for (int i = poolSize - 1; i >= 0; i--)
                pool.Push(i);

            slot = 0;
            holdsSlot = false;
        }

        /// <summary>Claims a slot for this thread for the duration of one parallel iteration.</summary>
        public static void AcquireSlot()
        {
            if (pool.TryPop(out int s))
            {
                slot = s;
                holdsSlot = true;
            }
            else
            {
                // Pool is sized above the worker count, so this should not happen; fall back to slot 0
                // (which exists) rather than an out-of-range index, and do not later push a fake slot.
                slot = 0;
                holdsSlot = false;
            }
        }

        /// <summary>Returns this thread's slot to the pool. Pair with <see cref="AcquireSlot"/>.</summary>
        public static void ReleaseSlot()
        {
            if (holdsSlot)
            {
                pool.Push(slot);
                holdsSlot = false;
            }

            slot = 0;
        }
    }
}

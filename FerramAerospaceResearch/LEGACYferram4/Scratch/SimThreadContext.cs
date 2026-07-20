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
using System.Threading;

// ReSharper disable once CheckNamespace
namespace ferram4
{
    /// <summary>
    /// Assigns each thread a small dense slot index so the aero solve can hold per-thread scratch in
    /// plain arrays indexed by <see cref="Slot"/>. This replaces <see cref="ThreadLocal{T}"/> on the
    /// hot path: under KSP's Mono runtime ThreadLocal.Value is slow enough that reading it millions of
    /// times per sweep across many worker threads erased (and reversed) the parallel speedup. A
    /// <c>[ThreadStatic]</c> int plus an array index is effectively free.
    ///
    /// Slots are handed out lazily via an interlocked counter and invalidated between sweeps by an
    /// epoch bump, so thread-pool threads reused across sweeps re-acquire a fresh slot. The main thread
    /// takes the first slot each sweep (it calls <see cref="BeginSweep"/> then touches Slot first).
    /// </summary>
    public static class SimThreadContext
    {
        /// <summary>Hard upper bound on concurrent solver threads; scratch arrays are sized to this.</summary>
        public const int MaxSlots = 64;

        [ThreadStatic] private static int slot;
        [ThreadStatic] private static int slotEpoch;

        private static int epoch;
        private static int nextSlot;
        private static int slotCap = MaxSlots;

        /// <summary>
        /// The active slot ceiling for this sweep, clamped to [1, MaxSlots]. A sweep sizes its per-slot
        /// resources (e.g. the engine solver clone pool) to this and caps its worker count just below,
        /// so no thread is ever handed a slot past what was allocated. Defaults to MaxSlots.
        /// </summary>
        public static int SlotCap
        {
            get => slotCap;
            set => slotCap = Math.Max(1, Math.Min(MaxSlots, value));
        }

        public static int Slot
        {
            get
            {
                if (slotEpoch != epoch)
                {
                    int s = Interlocked.Increment(ref nextSlot) - 1;
                    slot = s < slotCap ? s : slotCap - 1;
                    slotEpoch = epoch;
                }

                return slot;
            }
        }

        /// <summary>Starts a new slot epoch. Call on the main thread before spawning worker threads.</summary>
        public static void BeginSweep()
        {
            nextSlot = 0;
            epoch++;
        }
    }
}

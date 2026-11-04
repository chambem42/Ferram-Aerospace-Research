using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FerramAerospaceResearch.FARAeroComponents;
using FerramAerospaceResearch.FARGUI.FAREditorGUI;
using FerramAerospaceResearch.FARPartGeometry;
using FerramAerospaceResearch.FARThreading;
using HarmonyLib;
using KspTest;
using static FerramAerospaceResearch.InGameTests.Refl;

namespace FerramAerospaceResearch.InGameTests
{
    /// <summary>
    /// FAR's pool of voxel chunks, shared by every voxel model in the game. Each model built takes its chunks from the
    /// pool and gives them back when it is done with. A chunk given back twice, or given back while a model still uses
    /// it, ends up in two places of a model at once, and from then on every voxelization throws (IndexOutOfRange in
    /// VoxelChunk) until the game restarts.
    /// </summary>
    public static class VoxelPoolTests
    {
        private const string Airliner = "Stearwing A300";
        private const string HarmonyId = "FerramAerospaceResearch.InGameTests.VoxelPool";

        /// <summary>
        /// The editor gives the old model's chunks back when it queues a new voxelization. Leaving the editor while
        /// the new model is being built (launching right after a change, say) must not give them back again: by then
        /// the new model has them. The new model is held on FAR's voxelization thread as it comes out of
        /// VehicleVoxel.CreateNewVoxel while the test leaves the editor, so the voxelization is always still running
        /// when the editor closes. Then no free chunk is one the held model uses, none is free twice, and once it is
        /// let go both models' chunks are given back, each once, with no FAR exception.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = Airliner, TimeoutSec = 900)]
        public static IEnumerator Editor_LeftWhileVoxelizing(TestContext ctx)
        {
            bool wasMain = VoxelizationThreadpool.RunInMainThread;
            var harmony = new Harmony(HarmonyId);
            var log = new ExceptionLog();
            var pool = (Stack<VoxelChunk>)GetStatic(typeof(VehicleVoxel), "clearedChunks");
            try
            {
                // held on the main thread, the model would hold the game
                VoxelizationThreadpool.RunInMainThread = false;
                harmony.Patch(AccessTools.Method(typeof(VehicleVoxel), nameof(VehicleVoxel.CreateNewVoxel)),
                              postfix: new HarmonyMethod(typeof(Hold), nameof(Hold.Postfix)));

                yield return Far.WaitForEditorVoxels();
                yield return Far.Settled("the airliner");
                VehicleAerodynamics aero = Far.EditorVehicle;
                var old = Get<VehicleVoxel>(aero, "_voxel");
                Assert.True(old != null, "the editor keeps the airliner's voxel model");
                int oldChunks = ChunkCount(old), inUse = InUse();

                ctx.Step("Voxelized again, the new model held on FAR's voxelization thread");
                Hold.Arm();
                EditorGUI.RequestUpdateVoxel();
                yield return Wait.Until(() => Hold.Reached.WaitOne(0), 120, "FAR's voxelization thread to build the new model", shot: false);
                VehicleVoxel built = Hold.Built;
                var usedByBuilt = new HashSet<VoxelChunk>(Chunks(built));

                ctx.Step("Left the editor with the new model still held");
                yield return Nav.ToSpaceCenter(ctx);
                VoxelChunk[] free;
                lock (pool)
                    free = pool.ToArray();
                int shared = free.Count(usedByBuilt.Contains), twice = free.Length - new HashSet<VoxelChunk>(free).Count;
                ctx.Record("pool", $"{free.Length} free chunks, {twice} of them free twice, {shared} of them in the held model " +
                                   $"({usedByBuilt.Count} chunks); the old model had {oldChunks}");
                Assert.Equal(0, shared, "no free chunk is one the held model uses");
                Assert.Equal(0, twice, "no chunk free twice");

                ctx.Step("The held model let go");
                Hold.Release.Set();
                yield return Wait.Until(() => !aero.Voxelizing, 120, "FAR's voxelization to end", shot: false);
                // FAR logs a voxelization thread's exception from the main thread
                yield return Wait.Seconds(2);
                int after = InUse();
                ctx.Record("chunks in use", $"{inUse} with the old model, {after} once both are given back (the old model had {oldChunks})");
                Assert.Equal(inUse - oldChunks, after, "both models' chunks given back, each once");
                log.AssertNone("leaving the editor while it voxelizes");
            }
            finally
            {
                Hold.Disarm();
                log.Dispose();
                harmony.UnpatchAll(HarmonyId);
                VoxelizationThreadpool.RunInMainThread = wasMain;
            }
        }

        private static int InUse() => (int)GetStatic(typeof(VehicleVoxel), "chunksInUse");

        private static int ChunkCount(VehicleVoxel v) => Get<int>(v, "xLength") * Get<int>(v, "yLength") * Get<int>(v, "zLength");

        private static IEnumerable<VoxelChunk> Chunks(VehicleVoxel v) =>
            Get<VoxelChunk[,,]>(v, "voxelChunks").Cast<VoxelChunk>().Where(c => c != null);

        /// <summary>The next model VehicleVoxel.CreateNewVoxel returns, held on its thread until released.</summary>
        private static class Hold
        {
            private static int armed;
            public static volatile VehicleVoxel Built;
            public static readonly ManualResetEvent Reached = new ManualResetEvent(false);
            public static readonly ManualResetEvent Release = new ManualResetEvent(false);

            public static void Arm()
            {
                Built = null;
                Reached.Reset();
                Release.Reset();
                Interlocked.Exchange(ref armed, 1);
            }

            public static void Disarm()
            {
                Interlocked.Exchange(ref armed, 0);
                Release.Set();
            }

            public static void Postfix(VehicleVoxel __result)
            {
                if (Interlocked.Exchange(ref armed, 0) == 0)
                    return;
                Built = __result;
                Reached.Set();
                // never for good: the test lets it go when it ends however it ends (the harness disposes a test it
                // stops), and so does this, well before a voxelization the next test waits on gives up
                Release.WaitOne(TimeSpan.FromSeconds(60));
            }
        }
    }
}

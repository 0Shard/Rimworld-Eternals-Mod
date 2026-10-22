// Author: 0Shard
// Description: Compiled real-map/tick boundaries and strict profile validation; no host runtime claim.
using System;
using System.IO;
using System.Linq;
using Eternal.Tests.UI;
using Xunit;
#if DEBUG
using System.Reflection.Emit;
using Eternal.InGameTests.Performance;
#endif

namespace Eternal.Tests.Infrastructure
{
    public class IsolatedPerformanceBoundaryTests
    {
        private const string Suite = "Eternal.InGameTests.Performance.PerformanceSuite";
        private const string Maps = "Eternal.InGameTests.Performance.TestMapScope";
        private const string Palette = "Eternal.InGameTests.Performance.PerformanceStatePalette";

        [Fact]
        public void ReleaseExcludesIsolatedMapHarness()
        {
#if DEBUG
            Assert.NotNull(MenuAssemblyContract.BaseType(Maps));
#else
            Assert.Null(MenuAssemblyContract.BaseType(Maps));
            Assert.Null(MenuAssemblyContract.BaseType(Suite));
            Assert.Null(MenuAssemblyContract.BaseType(Palette));
            Assert.Null(MenuAssemblyContract.BaseType("Eternal.InGameTests.Performance.PerformanceConfiguration"));
#endif
        }
#if DEBUG
        [Fact]
        public void FixtureUsesNativeGeneratorAndOwnedNativeCleanup()
        {
            var creation = MenuAssemblyContract.ReachableInstructions(Maps, "CreateWithParent");
            Assert.Contains(creation, i => Equals(i.Operand, "Verse.MapGenerator.GenerateMap"));
            Assert.DoesNotContain(creation, i => i.Code == OpCodes.Newobj && Equals(i.Operand, "Verse.Map..ctor"));
            var cleanup = MenuAssemblyContract.ReachableInstructions(Maps, "Dispose");
            Assert.Contains(cleanup, i => Equals(i.Operand, "Verse.Game.DeinitAndRemoveMap"));
            Assert.Contains(cleanup, i => Equals(i.Operand, "RimWorld.Planet.WorldObjectsHolder.Remove"));
            Assert.DoesNotContain(cleanup, i => Equals(i.Operand, "Eternal.InGameTests.Helpers.TestPawnFactory.CleanupAll"));
            var protectedCleanup = MenuAssemblyContract.ReachableInstructions(Maps, "Dispose", true);
            Assert.Contains(protectedCleanup, i => Equals(i.Operand, "HarmonyLib.Harmony.UnpatchAll"));
        }

        [Fact]
        public void NativePreparationCallbacksExposeInternalStagesOutsideMeasuredDrive()
        {
            Assert.Contains(4, MenuAssemblyContract.ParameterCounts(Maps, "Create"));
            Assert.Contains(4, MenuAssemblyContract.ParameterCounts(Maps, "CreateWithParent"));
            var create = MenuAssemblyContract.ReachableInstructions(Maps, "Create");
            Assert.Contains(create, i => (i.Operand as string)?.EndsWith(".Invoke") == true);
            Assert.DoesNotContain(create, i => i.Code == OpCodes.Stsfld && (i.Operand as string)?.Contains("preparationProgress") == true);
            var drive = MenuAssemblyContract.Instructions(Suite, "Drive");
            Assert.DoesNotContain(drive, i => (i.Operand as string)?.Contains("WritePreparationProgress") == true
                || (i.Operand as string)?.StartsWith(Maps + ".Create") == true);
        }

        [Fact]
        public void HostAdvancesSuiteIteratorOncePerUpdateAndReleasesItOnFinish()
        {
            const string host = "Eternal.InGameTests.Helpers.DevelopmentGameHost";
            Assert.Contains("IteratorStateMachineAttribute", MenuAssemblyContract.MethodAttributeText(Suite, "RunAll"));
            var poll = MenuAssemblyContract.Instructions(host, "PollBenchmark");
            Assert.Single(poll.Where(i => (i.Operand as string)?.EndsWith(".MoveNext") == true));
            var update = MenuAssemblyContract.Instructions(host, "Update");
            int pollIndex = update.FindIndex(i => Equals(i.Operand, host + ".PollBenchmark"));
            int activeWait = update.FindIndex(i => Equals(i.Operand, "Verse.LongEventHandler.get_ForcePause"));
            int initialWait = update.FindIndex(pollIndex + 1,
                i => Equals(i.Operand, "Verse.LongEventHandler.get_ForcePause"));
            Assert.True(activeWait >= 0 && activeWait < pollIndex,
                "Standard-window loading events pause native ticks and must finish before advancing a profile.");
            Assert.True(pollIndex >= 0 && initialWait > pollIndex,
                "An active iterator must be polled before initial scene-control gates can strand terminal completion.");
            Assert.DoesNotContain(update, i => (i.Operand as string)?.EndsWith(".MoveNext") == true);
            Assert.Contains(MenuAssemblyContract.ReachableInstructions(host, "Finish", true),
                i => Equals(i.Operand, host + ".ReleaseBenchmark"));
            Assert.Contains(MenuAssemblyContract.Instructions(host, "ReleaseBenchmark"),
                i => (i.Operand as string)?.EndsWith(".Dispose") == true);
        }

        [Fact]
        public void TimingDrivesNativeTicksAndActualFrameCallback()
        {
            var drive = MenuAssemblyContract.Instructions(Suite, "Drive");
            Assert.Contains(drive, i => Equals(i.Operand, "Verse.TickManager.DoSingleTick"));
            Assert.Contains(drive, i => Equals(i.Operand, "Verse.TickManager.set_CurTimeSpeed"));
            Assert.Contains(MenuAssemblyContract.ReachableInstructions(Suite, "Drive", true),
                i => Equals(i.Operand, "Verse.TickManager.set_CurTimeSpeed"));
            Assert.DoesNotContain(drive, i => Equals(i.Operand, "Verse.GameComponent.GameComponentUpdate"));
            Assert.Contains(MenuAssemblyContract.Calls("Eternal.Eternal_Component", "GameComponentTick"),
                call => call == "Eternal.Components.TickOrchestrator.ProcessTick");
            Assert.Contains(drive, i => Equals(i.Operand, "System.Diagnostics.Stopwatch.GetTimestamp"));
            Assert.Contains(drive, i => Equals(i.Operand, "System.GC.CollectionCount"));
            Assert.DoesNotContain(drive, i => (i.Operand as string)?.Contains("GetTotalMemory") == true);
            Assert.DoesNotContain(drive, i => (i.Operand as string)?.Contains("DebugSetTicksGame") == true);
            Assert.DoesNotContain(drive, i => (i.Operand as string)?.Contains("Observe") == true);
        }

        [Fact]
        public void MeasuredBatchesRequireActualProductionCadence()
        {
            var run = MenuAssemblyContract.ReachableInstructions(Suite, "RunProfile");
            Assert.Contains(run, i => Equals(i.Operand, Suite + ".AssertProductionCadence"));
            var cadence = MenuAssemblyContract.Instructions(Suite, "AssertProductionCadence");
            Assert.Contains(cadence, i => i.Code == OpCodes.Ldstr && Equals(i.Operand, "ProcessNormalTick"));
            Assert.Contains(cadence, i => i.Code == OpCodes.Ldstr && Equals(i.Operand, "ProcessRareTick"));
            Assert.Contains(cadence, i => Equals(i.Operand, "Eternal.InGameTests.TestAssert.IsTrue"));
        }

        [Fact]
        public void StatePreparationUsesProductionDeathRegrowthAndResurrection()
        {
            var preparation = MenuAssemblyContract.ReachableInstructions(Palette, "Prepare");
            foreach (var boundary in new[] { "Eternal.InGameTests.Helpers.TestPawnFactory.KillPawn",
                "Eternal.EternalRegrowthManager.StartRegrowth", "Verse.Pawn_HealthTracker.AddHediff",
                "Verse.RoofCollapserImmediate.DropRoofInCells", "RimWorld.FireUtility.TryAttachFire",
                "Eternal.Healing.EternalCorpseHealingProcessor.StartCorpseHealing" })
            {
                Assert.Contains(preparation, i => Equals(i.Operand, boundary));
            }

            Assert.DoesNotContain(preparation, i => (i.Operand as string)?.Contains("ResurrectionUtility.TryResurrect") == true);
        }

        [Theory]
        [InlineData("0,1", "1")]
        [InlineData("1,4", "1")]
        [InlineData("0,4", "1")]
        [InlineData("0,1,4", "0")]
        public void InvalidScheduleAndBoundsFailClosed(string steps, string population)
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, Config(steps, population));
                Assert.Throws<FormatException>(() => PerformanceConfiguration.Load(path));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void ValidSchedulePreservesRepeatsAndJumps()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, Config("0,1,4,0,250", "100"));
                var config = PerformanceConfiguration.Load(path);
                Assert.Equal(new[] { 0, 1, 4, 0, 250 }, config.FrameSteps);
                Assert.Equal(100, config.Profiles[0].Population);
                Assert.Equal(75, config.Profiles[0].MapSize);
            }
            finally { File.Delete(path); }
        }
        private static string Config(string steps, string population)
        {
            return "<gameTest><performance seed='1' warmupFrames='1' batchFrames='2' batches='2' samples='1' " +
                "eventEveryBatches='1' deathValidationTicks='10' resurrectionValidationTicks='20' outputDirectory='report' " +
                "frameSteps='" + steps + "'><profile name='control' population='" + population +
                "' mapSize='75' emptyControl='false'/></performance></gameTest>";
        }
#endif
    }
}

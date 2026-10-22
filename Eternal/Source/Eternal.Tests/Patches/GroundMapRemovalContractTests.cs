// Author: 0Shard
// Description: Compiled early recovery entrypoints and failure-test native wiring; no game runtime claims.
using System;
using System.Linq;
using System.Reflection.Emit;
using Eternal.Tests.Infrastructure;
using Xunit;

namespace Eternal.Tests.Patches
{
    public class GroundMapRemovalContractTests
    {
        private const string Protection = "Eternal.Corpse.EternalMapProtection";
        private const string Guard = "Eternal.Patches.GroundMapRemovalGuard";
        private const string Suite = "Eternal.InGameTests.FailureScenarios.MapRemovalFailureTests";

        [Fact]
        public void CorpseAttachmentInitializesNativeOwnershipBeforePublishingTrackingState()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var il = assembly.Instructions(assembly.Method("Eternal.Corpse.EternalCorpseManager", "AddCorpseComponent"));
            int parent = il.FindIndex(i => i.Code == OpCodes.Stfld && Equals(i.Operand, "Verse.ThingComp.parent"));
            int properties = il.FindIndex(i => i.Code == OpCodes.Newobj
                && Equals(i.Operand, "Eternal.Corpse.CompProperties_EternalCorpse..ctor"));
            int initialize = il.FindIndex(i => Equals(i.Operand, "Verse.ThingComp.Initialize"));
            int tracking = il.FindIndex(i => Equals(i.Operand, "Eternal.Corpse.EternalCorpseComponent.set_CorpseData"));
            Assert.True(parent >= 0 && initialize > parent && tracking > initialize);
            Assert.True(properties >= 0 && initialize > properties);
            Assert.Contains(il, i => Equals(i.Operand, "System.Reflection.FieldInfo.GetValue"));
            Assert.Contains(il, i => Equals(i.Operand, "System.Reflection.FieldInfo.SetValue"));
            Assert.DoesNotContain(il, i => Equals(i.Operand, "Verse.ThingWithComps.get_AllComps"));
        }

        [Fact]
        public void SpawnedRollbackCannotFinishThroughTheContainerOnlyPath()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var il = assembly.Instructions(assembly.Method("Eternal.Corpse.EternalCorpseManager", "TryRestoreCorpseOwnership"));
            int container = il.FindIndex(i => Equals(i.Operand, "Verse.ThingOwner.TryAddOrTransfer"));
            int sourceMap = il.FindIndex(i => i.Code == OpCodes.Ldarg_3);
            Assert.True(sourceMap >= 0 && container > sourceMap);
            Assert.Contains(il.Skip(sourceMap).Take(container - sourceMap), i => i.Code.FlowControl == FlowControl.Cond_Branch
                && i.Operand is int target && target > il[container].Offset);
            Assert.Contains(il.Skip(container), i => Equals(i.Operand, "Verse.GenSpawn.Spawn"));
            Assert.Contains(il.Skip(container), i => Equals(i.Operand, "Verse.Thing.get_Spawned"));
            Assert.Contains(il.Skip(container), i => Equals(i.Operand, "Verse.Thing.get_Position"));
        }

        [Theory]
        [InlineData("Game_DeinitAndRemoveMap_Patch")]
        [InlineData("WorldObjectsHolder_Remove_Patch")]
        [InlineData("GroundMapParent_Destroy_Patch")]
        public void EveryEarlyEntryUsesOneRecoveryGateAndExceptionPreservingFinalizer(string patch)
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            string type = "Eternal.Patches." + patch;
            var prefix = assembly.Method(type, "Prefix");
            Assert.Equal("System.Boolean", assembly.Signature(prefix).ReturnType);
            Assert.Contains(assembly.Instructions(prefix), i => Equals(i.Operand, Guard + ".Begin"));
            Assert.Contains("HarmonyLib.HarmonyPrefix", assembly.AttributeText(assembly.Reader.GetMethodDefinition(prefix).GetCustomAttributes()));
            var finalizer = assembly.Method(type, "Finalizer");
            Assert.Equal("System.Exception", assembly.Signature(finalizer).ReturnType);
            Assert.Contains(assembly.Instructions(finalizer), i => Equals(i.Operand, Guard + ".End"));
            Assert.Contains("HarmonyLib.HarmonyFinalizer", assembly.AttributeText(assembly.Reader.GetMethodDefinition(finalizer).GetCustomAttributes()));
        }

        [Fact]
        public void NativeDestroyCoverageIncludesDeclaredOverridesBeforeSideEffects()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var il = assembly.ReachableInstructions("Eternal.Patches.GroundMapParent_Destroy_Patch", "TargetMethods", false);
            Assert.Contains(il, i => Equals(i.Operand, "System.Reflection.Assembly.GetTypes"));
            Assert.Contains(il, i => Equals(i.Operand, "System.Type.IsAssignableFrom"));
            Assert.Contains(il, i => Equals(i.Operand, "System.Type.GetMethod"));
            Assert.Contains(il, i => i.Code == OpCodes.Ldstr && Equals(i.Operand, "Destroy"));
            Assert.Contains(il, i => Equals(i.Operand, "System.Reflection.MethodBase.get_IsAbstract"));
        }

        [Fact]
        public void GroundRecoveryUsesOwnedTransfersAndNonStrippingDelivery()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var il = assembly.ReachableInstructions(Protection, "TryPrepareMapRemoval", false);
            foreach (string call in new[]
            {
                "Eternal.Corpse.EternalCorpseManager.GetAllCorpses",
                "Eternal.Corpse.EternalCorpseManager.TryRelocateCorpse",
                "Eternal.Corpse.EternalCorpseManager.GetCorpseData",
                "Eternal.World.SpaceCrashRescueService.DeliverCorpsesToCrashSite"
            })
            {
                Assert.Contains(il, i => Equals(i.Operand, call));
            }

            Assert.DoesNotContain(il, i => Equals(i.Operand, "Eternal.World.SpaceCrashRescueService.CrashDownEternals"));
            var owned = assembly.Instructions(assembly.Method(Protection, "IsSourceOwnedCorpse"));
            Assert.Contains(owned, i => Equals(i.Operand, "Verse.Thing.get_MapHeld"));
            Assert.DoesNotContain(owned, i => (i.Operand as string)?.EndsWith(".get_Spawned", StringComparison.Ordinal) == true);
        }

        [Fact]
        public void ProtectionCleanupRequiresCommittedNativeRemoval()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var il = assembly.Instructions(assembly.Method(Protection, "EndMapRemoval"));
            int maps = il.FindIndex(i => Equals(i.Operand, "Verse.Find.get_Maps"));
            int removed = il.FindIndex(i => Equals(i.Operand, Protection + ".RemoveMapProtection"));
            Assert.True(maps >= 0 && removed > maps);
            Assert.Contains(il.Skip(maps).Take(removed - maps), i =>
                i.Code.FlowControl == FlowControl.Cond_Branch && i.Operand is int target && target > il[removed].Offset);
            var begin = assembly.Instructions(assembly.Method(Protection, "BeginMapRemoval"));
            Assert.Contains(begin, i => Equals(i.Operand, Protection + ".TryPrepareMapRemoval"));
            Assert.DoesNotContain(begin, i => Equals(i.Operand, Protection + ".RemoveMapProtection"));
        }

        [Fact]
        public void SpacePoliciesRemainExcludedFromTheGroundGate()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var il = assembly.Instructions(assembly.Method(Protection, "IsGroundMap"));
            Assert.Contains(il, i => i.Code == OpCodes.Isinst && Equals(i.Operand, "RimWorld.Planet.SpaceMapParent"));
            Assert.Contains(il, i => Equals(i.Operand, "Eternal.Compatibility.SpaceModDetection.IsOrbitingShip"));
        }

#if DEBUG
        [Fact]
        public void FailureSuiteCallsActualNativeEntryPointsAndScopedFaultBoundaries()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var il = assembly.ReachableInstructions(Suite, "RunAll", false);
            foreach (string call in new[]
            {
                "Verse.Game.DeinitAndRemoveMap", "RimWorld.Planet.WorldObjectsHolder.Remove",
                "RimWorld.Planet.WorldObject.Destroy",
                "Eternal.InGameTests.Performance.TestMapScope.Create",
                "Eternal.InGameTests.Performance.TestMapScope.CreateWithParent",
                "Eternal.InGameTests.Helpers.TestPawnFactory.KillPawn",
                "Eternal.InGameTests.FailureScenarios.MapRemovalObservation.get_HomeFaults",
                "Eternal.InGameTests.FailureScenarios.MapRemovalObservation.get_SiteFaults",
                "Eternal.InGameTests.FailureScenarios.MapRemovalObservation.get_SitePostDestroyCalls"
            })
            {
                Assert.Contains(il, i => Equals(i.Operand, call));
            }
            // The compiler references the virtual slot's declaring WorldObject type even
            // for MapParent/Site receivers. Keep proof of both dispatch branches and the
            // derived Site receiver instead of requiring nonexistent override call tokens.
            var removal = assembly.Instructions(assembly.Method(Suite, "RemoveNative"));
            Assert.Equal(2, removal.Count(i => i.Code == OpCodes.Callvirt
                && Equals(i.Operand, "RimWorld.Planet.WorldObject.Destroy")));
            Assert.Contains(removal, i => i.Code == OpCodes.Castclass
                && Equals(i.Operand, "RimWorld.Planet.Site"));
            var observation = assembly.ReachableInstructions("Eternal.InGameTests.FailureScenarios.MapRemovalObservation", ".ctor", false);
            Assert.Contains(observation, i => Equals(i.Operand, "HarmonyLib.Harmony.Patch"));
            var cleanup = assembly.ReachableInstructions("Eternal.InGameTests.FailureScenarios.MapRemovalObservation", "Dispose", false);
            Assert.Contains(cleanup, i => Equals(i.Operand, "HarmonyLib.Harmony.Unpatch"));
        }
#else
        [Fact]
        public void ReleaseDoesNotContainFailureHarness()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            Assert.False(assembly.TryType(Suite, out _));
            Assert.False(assembly.TryType("Eternal.InGameTests.FailureScenarios.MapRemovalObservation", out _));
        }
#endif
    }
}

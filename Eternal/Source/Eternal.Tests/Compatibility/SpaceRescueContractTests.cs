// Author: 0Shard
// Description: Compiled rescue modes, ownership handoff and teleport save contracts; no game-host claims.
using System;
using System.Linq;
using System.Reflection.Emit;
using Eternal.Exceptions;
using Eternal.Tests.Infrastructure;
using Xunit;

namespace Eternal.Tests.Compatibility
{
    public class SpaceRescueContractTests
    {
        private const string Rescue = "Eternal.World.SpaceCrashRescueService";
        private const string Protection = "Eternal.Corpse.EternalMapProtection";

        [Fact]
        public void AnchorLoadKeepsDeadReferenceAndRestoresNativeAttachmentAfterMapLoading()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var expose = assembly.Instructions(assembly.Method("Eternal.Map.EternalAnchor", "ExposeData"));
            int reference = expose.FindIndex(i => Equals(i.Operand, "Verse.Scribe_References.Look"));
            Assert.True(reference > 0);
            Assert.Equal(OpCodes.Ldc_I4_1, expose[reference - 1].Code);
            var restore = assembly.Instructions(assembly.Method("Eternal.Map.EternalMapManager", "FinalizeInit"));
            int spawned = restore.FindIndex(i => Equals(i.Operand, "Verse.Thing.get_Spawned"));
            int spawn = restore.FindIndex(i => Equals(i.Operand, "Verse.GenSpawn.Spawn"));
            int validate = restore.FindIndex(i => Equals(i.Operand, "Eternal.Map.EternalAnchor.ShouldRemoveAnchor"));
            Assert.True(spawned >= 0 && spawn > spawned && validate > spawn);
            Assert.Contains(restore.Skip(spawned).Take(spawn - spawned), i => i.Code.FlowControl == FlowControl.Cond_Branch);
            Assert.Equal(OpCodes.Ldc_I4_1, restore[spawn - 2].Code);
            Assert.DoesNotContain(restore, i => i.Code == OpCodes.Stfld && Equals(i.Operand, "resurrectionTick"));
            Assert.DoesNotContain(assembly.Instructions(assembly.Method("Eternal.Map.EternalMapManager", "ExposeData")),
                i => Equals(i.Operand, "Verse.GenSpawn.Spawn"));
        }

        [Fact]
        public void RecoveryArrivalScribesModeAndRevalidatesBeforeNativeEntryOrPickup()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            const string arrival = "Eternal.World.CaravanArrivalAction_EternalCrashSite";
            const string site = "Eternal.World.WorldObject_EternalCrashSite";
            var arrived = assembly.Instructions(assembly.Method(arrival, "Arrived"));
            Assert.Contains(arrived, i => Equals(i.Operand, "RimWorld.Planet.CaravanArrivalAction.StillValid"));
            Assert.Contains(assembly.Instructions(assembly.Method(arrival, "StillValid")),
                i => Equals(i.Operand, site + ".CanRecover"));
            Assert.Contains(arrived, i => Equals(i.Operand, site + ".EnterRecoveryMap"));
            Assert.Contains(arrived, i => Equals(i.Operand, site + ".RescueEternalsToCaravan"));
            var scribe = assembly.Instructions(assembly.Method(arrival, "ExposeData"));
            Assert.Contains(scribe, i => Equals(i.Operand, "Verse.Scribe_References.Look"));
            Assert.Contains(scribe, i => Equals(i.Operand, "Verse.Scribe_Values.Look"));
            string iterator = assembly.Reader.TypeDefinitions.Select(handle => assembly.MemberName(handle))
                .Single(name => name.StartsWith(site + "+<GetFloatMenuOptions>d__", StringComparison.Ordinal));
            var menu = assembly.Instructions(assembly.Method(iterator, "MoveNext"));
            Assert.Contains(menu, i => Equals(i.Operand, "RimWorld.Planet.CaravanArrivalActionUtility.GetFloatMenuOptions"));
            var entry = assembly.ReachableInstructions(site, "EnterRecoveryMap", false);
            foreach (string required in new[] { "Verse.LongEventHandler.QueueLongEvent", "Verse.MapGenerator.GenerateMap",
                "RimWorld.Planet.CaravanEnterMapUtility.Enter", site + ".DeliverPendingContents" })
            { Assert.Contains(entry, i => Equals(i.Operand, required)); }
        }

        [Fact]
        public void SpaceCollectionAndCrashMergeUseCurrentPhysicalOwnership()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var collect = assembly.Instructions(assembly.Method(Rescue, "CollectEternalsOnMap"));
            foreach (string required in new[] { "Eternal.Corpse.EternalCorpseManager.GetAllCorpses",
                "Verse.Thing.get_MapHeld", "Verse.Thing.get_PositionHeld", Rescue + ".IsDepartingGravshipCargo" })
            { Assert.Contains(collect, i => Equals(i.Operand, required)); }
            var guard = assembly.Instructions(assembly.Method(Protection, "IsSourceOwnedCorpse"));
            Assert.Contains(guard, i => Equals(i.Operand, Rescue + ".IsDepartingGravshipCargo"));
            var refresh = assembly.Instructions(assembly.Method(Rescue, "RefreshPreCalculatedQueue"));
            Assert.Contains(refresh, i => Equals(i.Operand, "Eternal.Healing.EternalCorpseHealingProcessor.ReconcileCrashRegrowth"));
            var merge = assembly.ReachableInstructions("Eternal.Healing.EternalCorpseHealingProcessor", "ReconcileCrashRegrowth", false);
            Assert.Contains(merge, i => Equals(i.Operand, "Eternal.EternalRegrowthManager.StartRegrowth"));
            Assert.DoesNotContain(merge, i => (i.Operand as string)?.EndsWith(".AddResurrectionDebt") == true);
        }

        [Fact]
        public void TileContextsKeepPlanetLayerAndAnchorsUseIdempotentNativeDisposal()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            foreach (var context in new[] { ("Eternal.Patches.Odyssey.AbandonMapContext", "SavedWorldTile"),
                ("Eternal.Patches.VGE.VGELandingContext", "WorldTile") })
            {
                Assert.Equal("RimWorld.Planet.PlanetTile", assembly.Signature(assembly.Method(context.Item1, "get_" + context.Item2)).ReturnType);
            }
            var dispose = assembly.Instructions(assembly.Method("Eternal.Map.EternalAnchor", "DisposeAnchor"));
            Assert.Contains(dispose, i => Equals(i.Operand, "Verse.Entity.DeSpawn"));
            Assert.DoesNotContain(dispose, i => Equals(i.Operand, "Verse.Thing.Discard"));
            Assert.DoesNotContain(dispose, i => Equals(i.Operand, "Verse.Thing.Destroy"));
            var retry = assembly.Instructions(assembly.Method(Protection, "HandleTemporaryMapWithCorpses"));
            Assert.Contains(retry, i => Equals(i.Operand, Protection + ".ScheduleCorpseTeleport"));
        }

        [Theory]
        [InlineData("CrashDownEternals", true, "EternalSpaceLossTorsoDesc", EternalExceptionCategory.MapProtection)]
        [InlineData("DeliverCorpsesToCrashSite", true, "EternalCorpseRescuedDesc", EternalExceptionCategory.CorpseTracking)]
        public void RescueEntriesKeepTheirDistinctModesAndCountOnlyAcceptedTransfers(
            string method, bool detachContainer, string letter, EternalExceptionCategory category)
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            string[] parameters = method == "DeliverCorpsesToCrashSite" ? new[]
            {
                "System.Collections.Generic.IEnumerable`1<Verse.Corpse>", "RimWorld.Planet.PlanetTile",
                "System.Func`2<Verse.Map,System.Boolean>",
                "System.Func`2<Eternal.World.WorldObject_EternalCrashSite,System.Boolean>"
            } : null;
            var il = assembly.Instructions(assembly.Method(Rescue, method, parameters));
            int handoff = il.FindIndex(i => Equals(i.Operand, Rescue + ".TryDeliverCorpseToCrashSite"));
            Assert.True(handoff >= 1);
            Assert.Equal(detachContainer ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0, il[handoff - 1].Code);
            // A false handoff result branches beyond the increment in both optimized and Debug IL.
            int branch = il.FindIndex(handoff + 1, i => i.Code.FlowControl == FlowControl.Cond_Branch);
            int increment = il.FindIndex(handoff + 1, i => i.Code == OpCodes.Add);
            Assert.True(branch > handoff && increment > branch);
            Assert.True((int)il[branch].Operand > il[increment].Offset);
            Assert.Contains(il, i => i.Code == OpCodes.Ldstr && Equals(i.Operand, letter));
            int context = il.FindIndex(i => i.Code == OpCodes.Ldstr && Equals(i.Operand, method));
            Assert.Equal((int)category, IntConstant(il[context - 1]));

            foreach (string operation in new[] { "KillInPlace", "StripToTorso", "RefreshPreCalculatedQueue" })
            {
                bool present = il.Any(i => Equals(i.Operand, Rescue + "." + operation));
                Assert.Equal(method == "CrashDownEternals", present);
            }
            if (method == "CrashDownEternals")
            {
                int strip = il.FindIndex(i => Equals(i.Operand, Rescue + ".StripToTorso"));
                int queue = il.FindIndex(i => Equals(i.Operand, Rescue + ".RefreshPreCalculatedQueue"));
                Assert.True(strip >= 0 && queue > strip && handoff > queue);
            }
        }

        [Fact]
        public void SharedHandoffUsesTrackedOwnershipBoundaryAndGuardsContainerRemovalByMode()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var il = assembly.ReachableInstructions(Rescue, "TryDeliverCorpseToCrashSite", false);
            foreach (string call in new[]
            {
                "Eternal.Corpse.EternalCorpseManager.IsTracked",
                "Eternal.Corpse.EternalCorpseManager.TryReleaseCorpseToUnspawnedOwner",
                "Verse.Entity.DeSpawn", "Verse.ThingOwner.Remove",
                "Eternal.World.WorldObject_EternalCrashSite.AddCorpse"
            })
            {
                Assert.Single(il.Where(i => Equals(i.Operand, call)));
            }
            Assert.Contains(il, i => Equals(i.Operand, "Eternal.World.WorldObject_EternalCrashSite.HasPendingCorpse"));
            Assert.Contains(il, i => Equals(i.Operand, "Eternal.World.WorldObject_EternalCrashSite.ForgetPendingCorpse"));
            Assert.Contains(il, i => Equals(i.Operand, "RimWorld.Planet.WorldObject.get_Spawned"));
            Assert.Contains(il, i => Equals(i.Operand, "RimWorld.Planet.WorldObject.get_Destroyed"));

            int mode = il.FindIndex(i => i.Code == OpCodes.Ldfld && Equals(i.Operand, "detachContainerOwnership"));
            int remove = il.FindIndex(i => Equals(i.Operand, "Verse.ThingOwner.Remove"));
            int accept = il.FindIndex(i => Equals(i.Operand, "Eternal.World.WorldObject_EternalCrashSite.AddCorpse"));
            Assert.True(mode >= 0 && remove > mode && accept > remove);
            int guard = il.FindIndex(mode + 1, i => i.Code.FlowControl == FlowControl.Cond_Branch);
            Assert.True(guard < remove);
            Assert.True((int)il[guard].Operand > il[remove].Offset);
            Assert.True((int)il[guard].Operand <= il[accept].Offset);
        }

        [Fact]
        public void PendingTeleportKeepsExactScribeLabelsAndComponentLoadWiring()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            const string pending = Protection + "+PendingTeleport";
            var exposed = assembly.Instructions(assembly.Method(pending, "ExposeData"));
            Assert.Equal(new[] { "corpse", "targetMap", "targetLocation", "triggerTick" },
                exposed.Where(i => i.Code == OpCodes.Ldstr).Select(i => (string)i.Operand));
            Assert.Equal(2, exposed.Count(i => Equals(i.Operand, "Verse.Scribe_References.Look")));
            Assert.Equal(2, exposed.Count(i => Equals(i.Operand, "Verse.Scribe_Values.Look")));
            Assert.NotEmpty(assembly.Methods(pending, ".ctor"));

            var queue = assembly.Instructions(assembly.Method(Protection, "ExposeData"));
            Assert.Contains(queue, i => i.Code == OpCodes.Ldstr && Equals(i.Operand, "pendingTeleports"));
            Assert.Contains(queue, i => Equals(i.Operand, "Verse.Scribe_Collections.Look"));
            var component = assembly.Instructions(assembly.Method("Eternal.Eternal_Component", "ExposeData"));
            foreach (string method in new[] { "ExposeData", "PostLoadInit" })
            {
                Assert.Contains(component, i => Equals(i.Operand, Protection + "." + method));
            }
            var apply = assembly.Instructions(assembly.Method(Protection, "ApplyMapProtection"));
            Assert.Contains(apply, i => Equals(i.Operand, Protection + ".ScheduleCorpseTeleport"));
            Assert.Contains(apply, i => Equals(i.Operand, Protection + "+MapProtectionData.set_Strategy"));
        }

        private static int IntConstant(CompiledInstruction instruction)
        {
            if (instruction.Code == OpCodes.Ldc_I4 || instruction.Code == OpCodes.Ldc_I4_S)
            {
                return (int)instruction.Operand;
            }

            if (instruction.Code.Name.StartsWith("ldc.i4.", StringComparison.Ordinal))
            {
                return int.Parse(instruction.Code.Name.Substring("ldc.i4.".Length));
            }

            throw new InvalidOperationException("Expected an integer constant, got " + instruction.Code);
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using Eternal.Corpse;
using Eternal.Patches.VEF;
using Eternal.Tests.Infrastructure;
using Eternal.Tests.UI;
using Xunit;
using Xunit.Abstractions;

namespace Eternal.Tests.Compatibility
{
    public class CorpseFeedingCompatibilityTests
    {
        private readonly ITestOutputHelper output;
        public CorpseFeedingCompatibilityTests(ITestOutputHelper output) { this.output = output; }

        [Fact]
        public void InstalledVefCandidateGuard_ExcludesEveryFeedingEffectAndKeepsOrdinaryIteration()
        {
            string path = CompiledBuildReferences.OptionalPath("VEF17");
            Assert.True(File.Exists(path), "Installed VEF 1.7 input is required for this compatibility check.");
            var manifest = CompiledBuildReferences.CapturedManifest;
            string optionalJson = File.ReadAllText(Path.Combine(Path.GetDirectoryName((string)manifest.Element("capture").Attribute("path")), "optional-assemblies.json"));
            int start = optionalJson.IndexOf("\"VEF17\"", StringComparison.Ordinal);
            Assert.True(start >= 0);
            string captured = optionalJson.Substring(start, optionalJson.IndexOf('}', start) - start);
            using (var sha = SHA256.Create())
            using (var input = File.OpenRead(path))
            {
                string hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
                Assert.Contains(hash, captured);
                Assert.Contains(path, captured);
                output.WriteLine("Installed VEF SHA256: " + hash);
            }
            using var installed = new CompiledAssemblyContract(path);
            var identity = installed.Reader.GetAssemblyDefinition();
            output.WriteLine(installed.Reader.GetString(identity.Name) + ", Version=" + identity.Version);
            var method = installed.Method("VEF.AnimalBehaviours.CompCorpseDecayer", "CompTickInterval", new[] { "System.Int32" }, "System.Void", isStatic: false);
            var il = installed.Instructions(method);
            var positions = il.Select((i, n) => (i.Offset, n)).ToDictionary(v => v.Offset, v => v.n);
            string Member(CompiledInstruction instruction)
            {
                if (instruction.MemberHandle.Kind == HandleKind.FieldDefinition)
                {
                    var field = installed.Reader.GetFieldDefinition((FieldDefinitionHandle)instruction.MemberHandle);
                    return installed.MemberName(field.GetDeclaringType()) + "." + installed.Reader.GetString(field.Name);
                }
                return instruction.Operand as string;
            }
            var shape = il.Select(i => new CorpseFeedingInstruction(i.Code, Member(i), CompiledAssemblyContract.Local(i),
                i.Code.FlowControl == FlowControl.Branch || i.Code.FlowControl == FlowControl.Cond_Branch
                    ? positions[(int)i.Operand] : -1)).ToArray();
            var plan = CorpseFeedingGuard.Plan(shape);
            Assert.Equal(0, plan.EaterLocal);
            Assert.Equal(5, plan.CorpseLocal);
            Assert.Equal(0xf0, il[plan.Insertion].Offset);
            Assert.Equal(0x26d, il[plan.Continue].Offset);
            Assert.True(plan.Insertion > 0 && plan.Continue > plan.Insertion);
            var effects = shape.Skip(plan.Insertion).Take(plan.Continue - plan.Insertion).Where(i =>
                i.Member?.EndsWith("set_HitPoints", StringComparison.Ordinal) == true ||
                i.Member?.EndsWith("set_CurLevel", StringComparison.Ordinal) == true ||
                i.Member?.EndsWith("AddHediff", StringComparison.Ordinal) == true ||
                i.Member?.EndsWith("set_RotProgress", StringComparison.Ordinal) == true ||
                i.Member?.EndsWith("Destroy", StringComparison.Ordinal) == true).ToArray();
            Assert.Equal(5, effects.Length);
            // Apply the actual captured method's guard plan. The true edge jumps over
            // every original feeding instruction to the same MoveNext as the flesh gate.
            var transformed = shape.ToList();
            transformed.InsertRange(plan.Insertion, new[] {
                new CorpseFeedingInstruction(OpCodes.Ldloc, null, plan.EaterLocal),
                new CorpseFeedingInstruction(OpCodes.Ldloc, null, plan.CorpseLocal),
                new CorpseFeedingInstruction(OpCodes.Call, "Eternal.Corpse.AnimalCorpseProtection.ShouldBlock"),
                new CorpseFeedingInstruction(OpCodes.Brtrue, null, branchTarget: plan.Continue + 4) });
            Assert.Equal(shape[plan.Continue].Member, transformed[transformed[plan.Insertion + 3].BranchTarget].Member);
            Assert.Equal(shape[plan.Insertion].Member, transformed[plan.Insertion + 4].Member);
            Assert.Equal(shape.Length + 4, transformed.Count);
            output.WriteLine(installed.Describe(method) + $"; candidate local {plan.CorpseLocal}, eater local {plan.EaterLocal}, guard IL_{il[plan.Insertion].Offset:x4} -> IL_{il[plan.Continue].Offset:x4}; all five feeding effects bypassed only for blocked candidates.");
            var broken = shape.ToArray();
            broken[plan.Continue + 1] = new CorpseFeedingInstruction(OpCodes.Nop, null);
            Assert.Throws<InvalidOperationException>(() => CorpseFeedingGuard.Plan(broken));
        }

        [Fact]
        public void MixedCorpseCandidates_KeepOrdinaryFeedingAndHumanRules()
        {
            bool[] eternalCandidates = { true, false, true, false };
            Assert.Equal(new[] { false, false }, eternalCandidates.Where(eternal =>
                !AnimalCorpseProtectionPolicy.ShouldBlock(true, true, true, eternal)));
            Assert.Equal(4, eternalCandidates.Count(eternal => !AnimalCorpseProtectionPolicy.ShouldBlock(false, true, true, eternal)));
        }

        [Fact]
        public void VanillaHooks_UseCurrentTargetsSharedToilsAndFinalConsumptionPredicate()
        {
            foreach (string patch in new[] { "AnimalCorpseWillEatPatch", "AnimalCorpseIngestedPatch" })
            {
                string method = patch == "AnimalCorpseWillEatPatch" ? "Postfix" : "Prefix";
                Assert.Contains("Eternal.Corpse.AnimalCorpseProtection.ShouldBlock", MenuAssemblyContract.Calls("Eternal.Patches." + patch, method));
            }
            foreach (string patch in new[] { "AnimalCorpseChewPatch", "AnimalCorpseFinalizePatch" })
            {
                Assert.Contains("Eternal.Patches.AnimalCorpseToilProtection.Attach", MenuAssemblyContract.Calls("Eternal.Patches." + patch, "Postfix"));
            }
            var attach = MenuAssemblyContract.ReachableInstructions("Eternal.Patches.AnimalCorpseToilProtection", "Attach");
            Assert.Contains(attach, i => Equals(i.Operand, "Verse.AI.Toil.AddFailCondition"));
            Assert.Contains(attach, i => Equals(i.Operand, "Eternal.Corpse.AnimalCorpseProtection.ShouldBlock"));
            Assert.Contains(attach, i => Equals(i.Operand, "Verse.AI.Job.GetTarget"));
            var predicate = MenuAssemblyContract.Calls("Eternal.Corpse.AnimalCorpseProtection", "ShouldBlock");
            Assert.DoesNotContain(predicate, c => c?.Contains("Destroyed") == true || c?.Contains("Register") == true);
            Assert.Contains("Eternal.Extensions.PawnExtensions.HasTraitIgnoringSuppression", predicate);
        }

        [Fact]
        public void OptionalTranspiler_UsesValidatedPlanAndPreservesLabelsAndExceptionBlocks()
        {
            var il = MenuAssemblyContract.ReachableInstructions("Eternal.Patches.VEF.VEF_CorpseDecayer_Patch", "Transpiler");
            Assert.Contains(il, i => Equals(i.Operand, "Eternal.Patches.VEF.CorpseFeedingGuard.Plan"));
            Assert.Contains(il, i => Equals(i.Operand, "ShouldBlock"));
            Assert.Contains(il, i => (i.Operand as string)?.EndsWith(".InsertRange", StringComparison.Ordinal) == true);
            Assert.Contains(il, i => Equals(i.Operand, "HarmonyLib.CodeInstruction.labels"));
            Assert.Contains(il, i => Equals(i.Operand, "HarmonyLib.CodeInstruction.blocks"));
            Assert.Contains(MenuAssemblyContract.Instructions("Eternal.Patches.VEF.VEF_CorpseDecayer_Patch", "Prepare"), i => Equals(i.Operand, "Verse.Log.Error"));
        }
    }
}

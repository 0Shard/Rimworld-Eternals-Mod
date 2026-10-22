using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Verse;
using Eternal.Corpse;

namespace Eternal.Patches.VEF
{
    public readonly struct CorpseFeedingInstruction
    {
        public OpCode Code { get; }
        public string Member { get; }
        public int Local { get; }
        public int BranchTarget { get; }
        public CorpseFeedingInstruction(OpCode code, string member, int local = -1, int branchTarget = -1)
        {
            Code = code;
            Member = member;
            Local = local;
            BranchTarget = branchTarget;
        }
    }

    public readonly struct CorpseFeedingGuardPlan
    {
        public int Insertion { get; }
        public int Continue { get; }
        public int EaterLocal { get; }
        public int CorpseLocal { get; }
        public CorpseFeedingGuardPlan(int insertion, int next, int eater, int corpse)
        {
            Insertion = insertion;
            Continue = next;
            EaterLocal = eater;
            CorpseLocal = corpse;
        }
    }

    public static class CorpseFeedingGuard
    {
        public static CorpseFeedingGuardPlan Plan(IReadOnlyList<CorpseFeedingInstruction> code)
        {
            int Unique(string member)
            {
                var found = Enumerable.Range(0, code.Count).Where(i => code[i].Member == member).ToArray();
                return found.Length == 1 ? found[0] : throw new InvalidOperationException("Expected one " + member);
            }
            int flesh = Unique("Verse.RaceProperties.get_IsFlesh");
            int cast = Unique("Verse.Corpse");
            int eater = Unique("Verse.Pawn");
            if (flesh + 2 >= code.Count || cast + 1 >= code.Count || eater + 1 >= code.Count)
            {
                throw new InvalidOperationException("Incomplete VEF candidate loop");
            }
            int insertion = flesh + 2;
            int next = code[flesh + 1].BranchTarget;
            if (code[cast].Code != OpCodes.Isinst || !code[cast + 1].Code.Name.StartsWith("stloc", StringComparison.Ordinal) ||
                code[eater].Code != OpCodes.Isinst || !code[eater + 1].Code.Name.StartsWith("stloc", StringComparison.Ordinal) ||
                cast >= flesh || next <= insertion || next + 2 >= code.Count ||
                (code[flesh + 1].Code != OpCodes.Brfalse && code[flesh + 1].Code != OpCodes.Brfalse_S) ||
                code[insertion].Local != code[cast + 1].Local ||
                !code[insertion].Code.Name.StartsWith("ldloc", StringComparison.Ordinal) ||
                !code[next].Code.Name.StartsWith("ldloca", StringComparison.Ordinal) ||
                code[next + 1].Member?.EndsWith(".MoveNext", StringComparison.Ordinal) != true ||
                code[next + 2].BranchTarget >= cast || code[next + 2].BranchTarget < 0)
            {
                throw new InvalidOperationException("Unsupported VEF corpse candidate loop");
            }
            foreach (string effect in new[] { "Verse.Thing.set_HitPoints", "RimWorld.Need.set_CurLevel", "Verse.Pawn_HealthTracker.AddHediff",
                "RimWorld.CompRottable.set_RotProgress", "Verse.Thing.Destroy" })
            {
                int effectIndex = Unique(effect);
                if (effectIndex < insertion || effectIndex >= next)
                {
                    throw new InvalidOperationException("Feeding side effect outside guarded candidate: " + effect);
                }
            }
            if (code.Skip(insertion).Take(next - insertion).Count(i => i.Code == OpCodes.Stfld &&
                i.Member == "VEF.AnimalBehaviours.CompCorpseDecayer.flagOnce") != 1)
            {
                throw new InvalidOperationException("Candidate success flag unsupported");
            }
            return new CorpseFeedingGuardPlan(insertion, next, code[eater + 1].Local, code[cast + 1].Local);
        }
    }

    [HarmonyPatch]
    public static class VEF_CorpseDecayer_Patch
    {
        private const string TargetType = "VEF.AnimalBehaviours.CompCorpseDecayer";
        public static bool Prepare()
        {
            var type = AccessTools.TypeByName(TargetType);
            if (type == null)
            {
                return false;
            }
            if (AccessTools.DeclaredMethod(type, "CompTickInterval", new[] { typeof(int) }) != null)
            {
                return true;
            }
            Log.Error("[Eternal] VEF corpse protection unavailable: CompTickInterval(int) target unsupported.");
            return false;
        }

        public static MethodBase TargetMethod()
            => AccessTools.DeclaredMethod(AccessTools.TypeByName(TargetType), "CompTickInterval", new[] { typeof(int) });

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            CorpseFeedingGuardPlan plan;
            try
            {
                var shape = code.Select(i => new CorpseFeedingInstruction(i.opcode,
                    i.operand is MemberInfo member ? member is Type type ? type.FullName : member.DeclaringType?.FullName + "." + member.Name : null,
                    Local(i), i.operand is Label target ? code.FindIndex(c => c.labels.Contains(target)) : -1)).ToArray();
                plan = CorpseFeedingGuard.Plan(shape);
                if (code[plan.Insertion].blocks.Count != 0)
                {
                    throw new InvalidOperationException("Candidate begins at an unsupported exception boundary");
                }
            }
            catch (InvalidOperationException error)
            {
                Log.Error("[Eternal] VEF corpse protection unavailable: " + error.Message);
                return code;
            }
            var guard = new[]
            {
                new CodeInstruction(OpCodes.Ldloc, (short)plan.EaterLocal),
                new CodeInstruction(OpCodes.Ldloc, (short)plan.CorpseLocal),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AnimalCorpseProtection), nameof(AnimalCorpseProtection.ShouldBlock))),
                new CodeInstruction(OpCodes.Brtrue, code[plan.Insertion - 1].operand)
            };
            guard[0].labels.AddRange(code[plan.Insertion].labels);
            code[plan.Insertion].labels.Clear();
            guard[0].blocks.AddRange(code[plan.Insertion].blocks);
            code[plan.Insertion].blocks.Clear();
            code.InsertRange(plan.Insertion, guard);
            return code;
        }

        private static int Local(CodeInstruction instruction)
        {
            if (instruction.opcode == OpCodes.Ldloc_0 || instruction.opcode == OpCodes.Stloc_0) { return 0; }
            if (instruction.opcode == OpCodes.Ldloc_1 || instruction.opcode == OpCodes.Stloc_1) { return 1; }
            if (instruction.opcode == OpCodes.Ldloc_2 || instruction.opcode == OpCodes.Stloc_2) { return 2; }
            if (instruction.opcode == OpCodes.Ldloc_3 || instruction.opcode == OpCodes.Stloc_3) { return 3; }
            return instruction.opcode.Name.Contains("loc") ? instruction.operand is LocalBuilder local ? local.LocalIndex : Convert.ToInt32(instruction.operand) : -1;
        }
    }
}

// Author: 0Shard
// Description: Compiled settings selection and XML collection wiring; actual Scribe saves require the game.
using System;
using System.Linq;
using System.Reflection.Emit;
using Eternal.Tests.Infrastructure;
using Xunit;

namespace Eternal.Tests.Compatibility
{
    public class RjwSettingsPersistenceContractTests
    {
        [Fact]
        public void SaveToXml_SelectsUsingContextAwareSettingBeforeCreatingSlimRecords()
        {
            using var eternal = CompiledBuildReferences.OpenProduction();
            const string store = "Eternal.Settings.HediffSettingsStore";
            foreach (string entry in new[] { "SaveToXml", "ExposeData" })
            {
                Assert.Contains(eternal.Instructions(eternal.Method(store, entry)), instruction =>
                    Equals(instruction.Operand, store + ".GetCustomizedSettings"));
            }
            var selector = eternal.ReachableInstructions(store, "GetCustomizedSettings", false);
            Assert.Contains(selector, instruction => Equals(instruction.Operand, "Eternal.EternalHediffSetting.HasCustomSettings"));
            var save = eternal.Instructions(eternal.Method(store, "SaveToXml"));
            int selected = save.FindIndex(i => Equals(i.Operand, store + ".GetCustomizedSettings"));
            int converted = save.FindIndex(i => (i.Operand as string)?.StartsWith("System.Linq.Enumerable.ToDictionary", StringComparison.Ordinal) == true);
            int written = save.FindIndex(i => Equals(i.Operand, "Eternal.Settings.HediffSettingsXmlStore.Save"));
            Assert.True(selected >= 0 && converted > selected && written > converted,
                "Context-aware selection must precede slim conversion and XML writing");
            var reachable = eternal.ReachableInstructions(store, "SaveToXml", false);
            int slim = reachable.FindIndex(i => i.Code == OpCodes.Newobj && Equals(i.Operand, "Eternal.Settings.HediffSettingSlim..ctor"));
            Assert.True(slim >= 2);
            Assert.Equal("System.Collections.Generic.KeyValuePair`2<System.String,Eternal.EternalHediffSetting>.get_Value", reachable[slim - 1].Operand);
            Assert.Contains(reachable.Take(slim), i => Equals(i.Operand,
                "System.Collections.Generic.KeyValuePair`2<System.String,Eternal.EternalHediffSetting>.get_Key"));
            var customized = eternal.Instructions(eternal.Method("Eternal.EternalHediffSetting", "HasCustomSettings"));
            Assert.Contains(customized, instruction => Equals(instruction.Operand, "canHeal"));
            Assert.Contains(customized, instruction => Equals(instruction.Operand, "defaultCanHeal"));
        }

        [Fact]
        public void LegacyModSettings_StillScribesActiveChoicesAndCategoryFlagsFromTheirOriginalFields()
        {
            using var eternal = CompiledBuildReferences.OpenProduction();
            var il = eternal.Instructions(eternal.Method("Eternal.EternalHediffSetting", "ExposeData"));
            foreach (string key in new[] { "defName", "defaultCanHeal", "canHeal", "healingRate", "noThreshold",
                "allowedCategories", "isInjuryFilter", "isDiseaseFilter", "isConditionFilter" })
            {
                int label = il.FindIndex(i => i.Code == OpCodes.Ldstr && Equals(i.Operand, key));
                Assert.True(label > 1, "Missing legacy Scribe key: " + key);
                Assert.Equal(OpCodes.Ldflda, il[label - 1].Code);
                Assert.Equal(key, il[label - 1].Operand);
                Assert.Equal(OpCodes.Ldarg_0, il[label - 2].Code);
                int call = il.FindIndex(label + 1, i => i.Code.FlowControl == FlowControl.Call);
                Assert.True(call > label, "Missing Scribe call for: " + key);
                Assert.Equal(OpCodes.Call, il[call].Code);
                Assert.Equal("Verse.Scribe_Values.Look", il[call].Operand);
                Assert.True(il[call].CallSignature.HasValue);
                var signature = il[call].CallSignature.Value;
                Assert.False(signature.Header.IsInstance);
                Assert.Equal("System.Void", signature.ReturnType);
                Assert.Equal(4, signature.ParameterTypes.Length);
                Assert.EndsWith("&", signature.ParameterTypes[0]);
                Assert.Equal("System.String", signature.ParameterTypes[1]);
                Assert.Equal(signature.ParameterTypes[0].TrimEnd('&'), signature.ParameterTypes[2]);
                Assert.Equal("System.Boolean", signature.ParameterTypes[3]);
                // Default strings are also ldstr; prove they only push remaining arguments.
                var defaults = il.Skip(label + 1).Take(call - label - 1).Where(i => i.Code != OpCodes.Nop).ToArray();
                Assert.Equal(signature.ParameterTypes.Length - 2, defaults.Length);
                Assert.All(defaults, i => Assert.True(i.Code == OpCodes.Ldstr || i.Code == OpCodes.Ldc_R4 ||
                    i.Code == OpCodes.Ldc_I4 || i.Code == OpCodes.Ldc_I4_S ||
                    (i.Code.Value >= OpCodes.Ldc_I4_M1.Value && i.Code.Value <= OpCodes.Ldc_I4_8.Value),
                    "Unexpected consumption of Scribe field or label for: " + key));
            }
        }

        [Fact]
        public void XmlSave_TransfersInputValuesDirectlyAndDoesNotReapplyGlobalDefaults()
        {
            using var eternal = CompiledBuildReferences.OpenProduction();
            const string type = "Eternal.Settings.HediffSettingsXmlStore";
            var save = eternal.Instructions(eternal.Method(type, "Save"));
            Assert.Empty(eternal.Methods(type, "IsCustomized"));
            Assert.DoesNotContain(save, instruction => instruction.Operand is string member &&
                (member == "canHeal" || member == "healingRate" || member == "noThreshold" ||
                 member.EndsWith(".HasCustomSettings", StringComparison.Ordinal) || member.EndsWith(".IsCustomized", StringComparison.Ordinal)));
            int added = save.FindIndex(instruction => Equals(instruction.Operand,
                "System.Collections.Generic.List`1<Eternal.Settings.HediffSettingSlim>.Add"));
            Assert.True(added > 0, "XML writer must retain caller-selected records in the saved collection");
            var previous = save.Take(added).Last(instruction => instruction.Code != OpCodes.Nop);
            Assert.Equal("System.Collections.Generic.KeyValuePair`2<System.String,Eternal.Settings.HediffSettingSlim>.get_Value", previous.Operand);
            Assert.Contains(save, instruction => Equals(instruction.Operand, "Verse.SafeSaver.Save"));

            var writerType = eternal.Reader.TypeDefinitions.Where(handle => eternal.MemberName(handle).StartsWith(type + "+", StringComparison.Ordinal));
            var callbacks = writerType.SelectMany(handle => eternal.Reader.GetTypeDefinition(handle).GetMethods())
                .Where(method => eternal.Reader.GetString(eternal.Reader.GetMethodDefinition(method).Name).StartsWith("<Save>", StringComparison.Ordinal))
                .Select(eternal.Instructions).ToArray();
            Assert.Single(callbacks);
            Assert.Contains(callbacks[0], instruction => Equals(instruction.Operand, "Verse.Scribe_Collections.Look"));
            Assert.Contains(callbacks[0], instruction => Equals(instruction.Operand, "settings"));
            Assert.Contains(callbacks[0], instruction => instruction.Code == OpCodes.Ldflda && Equals(instruction.Operand, "customizedSettings"));
        }
    }
}

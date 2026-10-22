// Author: 0Shard
// Description: Exact Harmony attribute targets and injected parameter compatibility; no CLR game loading.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using Eternal.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Eternal.Tests.Compatibility
{
    public class HarmonyPatchSignatureTests
    {
        private readonly ITestOutputHelper output;
        public HarmonyPatchSignatureTests(ITestOutputHelper output) { this.output = output; }

        [Fact]
        public void AllAttributeTargetedPatchClasses_ParameterNames_MatchTargetSignature()
        {
            using var patches = CompiledBuildReferences.OpenProduction();
            using var targets = new CompiledAssemblyContract(CompiledBuildReferences.GameReferencePath);
            var dynamic = new List<string>();
            int checkedMethods = Validate(patches, targets, dynamic);
            Assert.True(checkedMethods >= 15, "Exact Harmony target discovery unexpectedly small");
            Assert.Equal(new[]
            {
                "CTR_BodyCultivationTick_Patch", "CTR_OnReadingTick_Patch", "GravshipUtility_AbandonMap_Patch",
                "GravshipUtility_PreLaunchConfirmation_Patch", "GroundMapParent_Destroy_Patch", "ISF_AddExperience_Patch", "ISF_ApplyOptions_Patch", "ISF_SchemaUse_Patch",
                "RI_GetFloatUpgrade_Patch", "RI_SetExp_Patch", "Rjw_LegacyBiology_Patch", "Rjw_SurgeryMutation_Patch",
                "SOS2_FloatAndDestroy_Patch", "SOS2_KillAllOffShip_Patch",
                "SOS2_MoveShip_Patch", "SOS2_OrbitingShipPatch", "SOS2_RemoveShipOrArea_Patch", "SOS2_ShipDestroyPatch",
                "SOS2_ShuttleShootdown_Patch", "VEF_CorpseDecayer_Patch", "VGE_LandingEnded_Patch"
            }, dynamic.Select(n => n.Substring(n.LastIndexOf('.') + 1)).OrderBy(n => n, StringComparer.Ordinal));
            foreach (string name in dynamic)
            {
                output.WriteLine("Reflection target accounted for, runtime resolution pending: " + name);
            }

            Assert.Contains(dynamic, n => n.EndsWith(".RI_SetExp_Patch", StringComparison.Ordinal));
            Assert.Contains(dynamic, n => n.EndsWith(".VGE_LandingEnded_Patch", StringComparison.Ordinal));
        }

        internal sealed class Target
        {
            public string Type;
            public string Name;
            public int Kind;
            public string[] Parameters;
            public Target Copy() => new Target { Type = Type, Name = Name, Kind = Kind, Parameters = Parameters };
            public string MethodName => Kind == 0 ? Name : Kind == 1 ? "get_" + Name : Kind == 2 ? "set_" + Name :
                Kind == 3 ? ".ctor" : Kind == 4 ? ".cctor" : throw new NotSupportedException("Unsupported Harmony MethodType " + Kind);
        }

        private static string AttributeName(CompiledAssemblyContract assembly, CustomAttributeHandle handle)
        {
            string constructor = assembly.MemberName(assembly.Reader.GetCustomAttribute(handle).Constructor);
            if (!constructor.EndsWith("..ctor", StringComparison.Ordinal))
            {
                throw new BadImageFormatException("Invalid attribute constructor " + constructor);
            }

            return constructor.Substring(0, constructor.Length - "..ctor".Length);
        }

        internal static bool ReadTarget(CompiledAssemblyContract assembly, CustomAttributeHandleCollection attributes, Target target, string context)
        {
            bool present = false;
            foreach (var handle in attributes)
            {
                if (AttributeName(assembly, handle) != "HarmonyLib.HarmonyPatch")
                {
                    continue;
                }

                present = true;
                var attribute = assembly.DecodeAttribute(handle, context);
                foreach (var argument in attribute.FixedArguments)
                {
                    if (argument.Type == "System.Type")
                    {
                        target.Type = (string)argument.Value;
                    }
                    else if (argument.Type == "System.String")
                    {
                        target.Name = (string)argument.Value;
                    }
                    else if (argument.Type == "HarmonyLib.MethodType")
                    {
                        target.Kind = (int)argument.Value;
                    }
                    else if (argument.Type == "System.Type[]")
                    {
                        target.Parameters = ((ImmutableArray<CustomAttributeTypedArgument<string>>)argument.Value).Select(a => (string)a.Value).ToArray();
                    }
                    else if (argument.Type == "HarmonyLib.ArgumentType[]")
                    {
                        var variations = ((ImmutableArray<CustomAttributeTypedArgument<string>>)argument.Value).Select(a => (int)a.Value).ToArray();
                        if (target.Parameters == null || variations.Length != target.Parameters.Length)
                        {
                            throw new InvalidOperationException(context + ": ArgumentType array without matching types");
                        }

                        target.Parameters = target.Parameters.Select((t, i) => variations[i] == 0 ? t : variations[i] == 1 || variations[i] == 2 ? t + "&" :
                            variations[i] == 3 ? t + "*" : throw new NotSupportedException(context + ": unsupported ArgumentType")).ToArray();
                    }
                    else
                    {
                        throw new NotSupportedException(context + ": unsupported HarmonyPatch argument " + argument.Type);
                    }
                }
                if (attribute.NamedArguments.Length != 0)
                {
                    throw new NotSupportedException(context + ": unsupported HarmonyPatch named arguments");
                }
            }
            return present;
        }

        internal static int Validate(CompiledAssemblyContract patches, CompiledAssemblyContract targets, List<string> dynamic,
            Func<string, bool> include = null)
        {
            int count = 0;
            foreach (var typeHandle in patches.Reader.TypeDefinitions)
            {
                var type = patches.Reader.GetTypeDefinition(typeHandle);
                string name = patches.MemberName(typeHandle);
                if (include != null && !include(name))
                {
                    continue;
                }

                var classTarget = new Target();
                bool classPatch = ReadTarget(patches, type.GetCustomAttributes(), classTarget, name);
                var stateTypes = new List<string>();
                bool dynamicRecorded = false;
                foreach (var methodHandle in type.GetMethods())
                {
                    var method = patches.Reader.GetMethodDefinition(methodHandle);
                    string methodName = patches.Reader.GetString(method.Name);
                    var attributes = method.GetCustomAttributes();
                    bool patchMethod = methodName == "Prefix" || methodName == "Postfix" || methodName == "Finalizer" ||
                        (methodName == "Transpiler" && name == "Eternal.Patches.VEF.VEF_CorpseDecayer_Patch") ||
                        attributes.Any(a => AttributeName(patches, a) == "HarmonyLib.HarmonyPrefix" ||
                            AttributeName(patches, a) == "HarmonyLib.HarmonyPostfix" || AttributeName(patches, a) == "HarmonyLib.HarmonyFinalizer");
                    var target = classTarget.Copy();
                    bool methodPatch = ReadTarget(patches, attributes, target, name + "." + methodName);
                    if (!patchMethod || !(classPatch || methodPatch))
                    {
                        continue;
                    }

                    if (target.Type == null || (target.Name == null && target.Kind != 3 && target.Kind != 4))
                    {
                        if (target.Type != null || target.Name != null)
                        {
                            throw new InvalidOperationException(name + ": incomplete Harmony target");
                        }

                        if (!patches.Methods(name, "TargetMethod").Any() && !patches.Methods(name, "TargetMethods").Any())
                        {
                            throw new InvalidOperationException(name + ": dynamic Harmony target without resolver");
                        }

                        if (!dynamicRecorded) { dynamic.Add(name); dynamicRecorded = true; }
                        continue;
                    }
                    var original = targets.Method(target.Type, target.MethodName, target.Parameters, inherited: true);
                    ValidateParameters(patches, methodHandle, targets, original, target.Type, stateTypes);
                    count++;
                }
                Assert.True(stateTypes.Distinct().Count() <= 1, name + ": incompatible patch __state types");
            }
            return count;
        }

        internal static void ValidateParameters(CompiledAssemblyContract patches, MethodDefinitionHandle patch,
            CompiledAssemblyContract targets, MethodDefinitionHandle original, string targetType, List<string> stateTypes)
        {
            var signature = patches.Signature(patch);
            var originalSignature = targets.Signature(original);
            var names = patches.ParameterNames(patch);
            var originals = targets.ParameterNames(original);
            string context = patches.Describe(patch) + " targeting " + targets.Describe(original);
            Assert.False(signature.Header.IsInstance, "Harmony patch must be static: " + context);
            string patchName = patches.Reader.GetString(patches.Reader.GetMethodDefinition(patch).Name);
            bool prefix = patchName == "Prefix" || patches.Reader.GetMethodDefinition(patch).GetCustomAttributes().Any(a => AttributeName(patches, a) == "HarmonyLib.HarmonyPrefix");
            bool finalizer = patchName == "Finalizer" || patches.Reader.GetMethodDefinition(patch).GetCustomAttributes().Any(a => AttributeName(patches, a) == "HarmonyLib.HarmonyFinalizer");
            int firstInjection = 0;
            if (finalizer)
            {
                Assert.True(signature.ReturnType == "System.Void" || signature.ReturnType == "System.Exception",
                    "Finalizer return requires System.Void or System.Exception: " + context);
            }
            else if (prefix)
            {
                Assert.Contains(signature.ReturnType, new[] { "System.Void", "System.Boolean" });
            }
            else if (signature.ReturnType != "System.Void")
            {
                Assert.True(signature.ReturnType == originalSignature.ReturnType,
                    "Postfix return incompatible with exact target: " + context);
                // Harmony 2.4.2 AddPostfixes consumes the first parameter by type,
                // independently of its name, before interpreting any injections.
                Assert.True(names.Length > 0 && signature.ParameterTypes[0] == signature.ReturnType,
                    "Returning postfix requires an exact passthrough first parameter: " + context);
                firstInjection = 1;
            }
            for (int i = firstInjection; i < names.Length; i++)
            {
                string name = names[i];
                string actual = Element(signature.ParameterTypes[i]);
                string expected;
                if (name == "__instance")
                {
                    Assert.True(originalSignature.Header.IsInstance, "Static target cannot inject __instance: " + context);
                    Assert.True(Assignable(targets, targetType, actual), "Incompatible __instance " + actual + ": " + context);
                    continue;
                }
                if (name == "__state") { stateTypes.Add(actual); continue; }
                if (name == "__exception")
                {
                    Assert.True(finalizer, "__exception injection requires a finalizer: " + context);
                    Assert.True(signature.ParameterTypes[i] == "System.Exception" || signature.ParameterTypes[i] == "System.Exception&",
                        "__exception injection requires System.Exception or System.Exception&: " + context);
                    continue;
                }
                if (name == "__result")
                {
                    Assert.True(originalSignature.ReturnType != "System.Void", "Cannot inject __result from System.Void: " + context);
                    // Harmony 2.4.2 keeps the managed reference for ref returns; stripping
                    // both sides would incorrectly accept a value parameter for int&.
                    expected = originalSignature.ReturnType;
                    string resultType = expected.EndsWith("&", StringComparison.Ordinal) ? signature.ParameterTypes[i] : actual;
                    Assert.True(Assignable(targets, expected, resultType), "Incompatible __result " + resultType + " for " + expected + ": " + context);
                    continue;
                }
                else if (name == "__runOriginal" || name == "__args")
                {
                    expected = name == "__runOriginal" ? "System.Boolean" : "System.Object[]";
                    Assert.True(signature.ParameterTypes[i] == expected, "Readonly injection '" + name + "' requires " + expected + ": " + context);
                    continue;
                }
                else if (name == "__originalMethod")
                {
                    string originalName = targets.Reader.GetString(targets.Reader.GetMethodDefinition(original).Name);
                    string methodType = originalName == ".ctor" || originalName == ".cctor" ? "System.Reflection.ConstructorInfo" : "System.Reflection.MethodInfo";
                    Assert.True(signature.ParameterTypes[i] == actual && (actual == "System.Reflection.MethodBase" || actual == methodType),
                        "Incompatible __originalMethod " + actual + " for " + methodType + ": " + context);
                    continue;
                }
                else if (name.StartsWith("___", StringComparison.Ordinal))
                {
                    var current = targets.Type(targetType);
                    var fields = new List<FieldDefinitionHandle>();
                    for (int depth = 0; depth < 64; depth++)
                    {
                        var definition = targets.Reader.GetTypeDefinition(current);
                        fields.AddRange(definition.GetFields().Where(h => targets.Reader.GetString(targets.Reader.GetFieldDefinition(h).Name) == name.Substring(3)));
                        if (fields.Count > 0 || definition.BaseType.IsNil || !targets.TryType(targets.MemberName(definition.BaseType), out current))
                        {
                            break;
                        }
                    }
                    Assert.Single(fields);
                    var field = targets.Reader.GetFieldDefinition(fields.Single());
                    expected = field.DecodeSignature(targets.Names, (object)null);
                    Assert.True((field.Attributes & System.Reflection.FieldAttributes.Static) != 0 || originalSignature.Header.IsInstance,
                        "Instance field '" + name + "' needs an instance target: " + context);
                    // Harmony emits ldfld/ldflda here without boxing. Managed references
                    // must keep the exact element type; a value field cannot become object.
                    bool byref = signature.ParameterTypes[i].EndsWith("&", StringComparison.Ordinal);
                    Assert.True(byref ? signature.ParameterTypes[i] == expected + "&" :
                        actual == expected || (!IsValueType(targets, expected) && Assignable(targets, expected, actual)),
                        "Incompatible field injection '" + name + "' type " + signature.ParameterTypes[i] + " for " + expected + ": " + context);
                    continue;
                }
                else
                {
                    int index = name.StartsWith("__", StringComparison.Ordinal) && int.TryParse(name.Substring(2), out int numeric) ? numeric : Array.IndexOf(originals, name);
                    Assert.True(index >= 0 && index < originals.Length, "Parameter '" + name + "' absent from exact target: " + context);
                    expected = Element(originalSignature.ParameterTypes[index]);
                }
                Assert.True(Assignable(targets, expected, actual), "Parameter '" + name + "' type " + actual + " differs from " + expected + ": " + context);
            }
        }

        private static string Element(string type) => type.EndsWith("&", StringComparison.Ordinal) ? type.Substring(0, type.Length - 1) : type;
        private static bool IsValueType(CompiledAssemblyContract assembly, string type)
        {
            if (type == "System.Object" || type == "System.String" || type.EndsWith("[]", StringComparison.Ordinal))
            {
                return false;
            }

            if (new[] { "System.Boolean", "System.Byte", "System.SByte", "System.Char", "System.Int16", "System.UInt16", "System.Int32", "System.UInt32",
                "System.Int64", "System.UInt64", "System.IntPtr", "System.UIntPtr", "System.Single", "System.Double", "System.Decimal" }.Contains(type))
            {
                return true;
            }

            if (!assembly.TryType(type, out var handle))
            {
                throw new NotSupportedException("Cannot resolve injected field type " + type);
            }

            string parent = assembly.MemberName(assembly.Reader.GetTypeDefinition(handle).BaseType);
            return parent == "System.ValueType" || parent == "System.Enum";
        }
        private static bool Assignable(CompiledAssemblyContract assembly, string target, string wanted)
        {
            if (target.EndsWith("&", StringComparison.Ordinal) || wanted.EndsWith("&", StringComparison.Ordinal))
            {
                return target == wanted;
            }

            if (wanted == "System.Object")
            {
                return true;
            }

            for (int depth = 0; depth < 64; depth++)
            {
                if (target == wanted)
                {
                    return true;
                }

                if (!assembly.TryType(target, out var type))
                {
                    return false;
                }

                var baseType = assembly.Reader.GetTypeDefinition(type).BaseType;
                if (baseType.IsNil)
                {
                    return false;
                }

                target = assembly.MemberName(baseType);
            }
            throw new BadImageFormatException("Harmony __instance ancestry bound exceeded");
        }
    }
}

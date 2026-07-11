// Relative Path: Eternal/Source/Eternal.Tests/Compatibility/HarmonyPatchSignatureTests.cs
// Creation Date: 11-07-2026
// Last Edit: 11-07-2026
// Author: 0Shard
// Description: Regression test for Harmony patch-time signature failures. Harmony binds
//              non-"__" prefix/postfix parameters to the target method's parameters BY NAME
//              and throws at patch time on a mismatch. Two such bugs shipped (bool __result
//              on void CheckRemoveMapNow; "Trait t" vs GainTrait's "trait"), each silently
//              aborting PatchAll for every class after it. This test cross-checks every
//              attribute-targeted patch class in Eternal.dll against the real game signatures
//              using PE metadata only (CauseDeathNowTests pattern) — no CLR loading of
//              Verse/RimWorld types, so it runs under Mono without Assembly-CSharp.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Eternal.Tests.Compatibility
{
    /// <summary>
    /// Validates that every prefix/postfix parameter name in Eternal's attribute-targeted
    /// Harmony patch classes exists on the target method. Classes resolving their target via
    /// TargetMethod()/Prepare() reflection (RimImmortal, SOS2, VGE crash-landing, Odyssey)
    /// carry no typeof(...) in their attributes and are skipped — their targets live in
    /// optional mod assemblies that are not present at test time.
    /// </summary>
    public class HarmonyPatchSignatureTests
    {
        private static readonly string EternalDllPath = LocateBesideTestAssembly("Eternal.dll");

        /// <summary>Injection-style parameter names Harmony resolves without a target-name match.</summary>
        private static bool IsInjectedParameter(string parameterName) =>
            parameterName.StartsWith("__", StringComparison.Ordinal);

        private static string LocateBesideTestAssembly(string fileName)
        {
            string codeBase = typeof(HarmonyPatchSignatureTests).Assembly.CodeBase;
            string testDllDir = Path.GetDirectoryName(new Uri(codeBase).LocalPath);
            return Path.Combine(testDllDir, fileName);
        }

        /// <summary>
        /// Assembly-CSharp source for target signatures: prefer the Krafs reference assembly the
        /// project compiles against (restored by NuGet, version-pinned in the csproj), fall back
        /// to an installed game copy.
        /// </summary>
        private static string LocateAssemblyCSharp()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string nugetRoot = Path.Combine(home, ".nuget", "packages", "krafs.rimworld.ref");
            if (Directory.Exists(nugetRoot))
            {
                string candidate = Directory.GetDirectories(nugetRoot)
                    .OrderBy(versionDir => versionDir, StringComparer.Ordinal)
                    .Select(versionDir => Path.Combine(versionDir, "ref", "net472", "Assembly-CSharp.dll"))
                    .LastOrDefault(File.Exists);
                if (candidate != null)
                    return candidate;
            }

            string steamCopy = Path.Combine(home,
                ".local", "share", "Steam", "steamapps", "common", "RimWorld",
                "RimWorldLinux_Data", "Managed", "Assembly-CSharp.dll");
            return File.Exists(steamCopy) ? steamCopy : null;
        }

        // -----------------------------------------------------------------
        // The test
        // -----------------------------------------------------------------

        [Fact]
        public void AllAttributeTargetedPatchClasses_ParameterNames_MatchTargetSignature()
        {
            Assert.True(File.Exists(EternalDllPath),
                $"Eternal.dll not found at: {EternalDllPath}. Run dotnet build first.");
            string gameDllPath = LocateAssemblyCSharp();
            Assert.False(gameDllPath == null,
                "Assembly-CSharp.dll not found in the Krafs NuGet cache or the Steam install. " +
                "Run dotnet restore first.");

            List<PatchClassTarget> patchClasses = ReadAttributeTargetedPatchClasses(EternalDllPath);

            // Decoder sanity guard: if attribute decoding silently breaks, this test must fail
            // rather than pass on an empty set. TraitSet/MapPawns/CompRottable/Pawn_HealthTracker
            // style classes guarantee well above this floor.
            Assert.True(patchClasses.Count >= 5,
                $"Only {patchClasses.Count} attribute-targeted patch classes decoded from Eternal.dll — " +
                "the attribute decoder is likely broken.");

            Dictionary<string, HashSet<string>> gameMethodParams = ReadMethodParameterNames(
                gameDllPath,
                patchClasses.Select(patch => patch.TargetTypeFullName).ToHashSet());

            var violations = new List<string>();
            int validatedClasses = 0;

            foreach (PatchClassTarget patchClass in patchClasses)
            {
                string targetKey = patchClass.TargetTypeFullName + "::" + patchClass.TargetMethodName;
                if (!gameMethodParams.TryGetValue(targetKey, out HashSet<string> targetParamNames))
                {
                    violations.Add($"{patchClass.PatchClassFullName}: target method " +
                        $"{targetKey} not found in {Path.GetFileName(gameDllPath)}");
                    continue;
                }

                validatedClasses++;
                foreach (PatchMethod patchMethod in patchClass.PatchMethods)
                {
                    foreach (string parameterName in patchMethod.ParameterNames)
                    {
                        if (IsInjectedParameter(parameterName))
                            continue;
                        if (!targetParamNames.Contains(parameterName))
                        {
                            violations.Add(
                                $"{patchClass.PatchClassFullName}.{patchMethod.Name}: parameter " +
                                $"'{parameterName}' does not exist on {targetKey} " +
                                $"(has: {string.Join(", ", targetParamNames.OrderBy(n => n))}) — " +
                                "Harmony binds by name and will throw at patch time.");
                        }
                    }
                }
            }

            Assert.True(validatedClasses > 0, "No patch class was validated against the game assembly.");
            Assert.True(violations.Count == 0,
                "Harmony signature mismatches found:\n" + string.Join("\n", violations));
        }

        // -----------------------------------------------------------------
        // Eternal.dll side: patch classes and their attribute-declared targets
        // -----------------------------------------------------------------

        private sealed class PatchClassTarget
        {
            public string PatchClassFullName;
            public string TargetTypeFullName;
            public string TargetMethodName;
            public List<PatchMethod> PatchMethods = new List<PatchMethod>();
        }

        private sealed class PatchMethod
        {
            public string Name;
            public List<string> ParameterNames = new List<string>();
        }

        private static List<PatchClassTarget> ReadAttributeTargetedPatchClasses(string dllPath)
        {
            var patchClasses = new List<PatchClassTarget>();

            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            MetadataReader reader = peReader.GetMetadataReader();

            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
            {
                TypeDefinition typeDef = reader.GetTypeDefinition(typeHandle);

                string targetTypeFullName = null;
                string targetMethodName = null;
                int methodType = 0; // HarmonyLib.MethodType.Normal
                bool hasHarmonyPatchAttribute = false;

                foreach (CustomAttributeHandle attrHandle in typeDef.GetCustomAttributes())
                {
                    CustomAttribute attr = reader.GetCustomAttribute(attrHandle);
                    if (GetAttributeTypeName(reader, attr) != "HarmonyPatch")
                        continue;
                    hasHarmonyPatchAttribute = true;

                    CustomAttributeValue<string> decoded;
                    try
                    {
                        decoded = attr.DecodeValue(new NameOnlyTypeProvider());
                    }
                    catch
                    {
                        continue; // Undecodable overload — treated like a reflection-targeted class below.
                    }

                    foreach (CustomAttributeTypedArgument<string> fixedArg in decoded.FixedArguments)
                    {
                        if (fixedArg.Type == "System.Type" && targetTypeFullName == null &&
                            fixedArg.Value is string serializedTypeName)
                        {
                            targetTypeFullName = serializedTypeName.Split(',')[0].Trim();
                        }
                        else if (fixedArg.Type == "System.String" && targetMethodName == null &&
                            fixedArg.Value is string methodName)
                        {
                            targetMethodName = methodName;
                        }
                        else if (fixedArg.Type != null && fixedArg.Type.EndsWith(".MethodType", StringComparison.Ordinal) &&
                            fixedArg.Value is int methodTypeValue)
                        {
                            methodType = methodTypeValue;
                        }
                    }
                }

                // Reflection-targeted classes ([HarmonyPatch] with TargetMethod()) have the
                // attribute but no typeof/name arguments — nothing to validate statically.
                if (!hasHarmonyPatchAttribute || targetTypeFullName == null || targetMethodName == null)
                    continue;

                // HarmonyLib.MethodType: 1 = Getter, 2 = Setter.
                if (methodType == 1)
                    targetMethodName = "get_" + targetMethodName;
                else if (methodType == 2)
                    targetMethodName = "set_" + targetMethodName;

                var patchClass = new PatchClassTarget
                {
                    PatchClassFullName = GetTypeFullName(reader, typeDef),
                    TargetTypeFullName = targetTypeFullName,
                    TargetMethodName = targetMethodName,
                };

                foreach (MethodDefinitionHandle methodHandle in typeDef.GetMethods())
                {
                    MethodDefinition methodDef = reader.GetMethodDefinition(methodHandle);
                    if (!IsPrefixOrPostfix(reader, methodDef))
                        continue;

                    var patchMethod = new PatchMethod { Name = reader.GetString(methodDef.Name) };
                    foreach (ParameterHandle parameterHandle in methodDef.GetParameters())
                    {
                        Parameter parameter = reader.GetParameter(parameterHandle);
                        if (parameter.SequenceNumber == 0)
                            continue; // Return-value pseudo-parameter.
                        patchMethod.ParameterNames.Add(reader.GetString(parameter.Name));
                    }
                    patchClass.PatchMethods.Add(patchMethod);
                }

                if (patchClass.PatchMethods.Count > 0)
                    patchClasses.Add(patchClass);
            }

            return patchClasses;
        }

        private static bool IsPrefixOrPostfix(MetadataReader reader, MethodDefinition methodDef)
        {
            string methodName = reader.GetString(methodDef.Name);
            if (methodName == "Prefix" || methodName == "Postfix")
                return true;

            foreach (CustomAttributeHandle attrHandle in methodDef.GetCustomAttributes())
            {
                string attrName = GetAttributeTypeName(reader, reader.GetCustomAttribute(attrHandle));
                if (attrName == "HarmonyPrefix" || attrName == "HarmonyPostfix")
                    return true;
            }
            return false;
        }

        // -----------------------------------------------------------------
        // Assembly-CSharp side: parameter names of the targeted methods
        // -----------------------------------------------------------------

        /// <summary>
        /// Returns "Namespace.Type::Method" → union of parameter names across all overloads,
        /// keyed by the WANTED type but collected up its base-type chain, because Harmony's
        /// [HarmonyPatch(typeof(X), "m")] resolves inherited methods too. Union across
        /// overloads keeps the check conservative for overloaded targets.
        /// </summary>
        private static Dictionary<string, HashSet<string>> ReadMethodParameterNames(
            string dllPath, HashSet<string> wantedTypeFullNames)
        {
            var methodParams = new Dictionary<string, HashSet<string>>();

            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            MetadataReader reader = peReader.GetMetadataReader();

            var typeHandlesByFullName = new Dictionary<string, TypeDefinitionHandle>();
            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
            {
                typeHandlesByFullName[GetTypeFullName(reader, reader.GetTypeDefinition(typeHandle))] = typeHandle;
            }

            foreach (string wantedTypeFullName in wantedTypeFullNames)
            {
                if (!typeHandlesByFullName.TryGetValue(wantedTypeFullName, out TypeDefinitionHandle currentHandle))
                    continue;

                // Walk the base chain while it stays inside this assembly (System.Object and
                // cross-assembly bases end the walk; game targets all live in Assembly-CSharp).
                while (true)
                {
                    TypeDefinition typeDef = reader.GetTypeDefinition(currentHandle);
                    foreach (MethodDefinitionHandle methodHandle in typeDef.GetMethods())
                    {
                        MethodDefinition methodDef = reader.GetMethodDefinition(methodHandle);
                        string key = wantedTypeFullName + "::" + reader.GetString(methodDef.Name);
                        if (!methodParams.TryGetValue(key, out HashSet<string> parameterNames))
                        {
                            parameterNames = new HashSet<string>();
                            methodParams[key] = parameterNames;
                        }
                        foreach (ParameterHandle parameterHandle in methodDef.GetParameters())
                        {
                            Parameter parameter = reader.GetParameter(parameterHandle);
                            if (parameter.SequenceNumber != 0)
                                parameterNames.Add(reader.GetString(parameter.Name));
                        }
                    }

                    if (typeDef.BaseType.IsNil || typeDef.BaseType.Kind != HandleKind.TypeDefinition)
                        break;
                    currentHandle = (TypeDefinitionHandle)typeDef.BaseType;
                }
            }

            return methodParams;
        }

        // -----------------------------------------------------------------
        // Metadata helpers
        // -----------------------------------------------------------------

        private static string GetTypeFullName(MetadataReader reader, TypeDefinition typeDef)
        {
            string ns = reader.GetString(typeDef.Namespace);
            string name = reader.GetString(typeDef.Name);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        private static string GetAttributeTypeName(MetadataReader reader, CustomAttribute attr)
        {
            switch (attr.Constructor.Kind)
            {
                case HandleKind.MemberReference:
                    MemberReference memberRef = reader.GetMemberReference((MemberReferenceHandle)attr.Constructor);
                    if (memberRef.Parent.Kind == HandleKind.TypeReference)
                        return reader.GetString(reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent).Name);
                    return null;
                case HandleKind.MethodDefinition:
                    MethodDefinition ctorDef = reader.GetMethodDefinition((MethodDefinitionHandle)attr.Constructor);
                    return reader.GetString(reader.GetTypeDefinition(ctorDef.GetDeclaringType()).Name);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Minimal ICustomAttributeTypeProvider that represents every type as its full-name string —
        /// enough to classify HarmonyPatch fixed arguments (System.Type / System.String / MethodType).
        /// </summary>
        private sealed class NameOnlyTypeProvider : ICustomAttributeTypeProvider<string>
        {
            public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;
            public string GetSystemType() => "System.Type";
            public string GetSZArrayType(string elementType) => elementType + "[]";
            public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            {
                TypeDefinition typeDef = reader.GetTypeDefinition(handle);
                return GetTypeFullName(reader, typeDef);
            }
            public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            {
                TypeReference typeRef = reader.GetTypeReference(handle);
                string ns = reader.GetString(typeRef.Namespace);
                string name = reader.GetString(typeRef.Name);
                return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
            }
            public string GetTypeFromSerializedName(string name) => name;
            public bool IsSystemType(string type) => type == "System.Type";
            public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
        }
    }
}

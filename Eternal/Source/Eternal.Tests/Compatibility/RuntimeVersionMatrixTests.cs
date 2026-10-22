// Author: 0Shard
// Description: Persist independently identified runtime metadata matrices; metadata is not game-host proof.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Xml.Linq;
using Eternal.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Eternal.Tests.Compatibility
{
    public class RuntimeVersionMatrixTests
    {
        private readonly ITestOutputHelper output;
        public RuntimeVersionMatrixTests(ITestOutputHelper output) { this.output = output; }

        [Theory]
        [InlineData("1.6")]
        [InlineData("1.7")]
        public void SharedArtifact_ExternalSignatures_AndHarmonyTargets_ResolveAgainstRuntime(string version)
        {
            var manifest = CompiledBuildReferences.CapturedManifest;
            var capture = manifest.Element("runtimeTargets");
            Assert.NotNull(capture);
            var target = capture.Elements("runtime").Single(r => (string)r.Attribute("version") == version);
            var matrix = new XElement("runtimeMatrix", new XAttribute("schema", "1"), new XAttribute("version", version),
                new XAttribute("configuration", (string)manifest.Attribute("configuration")),
                new XAttribute("scope", "metadata only; game-host and optional-mod integration proof unavailable"),
                new XElement("compilerProvenance", new XAttribute("rimworldPackage", (string)manifest.Attribute("rimworldPackage")),
                    manifest.Elements("reference").Select(r => new XElement(r))),
                new XElement(manifest.Element("artifact")), new XElement("runtimeInputs", new XElement(target)));
            string directory = Path.GetDirectoryName((string)manifest.Element("capture").Attribute("path"));
            string destination = Path.Combine(directory, "runtime-matrix-" + manifest.Attribute("configuration").Value + "-" + version + ".xml");
            var errors = new List<string>();
            try
            {
                if ((string)target.Attribute("status") == "mismatch")
                {
                    throw new InvalidOperationException((string)target.Attribute("mismatch"));
                }

                var rows = target.Elements().Where(r => r.Name != "missing").ToArray();
                foreach (var row in rows.Where(r => r.Attribute("sha256") != null))
                {
                    string path = CompiledBuildReferences.ValidatedPath(row);
                    var recorded = matrix.Element("runtimeInputs").Element("runtime").Elements(row.Name)
                        .Single(r => (string)r.Attribute("path") == path);
                    if (row.Name == "versionFile")
                    {
                        Assert.Equal((string)row.Attribute("text"), File.ReadAllText(path).Trim());
                        Assert.Equal((string)target.Attribute("expectedVersion"), File.ReadAllText(path).Trim());
                    }
                    else
                    {
                        var identity = AssemblyName.GetAssemblyName(path);
                        recorded.SetAttributeValue("identity", identity.FullName);
                        if (row.Name == "harmony")
                        {
                            Assert.Equal("0Harmony", identity.Name);
                            Assert.Equal((string)target.Attribute("expectedHarmonyVersion"), identity.Version.ToString());
                        }
                    }
                }

                if ((string)target.Attribute("status") == "unavailable")
                {
                    matrix.SetAttributeValue("status", "unavailable");
                    output.WriteLine("UNAVAILABLE runtime " + version + ": " + string.Join("; ", target.Elements("missing").Select(r => (string)r.Attribute("path"))));
                    Assert.NotEqual("require_installed", (string)capture.Attribute("unavailablePolicy"));
                    return;
                }

                using var production = CompiledBuildReferences.OpenProduction();
                using var runtime = new RuntimeMetadataResolver(rows.Where(r => r.Name != "versionFile").Select(r => (string)r.Attribute("path")),
                    (string)target.Attribute("unityRedirect"));
                var evidence = new XElement("signatures");
                matrix.Add(evidence);
                ValidateReferences(production, runtime, evidence, errors, version == "1.7");
                string gamePath = (string)target.Elements("assembly").Single(r => Path.GetFileName((string)r.Attribute("path")) == "Assembly-CSharp.dll").Attribute("path");
                using var game = new CompiledAssemblyContract(gamePath);
                var dynamic = new List<string>();
                int patched = 0;
                try
                {
                    patched = HarmonyPatchSignatureTests.Validate(production, game, dynamic);
                }
                catch (Exception error)
                {
                    errors.Add("Harmony patch signatures: " + error.Message);
                }

                var harmony = new XElement("harmonyTargets", new XAttribute("validatedPatchMethods", patched));
                matrix.Add(harmony);
                RecordHarmonyTargets(production, game, harmony, errors);
                RecordNativeDynamicTargets(production, game, harmony, errors);
                // Dynamic resolvers depend on enabled DLC/mods and loaded runtime
                // state. Persist every class; resolving metadata cannot certify that
                // Harmony installed its patches or that an optional mod is compatible.
                foreach (string name in DynamicPatchClasses(production))
                {
                    harmony.Add(new XElement("dynamic", new XAttribute("patch", name), new XAttribute("status", "runtime_unavailable"),
                        new XAttribute("reason", "reflection resolver and Prepare require game-host execution with enabled dependencies")));
                }

                Assert.True(patched >= 15, "Runtime Harmony target discovery unexpectedly small");
                matrix.SetAttributeValue("status", errors.Count == 0 ? "metadata_pass" : "metadata_fail");
                output.WriteLine("Runtime " + version + ": " + evidence.Elements().Count(r => r.Name != "clrSyntheticMember") + " external signatures, " + patched +
                    " annotated patch methods; dynamic/game-host proof unavailable. Evidence: " + destination);
                Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
            }
            catch (Exception error)
            {
                matrix.SetAttributeValue("status", "metadata_fail");
                matrix.Add(new XElement("error", error.Message));
                throw;
            }
            finally
            {
                matrix.Add(errors.Select(error => new XElement("error", error)));
                matrix.Save(destination);
            }
        }

        private static bool ExternalScope(string scope)
            => scope == "Assembly-CSharp" || scope == "0Harmony" || scope == "System.Numerics" ||
                (scope != null && scope.StartsWith("UnityEngine", StringComparison.Ordinal));

        private static void ValidateReferences(CompiledAssemblyContract production, RuntimeMetadataResolver runtime, XElement evidence, List<string> errors,
            bool frameworkForwarding)
        {
            void Check(XElement row, Action resolve)
            {
                evidence.Add(row);
                try
                {
                    resolve();
                    row.SetAttributeValue("status", "resolved");
                }
                catch (Exception error)
                {
                    row.SetAttributeValue("status", "mismatch");
                    row.SetAttributeValue("error", error.Message);
                    errors.Add(error.Message);
                }
            }

            foreach (var handle in production.Reader.TypeReferences)
            {
                string scope = RuntimeMetadataResolver.ReferenceAssembly(production, handle);
                if (!ExternalScope(scope) && !(frameworkForwarding && scope == "mscorlib"))
                {
                    continue;
                }

                string name = production.MemberName(handle);
                var row = new XElement(scope == "mscorlib" ? "frameworkType" : "type", new XAttribute("scope", scope), new XAttribute("name", name));
                Check(row, () =>
                {
                    var resolved = runtime.Type(scope, name);
                    row.SetAttributeValue("resolvedAssembly", Path.GetFileName(resolved.Assembly.Path));
                });
            }

            var fieldUsage = new Dictionary<MemberReferenceHandle, HashSet<bool>>();
            foreach (var handle in production.Reader.MethodDefinitions)
            {
                if (production.Reader.GetMethodDefinition(handle).RelativeVirtualAddress == 0)
                {
                    continue;
                }

                foreach (var instruction in production.Instructions(handle))
                {
                    if (instruction.Code.OperandType != OperandType.InlineField || instruction.MemberHandle.Kind != HandleKind.MemberReference)
                    {
                        continue;
                    }

                    var field = (MemberReferenceHandle)instruction.MemberHandle;
                    if (!fieldUsage.TryGetValue(field, out var usage))
                    {
                        fieldUsage.Add(field, usage = new HashSet<bool>());
                    }

                    usage.Add(instruction.Code == OpCodes.Ldsfld || instruction.Code == OpCodes.Ldsflda || instruction.Code == OpCodes.Stsfld);
                }
            }

            foreach (var handle in production.Reader.MemberReferences)
            {
                var reference = production.Reader.GetMemberReference(handle);
                string scope = RuntimeMetadataResolver.ReferenceAssembly(production, reference.Parent);
                if (scope == null && reference.Parent.Kind == HandleKind.TypeSpecification)
                {
                    evidence.Add(new XElement("clrSyntheticMember", new XAttribute("owner", production.MemberName(reference.Parent)),
                        new XAttribute("name", production.Reader.GetString(reference.Name)),
                        new XAttribute("status", "not_an_external_assembly_member"),
                        new XAttribute("reason", "array or generic-variable member; external element/argument TypeRefs are checked separately")));
                }

                if (!ExternalScope(scope))
                {
                    continue;
                }

                string owner = production.MemberName(reference.Parent), name = production.Reader.GetString(reference.Name);
                var row = new XElement(reference.GetKind() == MemberReferenceKind.Method ? "method" : "field",
                    new XAttribute("scope", scope), new XAttribute("owner", owner), new XAttribute("name", name));
                Check(row, () =>
                {
                    RuntimeMetadataResolver.ResolvedMember resolved;
                    if (reference.GetKind() == MemberReferenceKind.Method)
                    {
                        var signature = reference.DecodeMethodSignature(production.Names, (object)null);
                        row.SetAttributeValue("compiledSignature", RuntimeMetadataResolver.MethodSignature(signature, Array.Empty<string>()));
                        resolved = runtime.Method(scope, owner, name, signature);
                    }
                    else
                    {
                        string signature = reference.DecodeFieldSignature(production.Names, (object)null);
                        row.SetAttributeValue("compiledSignature", signature);
                        if (!fieldUsage.TryGetValue(handle, out var usage) || usage.Count != 1)
                        {
                            throw new InvalidOperationException("Missing/contradictory compiled field static usage: " + owner + "." + name);
                        }

                        resolved = runtime.Field(scope, owner, name, signature, usage.Single());
                    }

                    row.SetAttributeValue("resolvedAssembly", Path.GetFileName(resolved.Owner.Assembly.Path));
                    row.SetAttributeValue("declaringType", resolved.Owner.Name);
                    row.SetAttributeValue("resolvedSignature", resolved.Signature);
                    row.SetAttributeValue("static", resolved.Static);
                });
            }
        }

        private static IEnumerable<string> DynamicPatchClasses(CompiledAssemblyContract production)
        {
            foreach (var handle in production.Reader.TypeDefinitions)
            {
                var type = production.Reader.GetTypeDefinition(handle);
                string name = production.MemberName(handle);
                if (!HarmonyPatchSignatureTests.ReadTarget(production, type.GetCustomAttributes(), new HarmonyPatchSignatureTests.Target(), name))
                {
                    continue;
                }

                if (production.Methods(name, "TargetMethod").Any() || production.Methods(name, "TargetMethods").Any())
                {
                    yield return name;
                }
            }
        }

        private static void RecordNativeDynamicTargets(CompiledAssemblyContract production, CompiledAssemblyContract game, XElement evidence, List<string> errors)
        {
            using var compiler = new CompiledAssemblyContract(CompiledBuildReferences.GameReferencePath);
            foreach (string patch in DynamicPatchClasses(production))
            {
                string shortName = patch.Substring(patch.LastIndexOf('.') + 1);
                var originals = new List<MethodDefinitionHandle>();
                try
                {
                    if (shortName == "GravshipUtility_AbandonMap_Patch" || shortName == "GravshipUtility_PreLaunchConfirmation_Patch")
                    {
                        string method = shortName == "GravshipUtility_AbandonMap_Patch" ? "AbandonMap" : "PreLaunchConfirmation";
                        var expected = compiler.Signature(compiler.Method("RimWorld.GravshipUtility", method, inherited: true));
                        originals.Add(game.Method("RimWorld.GravshipUtility", method, expected.ParameterTypes.ToArray(), expected.ReturnType,
                            expected.GenericParameterCount, !expected.Header.IsInstance, inherited: true));
                    }
                    else if (shortName == "GroundMapParent_Destroy_Patch")
                    {
                        foreach (var typeHandle in game.Reader.TypeDefinitions)
                        {
                            string typeName = game.MemberName(typeHandle);
                            if (typeName != "RimWorld.Planet.WorldObject" && !DerivesFrom(game, typeHandle, "RimWorld.Planet.MapParent"))
                            {
                                continue;
                            }

                            originals.AddRange(game.Reader.GetTypeDefinition(typeHandle).GetMethods().Where(h =>
                            {
                                var method = game.Reader.GetMethodDefinition(h);
                                var signature = game.Signature(h);
                                return game.Reader.GetString(method.Name) == "Destroy" &&
                                    (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public &&
                                    (method.Attributes & MethodAttributes.Abstract) == 0 && signature.Header.IsInstance &&
                                    signature.GenericParameterCount == 0 && signature.ParameterTypes.Length == 0 && signature.ReturnType == "System.Void";
                            }));
                        }

                        Assert.NotEmpty(originals);
                    }
                    else
                    {
                        continue;
                    }

                    var stateTypes = new List<string>();
                    foreach (var original in originals)
                    {
                        string targetType = game.MemberName(game.Reader.GetMethodDefinition(original).GetDeclaringType());
                        foreach (var patchMethod in production.Reader.GetTypeDefinition(production.Type(patch)).GetMethods())
                        {
                            string name = production.Reader.GetString(production.Reader.GetMethodDefinition(patchMethod).Name);
                            string attributes = production.AttributeText(production.Reader.GetMethodDefinition(patchMethod).GetCustomAttributes());
                            if (name == "Prefix" || name == "Postfix" || name == "Finalizer" || attributes.Contains("HarmonyLib.HarmonyPrefix..ctor") ||
                                attributes.Contains("HarmonyLib.HarmonyPostfix..ctor") || attributes.Contains("HarmonyLib.HarmonyFinalizer..ctor"))
                            {
                                HarmonyPatchSignatureTests.ValidateParameters(production, patchMethod, game, original, targetType, stateTypes);
                            }
                        }

                        evidence.Add(new XElement("dynamicNative", new XAttribute("patch", patch), new XAttribute("target", game.Describe(original)),
                            new XAttribute("signature", RuntimeMetadataResolver.MethodSignature(game.Signature(original), Array.Empty<string>())),
                            new XAttribute("status", "metadata_resolved"), new XAttribute("runtimeProof", "unavailable")));
                    }

                    Assert.True(stateTypes.Distinct().Count() <= 1, patch + ": incompatible dynamic __state types");
                }
                catch (Exception error)
                {
                    evidence.Add(new XElement("dynamicNative", new XAttribute("patch", patch), new XAttribute("status", "mismatch"),
                        new XAttribute("error", error.Message)));
                    errors.Add(error.Message);
                }
            }
        }

        private static bool DerivesFrom(CompiledAssemblyContract game, TypeDefinitionHandle type, string wanted)
        {
            var visited = new HashSet<TypeDefinitionHandle>();
            while (!type.IsNil && visited.Add(type))
            {
                if (game.MemberName(type) == wanted)
                {
                    return true;
                }

                var parent = game.Reader.GetTypeDefinition(type).BaseType;
                if (parent.IsNil || !game.TryType(game.MemberName(parent), out type))
                {
                    return false;
                }
            }

            return false;
        }

        private static void RecordHarmonyTargets(CompiledAssemblyContract production, CompiledAssemblyContract game, XElement evidence, List<string> errors)
        {
            foreach (var handle in production.Reader.TypeDefinitions)
            {
                var type = production.Reader.GetTypeDefinition(handle);
                string name = production.MemberName(handle);
                var target = new HarmonyPatchSignatureTests.Target();
                HarmonyPatchSignatureTests.ReadTarget(production, type.GetCustomAttributes(), target, name);
                foreach (var methodHandle in type.GetMethods())
                {
                    var method = production.Reader.GetMethodDefinition(methodHandle);
                    string methodName = production.Reader.GetString(method.Name);
                    string attributes = production.AttributeText(method.GetCustomAttributes());
                    if (methodName != "Prefix" && methodName != "Postfix" && methodName != "Finalizer" &&
                        !attributes.Contains("HarmonyLib.HarmonyPrefix..ctor") && !attributes.Contains("HarmonyLib.HarmonyPostfix..ctor") &&
                        !attributes.Contains("HarmonyLib.HarmonyFinalizer..ctor"))
                    {
                        continue;
                    }

                    var selected = target.Copy();
                    HarmonyPatchSignatureTests.ReadTarget(production, method.GetCustomAttributes(), selected, name + "." + methodName);
                    if (selected.Type == null)
                    {
                        continue;
                    }

                    var row = new XElement("annotated", new XAttribute("patch", production.Describe(methodHandle)),
                        new XAttribute("targetType", selected.Type), new XAttribute("targetMethod", selected.MethodName));
                    evidence.Add(row);
                    try
                    {
                        var original = game.Method(selected.Type, selected.MethodName, selected.Parameters, inherited: true);
                        row.SetAttributeValue("signature", RuntimeMetadataResolver.MethodSignature(game.Signature(original), Array.Empty<string>()));
                        row.SetAttributeValue("declaringType", game.MemberName(game.Reader.GetMethodDefinition(original).GetDeclaringType()));
                        row.SetAttributeValue("parameterNames", string.Join(",", game.ParameterNames(original)));
                        row.SetAttributeValue("status", "metadata_resolved");
                    }
                    catch (Exception error)
                    {
                        row.SetAttributeValue("status", "mismatch");
                        row.SetAttributeValue("error", error.Message);
                        errors.Add(error.Message);
                    }
                }
            }
        }
    }
}

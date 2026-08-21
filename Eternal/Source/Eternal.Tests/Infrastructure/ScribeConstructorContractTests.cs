// Relative Path: Eternal/Source/Eternal.Tests/Infrastructure/ScribeConstructorContractTests.cs
// Creation Date: 21-08-2026
// Last Edit: 21-08-2026
// Author: 0Shard
// Description: Regression test for the "Constructor on type ... not found" save-load failure.
//              Scribe_Deep / LookMode.Deep rebuild IExposable instances on load through
//              Activator.CreateInstance(type, ctorArgs) and Eternal passes no ctorArgs, so every
//              concrete IExposable type in Eternal.dll must expose a parameterless constructor.
//              EternalCaravanDeathHandler shipped with only a (Game) constructor, which nulled
//              the handler on every load. Uses PE metadata only (HarmonyPatchSignatureTests
//              pattern) so it runs under Mono without loading Verse types into the CLR.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Eternal.Tests.Infrastructure
{
    /// <summary>
    /// Asserts that every concrete Verse.IExposable type compiled into Eternal.dll has a
    /// parameterless constructor, except the components RimWorld itself instantiates with
    /// ctorArgs (GameComponent gets the Game, MapComponent gets the Map).
    /// </summary>
    public class ScribeConstructorContractTests
    {
        private const string ExposableInterfaceName = "IExposable";
        private const string ExposableInterfaceNamespace = "Verse";

        /// <summary>
        /// Types the engine constructs with ctorArgs because they are registered components.
        /// Anything Eternal nests inside another IExposable via Scribe_Deep is NOT exempt,
        /// even if it inherits from GameComponent/MapComponent without being registered.
        /// </summary>
        private static readonly HashSet<string> EngineConstructedComponents = new HashSet<string>(StringComparer.Ordinal)
        {
            "Eternal.Eternal_Component",      // GameComponent: Game.FillComponents passes the Game
            "Eternal.Map.EternalMapManager",  // MapComponent: Map.FillComponents passes the Map
        };

        private static string LocateBesideTestAssembly(string fileName)
        {
            string codeBase = typeof(ScribeConstructorContractTests).Assembly.CodeBase;
            string testDllDir = Path.GetDirectoryName(new Uri(codeBase).LocalPath);
            return Path.Combine(testDllDir, fileName);
        }

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

        [Fact]
        public void AllConcreteExposableTypes_HaveParameterlessConstructor()
        {
            string eternalDllPath = LocateBesideTestAssembly("Eternal.dll");
            Assert.True(File.Exists(eternalDllPath),
                $"Eternal.dll not found at: {eternalDllPath}. Run dotnet build first.");
            string gameDllPath = LocateAssemblyCSharp();
            Assert.False(gameDllPath == null,
                "Assembly-CSharp.dll not found (Krafs.Rimworld.ref NuGet package or Steam install).");

            using (var eternalStream = File.OpenRead(eternalDllPath))
            using (var eternalPe = new PEReader(eternalStream))
            using (var gameStream = File.OpenRead(gameDllPath))
            using (var gamePe = new PEReader(gameStream))
            {
                MetadataReader eternal = eternalPe.GetMetadataReader();
                MetadataReader game = gamePe.GetMetadataReader();

                var exposableTypes = new List<string>();
                var violations = new List<string>();

                foreach (TypeDefinitionHandle typeHandle in eternal.TypeDefinitions)
                {
                    TypeDefinition type = eternal.GetTypeDefinition(typeHandle);
                    if ((type.Attributes & TypeAttributes.Abstract) != 0)
                        continue;
                    if (!ImplementsExposable(eternal, game, typeHandle))
                        continue;

                    string fullName = FullName(eternal, type);
                    if (EngineConstructedComponents.Contains(fullName))
                        continue;

                    exposableTypes.Add(fullName);
                    if (!HasParameterlessInstanceConstructor(eternal, type))
                        violations.Add(fullName);
                }

                // Guard against a silently vacuous scan: the known Scribe_Deep targets must be seen.
                Assert.Contains("Eternal.Caravan.EternalCaravanDeathHandler", exposableTypes);
                Assert.Contains("Eternal.Corpse.EternalCorpseManager", exposableTypes);
                Assert.Contains("Eternal.Eternal_Hediff", exposableTypes);

                Assert.True(violations.Count == 0,
                    "IExposable types without a parameterless constructor (Scribe cannot rebuild them on load):\n  "
                    + string.Join("\n  ", violations));
            }
        }

        // -----------------------------------------------------------------
        // Metadata helpers
        // -----------------------------------------------------------------

        /// <summary>
        /// Walks the type's ancestry across Eternal.dll and Assembly-CSharp checking each level's
        /// interface list for Verse.IExposable. Base types from other assemblies (mscorlib,
        /// UnityEngine, Harmony) terminate the walk as non-exposable.
        /// </summary>
        private static bool ImplementsExposable(MetadataReader eternal, MetadataReader game, TypeDefinitionHandle handle)
        {
            MetadataReader reader = eternal;
            TypeDefinitionHandle current = handle;
            // Bound the walk; RimWorld hierarchies are shallow and this prevents cycles on odd metadata.
            for (int depth = 0; depth < 32; depth++)
            {
                TypeDefinition type = reader.GetTypeDefinition(current);
                if (DeclaresExposable(reader, type))
                    return true;

                EntityHandle baseHandle = type.BaseType;
                if (baseHandle.IsNil)
                    return false;

                if (baseHandle.Kind == HandleKind.TypeDefinition)
                {
                    current = (TypeDefinitionHandle)baseHandle;
                    continue;
                }

                if (baseHandle.Kind != HandleKind.TypeReference)
                    return false; // TypeSpecification (generic instantiation) — none of Eternal's exposables use one

                TypeReference baseRef = reader.GetTypeReference((TypeReferenceHandle)baseHandle);
                string baseNamespace = reader.GetString(baseRef.Namespace);
                string baseName = reader.GetString(baseRef.Name);
                if (reader == game)
                {
                    // Assembly-CSharp base that itself lives outside Assembly-CSharp: not exposable.
                    if (!TryFindTypeDefinition(game, baseNamespace, baseName, out current))
                        return false;
                    continue;
                }

                if (!TryFindTypeDefinition(game, baseNamespace, baseName, out current))
                    return false;
                reader = game;
            }
            return false;
        }

        private static bool DeclaresExposable(MetadataReader reader, TypeDefinition type)
        {
            foreach (InterfaceImplementationHandle implHandle in type.GetInterfaceImplementations())
            {
                EntityHandle iface = reader.GetInterfaceImplementation(implHandle).Interface;
                string ns, name;
                if (iface.Kind == HandleKind.TypeReference)
                {
                    TypeReference r = reader.GetTypeReference((TypeReferenceHandle)iface);
                    ns = reader.GetString(r.Namespace);
                    name = reader.GetString(r.Name);
                }
                else if (iface.Kind == HandleKind.TypeDefinition)
                {
                    TypeDefinition d = reader.GetTypeDefinition((TypeDefinitionHandle)iface);
                    ns = reader.GetString(d.Namespace);
                    name = reader.GetString(d.Name);
                }
                else
                {
                    continue;
                }

                if (ns == ExposableInterfaceNamespace && name == ExposableInterfaceName)
                    return true;
            }
            return false;
        }

        private static bool TryFindTypeDefinition(MetadataReader reader, string ns, string name, out TypeDefinitionHandle found)
        {
            foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
            {
                TypeDefinition candidate = reader.GetTypeDefinition(handle);
                if (reader.GetString(candidate.Name) == name && reader.GetString(candidate.Namespace) == ns)
                {
                    found = handle;
                    return true;
                }
            }
            found = default(TypeDefinitionHandle);
            return false;
        }

        private static bool HasParameterlessInstanceConstructor(MetadataReader reader, TypeDefinition type)
        {
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != ".ctor")
                    continue;

                BlobReader signature = reader.GetBlobReader(method.Signature);
                SignatureHeader header = signature.ReadSignatureHeader();
                if (header.IsGeneric)
                    signature.ReadCompressedInteger(); // generic parameter count
                int parameterCount = signature.ReadCompressedInteger();
                if (parameterCount == 0)
                    return true;
            }
            return false;
        }

        private static string FullName(MetadataReader reader, TypeDefinition type)
        {
            string name = reader.GetString(type.Name);
            if (type.IsNested)
            {
                TypeDefinition declaring = reader.GetTypeDefinition(type.GetDeclaringType());
                return FullName(reader, declaring) + "+" + name;
            }
            string ns = reader.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }
    }
}

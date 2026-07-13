// Relative Path: Eternal/Source/Eternal.Tests/Compatibility/SpaceModSeamPresenceTests.cs
// Creation Date: 13-07-2026
// Last Edit: 13-07-2026
// Author: 0Shard
// Description: Seam-drift detector for the reflection-targeted space-mod patches, which the
//              attribute-based HarmonyPatchSignatureTests cannot cover. Reads the installed
//              workshop DLLs (SOS2 ShipsHaveInsides.dll, VGE VanillaGravshipExpanded.dll) via
//              PE metadata only - no CLR loading - and asserts every type/member the Eternal
//              patches resolve by name still exists. Skips silently when a mod is not installed
//              (the corresponding patches are Prepare()-gated off in that case too).

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
    /// Asserts the presence of every SOS2/VGE member that Eternal's space-loss patches
    /// resolve via AccessTools at runtime. A failed assertion here means the workshop mod
    /// updated and the named seam moved - fix the patch before shipping.
    /// </summary>
    public class SpaceModSeamPresenceTests
    {
        private static readonly string WorkshopRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "Steam", "steamapps", "workshop", "content", "294100");

        private static readonly string SOS2DllPath = Path.Combine(
            WorkshopRoot, "1909914131", "1.6", "Assemblies", "ShipsHaveInsides.dll");

        private static readonly string VGEDllPath = Path.Combine(
            WorkshopRoot, "3609835606", "1.6", "Assemblies", "VanillaGravshipExpanded.dll");

        /// <summary>(type full name, member name, isField) tuples the SOS2 patches resolve by name.</summary>
        private static readonly (string type, string member, bool isField)[] SOS2Seams =
        {
            ("SaveOurShip2.WorldObjectOrbitingShip", "ShouldRemoveMapNow", false),
            ("SaveOurShip2.WorldObjectOrbitingShip", "Destroy", false),
            ("SaveOurShip2.ShipMapComp", "KillAllOffShip", false),
            ("SaveOurShip2.ShipMapComp", "DeRegisterShuttleMission", false),
            ("SaveOurShip2.ShipMapComp", "get_MapShipCells", false),
            ("SaveOurShip2.ShipMapComp", "get_ShipsOnMap", false),
            ("SaveOurShip2.ShipMapComp+ShuttleMissionData", "shuttle", true),
            ("SaveOurShip2.ShipInteriorMod2", "RemoveShipOrArea", false),
            ("SaveOurShip2.ShipInteriorMod2", "MoveShip", false),
            ("SaveOurShip2.SpaceShipCache", "FloatAndDestroy", false),
            ("SaveOurShip2.SpaceShipCache", "get_Map", false),
            ("SaveOurShip2.SpaceShipCache", "Area", true),
        };

        /// <summary>Members the VGE landing patch resolves by name.</summary>
        private static readonly (string type, string member, bool isField)[] VGESeams =
        {
            ("VanillaGravshipExpanded.GravshipMapGenUtility", "BlockingThings", true),
        };

        [Fact]
        public void SOS2Seams_AllPresent()
        {
            AssertSeamsPresent(SOS2DllPath, SOS2Seams, "SOS2");
        }

        [Fact]
        public void VGESeams_AllPresent()
        {
            AssertSeamsPresent(VGEDllPath, VGESeams, "VGE");
        }

        private static void AssertSeamsPresent(string dllPath,
            (string type, string member, bool isField)[] seams, string modName)
        {
            if (!File.Exists(dllPath))
            {
                // Mod not installed - the patches are Prepare()-gated off, nothing to verify
                return;
            }

            Dictionary<string, (HashSet<string> methods, HashSet<string> fields)> typeMembers =
                ReadTypeMembers(dllPath, seams.Select(s => s.type).ToHashSet());

            var missing = new List<string>();
            foreach (var (type, member, isField) in seams)
            {
                if (!typeMembers.TryGetValue(type, out var members))
                {
                    missing.Add($"{modName}: type {type} not found");
                    continue;
                }

                var pool = isField ? members.fields : members.methods;
                if (!pool.Contains(member))
                {
                    missing.Add($"{modName}: {(isField ? "field" : "method")} {type}::{member} not found");
                }
            }

            Assert.True(missing.Count == 0,
                $"Space-mod seams moved ({modName} updated?) - fix the Eternal patches:\n" +
                string.Join("\n", missing));
        }

        /// <summary>
        /// Reads method and field names for the requested types from PE metadata.
        /// Nested types are keyed as "Namespace.Declaring+Nested".
        /// </summary>
        private static Dictionary<string, (HashSet<string> methods, HashSet<string> fields)> ReadTypeMembers(
            string dllPath, HashSet<string> wantedTypes)
        {
            var membersByType = new Dictionary<string, (HashSet<string>, HashSet<string>)>();

            using (var stream = File.OpenRead(dllPath))
            using (var peReader = new PEReader(stream))
            {
                MetadataReader metadata = peReader.GetMetadataReader();

                foreach (TypeDefinitionHandle typeHandle in metadata.TypeDefinitions)
                {
                    TypeDefinition typeDef = metadata.GetTypeDefinition(typeHandle);
                    string fullName = GetTypeFullName(metadata, typeDef);
                    if (!wantedTypes.Contains(fullName))
                    {
                        continue;
                    }

                    var methods = new HashSet<string>();
                    foreach (MethodDefinitionHandle methodHandle in typeDef.GetMethods())
                    {
                        methods.Add(metadata.GetString(metadata.GetMethodDefinition(methodHandle).Name));
                    }

                    var fields = new HashSet<string>();
                    foreach (FieldDefinitionHandle fieldHandle in typeDef.GetFields())
                    {
                        fields.Add(metadata.GetString(metadata.GetFieldDefinition(fieldHandle).Name));
                    }

                    membersByType[fullName] = (methods, fields);
                }
            }

            return membersByType;
        }

        private static string GetTypeFullName(MetadataReader metadata, TypeDefinition typeDef)
        {
            string name = metadata.GetString(typeDef.Name);

            if (typeDef.IsNested)
            {
                TypeDefinition declaring = metadata.GetTypeDefinition(typeDef.GetDeclaringType());
                string declaringNamespace = metadata.GetString(declaring.Namespace);
                string declaringName = metadata.GetString(declaring.Name);
                return string.IsNullOrEmpty(declaringNamespace)
                    ? $"{declaringName}+{name}"
                    : $"{declaringNamespace}.{declaringName}+{name}";
            }

            string typeNamespace = metadata.GetString(typeDef.Namespace);
            return string.IsNullOrEmpty(typeNamespace) ? name : $"{typeNamespace}.{name}";
        }
    }
}

// Author: 0Shard
// Description: Exact compiled IExposable inheritance and constructors; real Scribe execution is separate.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using Xunit;

namespace Eternal.Tests.Infrastructure
{
    public class ScribeConstructorContractTests
    {
        private static readonly HashSet<string> EngineConstructedComponents = new HashSet<string>(StringComparer.Ordinal)
        {
            "Eternal.Eternal_Component",
            "Eternal.Map.EternalMapManager",
            // Native Game supplies its own ctor argument when loading GameComponents.
            "Eternal.InGameTests.Helpers.DevelopmentGameHostComponent"
        };

        [Fact]
        public void AllConcreteExposableTypes_HaveParameterlessConstructor()
        {
            using var eternal = CompiledBuildReferences.OpenProduction();
            using var game = new CompiledAssemblyContract(CompiledBuildReferences.GameReferencePath);
            var exposableTypes = new List<string>();
            var violations = new List<string>();
            foreach (var handle in eternal.Reader.TypeDefinitions)
            {
                var type = eternal.Reader.GetTypeDefinition(handle);
                if ((type.Attributes & TypeAttributes.Abstract) != 0 || !ImplementsExposable(eternal, game, handle))
                {
                    continue;
                }

                string name = eternal.MemberName(handle);
                if (EngineConstructedComponents.Contains(name))
                {
                    continue;
                }

                exposableTypes.Add(name);
                if (!eternal.Methods(name, ".ctor").Any(h =>
                    eternal.Signature(h).ParameterTypes.Length == 0 && eternal.Signature(h).Header.IsInstance))
                {
                    violations.Add(name);
                }
            }
            Assert.Contains("Eternal.Caravan.EternalCaravanDeathHandler", exposableTypes);
            Assert.Contains("Eternal.Corpse.EternalCorpseManager", exposableTypes);
            Assert.Contains("Eternal.Eternal_Hediff", exposableTypes);
            Assert.True(violations.Count == 0, "Scribe cannot reconstruct types without parameterless constructors:\n" + string.Join("\n", violations));
        }

        private static bool ImplementsExposable(CompiledAssemblyContract eternal, CompiledAssemblyContract game, TypeDefinitionHandle handle)
        {
            var assembly = eternal;
            var seen = new HashSet<string>();
            for (int depth = 0; depth < 64; depth++)
            {
                string identity = assembly.Path + ":" + assembly.MemberName(handle);
                if (!seen.Add(identity))
                {
                    throw new BadImageFormatException("Cyclic Scribe inheritance " + identity);
                }

                var type = assembly.Reader.GetTypeDefinition(handle);
                foreach (var implementation in type.GetInterfaceImplementations())
                {
                    if (IsExposableInterface(assembly, assembly.Reader.GetInterfaceImplementation(implementation).Interface, new HashSet<string>()))
                    {
                        return true;
                    }
                }

                if (type.BaseType.IsNil)
                {
                    return false;
                }

                string baseName = assembly.MemberName(type.BaseType);
                if (assembly.TryType(baseName, out handle))
                {
                    continue;
                }

                if (!game.TryType(baseName, out handle))
                {
                    if (baseName.StartsWith("Verse.", StringComparison.Ordinal) || baseName.StartsWith("RimWorld.", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Unresolved game base in Scribe contract: " + baseName);
                    }

                    return false;
                }
                assembly = game;
            }
            throw new BadImageFormatException("Scribe ancestry bound exceeded");
        }

        private static bool IsExposableInterface(CompiledAssemblyContract assembly, EntityHandle handle, HashSet<string> seen)
        {
            string name = assembly.MemberName(handle);
            if (name == "Verse.IExposable")
            {
                return true;
            }

            if (!seen.Add(name))
            {
                throw new BadImageFormatException("Cyclic Scribe interface " + name);
            }

            if (!assembly.TryType(name, out var type))
            {
                return false;
            }

            return assembly.Reader.GetTypeDefinition(type).GetInterfaceImplementations().Any(h =>
                IsExposableInterface(assembly, assembly.Reader.GetInterfaceImplementation(h).Interface, new HashSet<string>(seen)));
        }
    }
}

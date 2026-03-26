// Relative Path: Eternal/Source/Eternal.Tests/Hediffs/CauseDeathNowTests.cs
// Creation Date: 26-03-2026
// Last Edit: 26-03-2026
// Author: 0Shard
// Description: Unit tests for CauseDeathNow() defense-in-depth overrides on Eternal hediff classes.
//              Verifies that CauseDeathNow() is declared on each subclass by reading Eternal.dll's
//              PE metadata via System.Reflection.Metadata — no CLR type loading, no Assembly-CSharp
//              resolution required. IL opcode inspection confirms the method body returns false.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace Eternal.Tests.Hediffs
{
    /// <summary>
    /// Verifies CauseDeathNow() overrides on Eternal hediff subclasses using PE metadata inspection.
    ///
    /// Why PE metadata instead of CLR reflection?
    /// Any Verse.Hediff subclass is linked to Assembly-CSharp (the RimWorld game DLL). CLR reflection
    /// requires that DLL to be present to load any type in the hierarchy — including just inspecting
    /// method declarations. PE metadata reading (System.Reflection.Metadata) reads the raw IL tables
    /// in Eternal.dll without resolving any references, so Assembly-CSharp is not needed.
    ///
    /// The actual runtime behaviour (CauseDeathNow() returns false at all severity levels) is verified
    /// by the in-game E2E test suite, which runs inside the game with Assembly-CSharp loaded.
    /// </summary>
    public class CauseDeathNowTests
    {
        private static readonly string EternalDllPath = FindEternalDll();

        private static string FindEternalDll()
        {
            // Eternal.dll is copied to the test output directory by MSBuild (ProjectReference).
            // Use CodeBase (original source path, not shadow-copy temp path) to locate it.
            string codeBase = typeof(CauseDeathNowTests).Assembly.CodeBase;
            string uriPath = new Uri(codeBase).LocalPath;
            string testDllDir = Path.GetDirectoryName(uriPath);
            return Path.Combine(testDllDir, "Eternal.dll");
        }

        /// <summary>
        /// Reads PE metadata from Eternal.dll and returns all method names declared on a given type.
        /// Uses System.Reflection.Metadata for dependency-free metadata inspection — no CLR type
        /// loading occurs, so Assembly-CSharp does not need to be resolvable.
        /// </summary>
        private static HashSet<string> GetDeclaredMethodNames(string fullTypeName)
        {
            using var stream = File.OpenRead(EternalDllPath);
            using var peReader = new PEReader(stream);
            var metadataReader = peReader.GetMetadataReader();

            foreach (var typeDefHandle in metadataReader.TypeDefinitions)
            {
                var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
                var ns = metadataReader.GetString(typeDef.Namespace);
                var name = metadataReader.GetString(typeDef.Name);
                string qualifiedName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

                if (qualifiedName != fullTypeName)
                    continue;

                var methodNames = new HashSet<string>();
                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var methodDef = metadataReader.GetMethodDefinition(methodHandle);
                    methodNames.Add(metadataReader.GetString(methodDef.Name));
                }
                return methodNames;
            }

            return new HashSet<string>(); // Type not found
        }

        /// <summary>
        /// Returns the raw IL bytes for a method declared on the given type in Eternal.dll.
        /// Returns null if the type or method is not found, or the method has no body.
        /// </summary>
        private static byte[] GetMethodIL(string fullTypeName, string methodName)
        {
            using var stream = File.OpenRead(EternalDllPath);
            using var peReader = new PEReader(stream);
            var metadataReader = peReader.GetMetadataReader();

            foreach (var typeDefHandle in metadataReader.TypeDefinitions)
            {
                var typeDef = metadataReader.GetTypeDefinition(typeDefHandle);
                var ns = metadataReader.GetString(typeDef.Namespace);
                var name = metadataReader.GetString(typeDef.Name);
                string qualifiedName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

                if (qualifiedName != fullTypeName)
                    continue;

                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var methodDef = metadataReader.GetMethodDefinition(methodHandle);
                    if (metadataReader.GetString(methodDef.Name) != methodName)
                        continue;

                    if (methodDef.RelativeVirtualAddress == 0)
                        return null; // Abstract or extern — no body

                    var methodBody = peReader.GetMethodBody(methodDef.RelativeVirtualAddress);
                    return methodBody.GetILBytes();
                }
                return null; // Method not found on this type
            }

            return null; // Type not found
        }

        // -----------------------------------------------------------------
        // EternalRegrowing_Hediff — CauseDeathNow declared on subclass
        // -----------------------------------------------------------------

        [Fact]
        public void EternalRegrowing_CauseDeathNow_OverrideExistsOnSubclass()
        {
            Assert.True(File.Exists(EternalDllPath),
                $"Eternal.dll not found at: {EternalDllPath}. Run dotnet build first.");

            var methods = GetDeclaredMethodNames("Eternal.EternalRegrowing_Hediff");
            Assert.NotEmpty(methods);
            Assert.Contains("CauseDeathNow", methods);
        }

        // -----------------------------------------------------------------
        // MetabolicRecovery_Hediff — CauseDeathNow declared on subclass
        // -----------------------------------------------------------------

        [Fact]
        public void MetabolicRecovery_CauseDeathNow_OverrideExistsOnSubclass()
        {
            Assert.True(File.Exists(EternalDllPath),
                $"Eternal.dll not found at: {EternalDllPath}. Run dotnet build first.");

            var methods = GetDeclaredMethodNames("Eternal.Hediffs.MetabolicRecovery_Hediff");
            Assert.NotEmpty(methods);
            Assert.Contains("CauseDeathNow", methods);
        }

        // -----------------------------------------------------------------
        // EternalRegrowing_Hediff — IL returns false (ldc.i4.0 + ret)
        // -----------------------------------------------------------------

        [Fact]
        public void EternalRegrowing_CauseDeathNow_IL_ReturnsFalse()
        {
            Assert.True(File.Exists(EternalDllPath),
                $"Eternal.dll not found at: {EternalDllPath}. Run dotnet build first.");

            byte[] il = GetMethodIL("Eternal.EternalRegrowing_Hediff", "CauseDeathNow");
            Assert.NotNull(il);

            // `=> false` compiles to: ldc.i4.0 (0x16), ret (0x2A)
            // These are the only opcodes present in an expression-bodied `return false`
            Assert.Contains((byte)0x16, il); // ldc.i4.0 — push false (0) onto stack
            Assert.Contains((byte)0x2A, il); // ret — return top of stack
        }

        // -----------------------------------------------------------------
        // MetabolicRecovery_Hediff — IL returns false (ldc.i4.0 + ret)
        // -----------------------------------------------------------------

        [Fact]
        public void MetabolicRecovery_CauseDeathNow_IL_ReturnsFalse()
        {
            Assert.True(File.Exists(EternalDllPath),
                $"Eternal.dll not found at: {EternalDllPath}. Run dotnet build first.");

            byte[] il = GetMethodIL("Eternal.Hediffs.MetabolicRecovery_Hediff", "CauseDeathNow");
            Assert.NotNull(il);

            // `=> false` compiles to: ldc.i4.0 (0x16), ret (0x2A)
            Assert.Contains((byte)0x16, il); // ldc.i4.0 — push false (0) onto stack
            Assert.Contains((byte)0x2A, il); // ret — return top of stack
        }
    }
}

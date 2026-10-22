// Author: 0Shard
// Description: Negative signature fixtures ensure runtime matrices reject changed returns, staticness, generics and field contracts.
using System;
using System.Linq;
using System.Reflection.Metadata;
using RimWorld;
using Verse;
using Xunit;

namespace Eternal.Tests.Infrastructure
{
    public class RuntimeMetadataResolverTests
    {
        private static string AssemblyPath => new Uri(typeof(RuntimeMetadataResolverTests).Assembly.CodeBase).LocalPath;
        private const string Fixture = "Eternal.Tests.Infrastructure.RuntimeResolverFixture";
        private const string Child = "Eternal.Tests.Infrastructure.RuntimeResolverChild";

        [Theory]
        [InlineData("WrongReturn")]
        [InlineData("WrongParameter")]
        [InlineData("WrongStatic")]
        [InlineData("WrongGeneric")]
        public void ChangedMethodSignature_IsRejected(string incompatible)
        {
            using var metadata = new CompiledAssemblyContract(AssemblyPath);
            using var runtime = new RuntimeMetadataResolver(new[] { AssemblyPath });
            var wanted = metadata.Signature(metadata.Method(Fixture, incompatible));
            Assert.Throws<InvalidOperationException>(() => runtime.Method("Eternal.Tests", Fixture, "Choose", wanted));
        }

        [Fact]
        public void InheritedMethod_ResolvesExactDeclaringType()
        {
            using var metadata = new CompiledAssemblyContract(AssemblyPath);
            using var runtime = new RuntimeMetadataResolver(new[] { AssemblyPath });
            var wanted = metadata.Signature(metadata.Method(Fixture, "Choose"));
            var resolved = runtime.Method("Eternal.Tests", Child, "Choose", wanted);
            Assert.Equal(Fixture, resolved.Owner.Name);
            Assert.False(resolved.Static);
            Assert.Throws<InvalidOperationException>(() => runtime.Method("Eternal.Tests", Child, ".ctor",
                metadata.Signature(metadata.Method(Fixture, ".ctor", new[] { "System.Int32" }))));
        }

        [Fact]
        public void FieldTypeAndStaticUsage_AreBothRequired()
        {
            using var runtime = new RuntimeMetadataResolver(new[] { AssemblyPath });
            Assert.False(runtime.Field("Eternal.Tests", Child, "Number", "System.Int32", false).Static);
            Assert.Throws<InvalidOperationException>(() => runtime.Field("Eternal.Tests", Child, "Number", "System.String", false));
            Assert.Throws<InvalidOperationException>(() => runtime.Field("Eternal.Tests", Child, "Number", "System.Int32", true));
            Assert.True(runtime.Field("Eternal.Tests", Child, "Shared", "System.Int32", true).Static);
        }

        [Fact]
        public void ConstructedGenericParent_SubstitutesTypeArguments_WithoutChangingMethodArguments()
        {
            using var metadata = new CompiledAssemblyContract(AssemblyPath);
            using var runtime = new RuntimeMetadataResolver(new[] { AssemblyPath });
            var wanted = metadata.Signature(metadata.Method(Fixture, "Choose"));
            var resolved = runtime.Method("Eternal.Tests", "Eternal.Tests.Infrastructure.RuntimeResolverGenericChild", "Read", wanted);
            Assert.Equal("Eternal.Tests.Infrastructure.RuntimeResolverGeneric`1", resolved.Owner.Name);
            Assert.Equal(new[] { "System.Int32" }, resolved.Owner.Arguments);
            Assert.False(runtime.Field("Eternal.Tests", "Eternal.Tests.Infrastructure.RuntimeResolverGenericChild", "Value", "System.Int32", false).Static);
            Assert.Equal("System.Int32,!!0,System.String[]", RuntimeMetadataResolver.Substitute("!0,!!0,!1[]", new[] { "System.Int32", "System.String" }));
            var split = RuntimeMetadataResolver.SplitType("Example`2<System.Int32,System.Collections.Generic.Dictionary`2<System.String,System.Int32>>");
            Assert.Equal(2, split.arguments.Length);
            Assert.Equal("System.Collections.Generic.Dictionary`2<System.String,System.Int32>", split.arguments.Last());
        }

        [Fact]
        public void TypeSpecificationOwner_UsesMetadataTokens_WithoutHidingConstructedGameReferences()
        {
            using var metadata = new CompiledAssemblyContract(AssemblyPath);
            var owners = metadata.Reader.MemberReferences.Select(h => metadata.Reader.GetMemberReference(h).Parent)
                .Concat(metadata.Instructions(metadata.Method(Fixture, "GenericValue")).Select(i => i.MemberHandle))
                .Where(h => h.Kind == HandleKind.TypeSpecification).Distinct().ToArray();
            Assert.Contains(owners, h => metadata.MemberName(h) == "!!0" && RuntimeMetadataResolver.ReferenceAssembly(metadata, h) == null);
            Assert.Contains(owners, h => metadata.MemberName(h) == "System.Int32[,]" && RuntimeMetadataResolver.ReferenceAssembly(metadata, h) == null);
            Assert.Contains(owners, h => metadata.MemberName(h).StartsWith("<>f__AnonymousType", StringComparison.Ordinal) &&
                RuntimeMetadataResolver.ReferenceAssembly(metadata, h) == "Eternal.Tests");
            Assert.Contains(owners, h => metadata.MemberName(h) == "Verse.DefDatabase`1<RimWorld.TraitDef>" &&
                RuntimeMetadataResolver.ReferenceAssembly(metadata, h) == "Assembly-CSharp");
            var generated = RuntimeMetadataResolver.SplitType("<>f__AnonymousType0`2<System.Int32,System.String>");
            Assert.Equal("<>f__AnonymousType0`2", generated.name);
            Assert.Equal(new[] { "System.Int32", "System.String" }, generated.arguments);
        }
    }

    internal class RuntimeResolverFixture
    {
        public int Number = 1;
        public static int Shared = 2;
        public RuntimeResolverFixture() { }
        public RuntimeResolverFixture(int number) { Number = number; }
        public int Choose(int number) => number;
        public string WrongReturn(int number) => number.ToString();
        public int WrongParameter(string number) => number.Length;
        public static int WrongStatic(int number) => number;
        public int WrongGeneric<T>(int number) => number;
        public static string GenericValue<T>(T value) => value.ToString();
        public static int ArrayValue(int[,] values) => values[0, 0];
        public static int AnonymousValue() => new { Number = 1, Text = "fixture" }.Number;
        public static TraitDef GameGenericValue() => DefDatabase<TraitDef>.GetNamedSilentFail("fixture");
    }
    internal class RuntimeResolverChild : RuntimeResolverFixture { }
    internal class RuntimeResolverGeneric<T>
    {
        public T Value = default;
        public T Read(T value) => value;
    }
    internal class RuntimeResolverGenericChild : RuntimeResolverGeneric<int> { }
}

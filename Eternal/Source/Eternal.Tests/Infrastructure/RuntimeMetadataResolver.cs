// Author: 0Shard
// Description: Resolve external PE signatures, generic substitutions and inherited members without loading game code.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;

namespace Eternal.Tests.Infrastructure
{
    internal sealed class RuntimeMetadataResolver : IDisposable
    {
        private readonly Dictionary<string, CompiledAssemblyContract> assemblies;
        private readonly string unityRedirect;

        public RuntimeMetadataResolver(IEnumerable<string> paths, string unityRedirect = null)
        {
            assemblies = new Dictionary<string, CompiledAssemblyContract>(StringComparer.Ordinal);
            this.unityRedirect = unityRedirect;
            try
            {
                foreach (string path in paths.Distinct())
                {
                    var assembly = new CompiledAssemblyContract(path);
                    string name = assembly.Reader.GetString(assembly.Reader.GetAssemblyDefinition().Name);
                    if (assemblies.ContainsKey(name))
                    {
                        assembly.Dispose();
                        throw new InvalidOperationException("Duplicate runtime assembly name: " + name);
                    }

                    assemblies.Add(name, assembly);
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal sealed class ResolvedType
        {
            public CompiledAssemblyContract Assembly;
            public TypeDefinitionHandle Handle;
            public string[] Arguments;
            public string Name => Assembly.MemberName(Handle);
        }

        internal sealed class ResolvedMember
        {
            public ResolvedType Owner;
            public EntityHandle Handle;
            public string Signature;
            public bool Static;
        }

        public static string ReferenceAssembly(CompiledAssemblyContract source, EntityHandle handle)
        {
            if (handle.Kind == HandleKind.TypeReference)
            {
                var reference = source.Reader.GetTypeReference((TypeReferenceHandle)handle);
                if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
                {
                    return ReferenceAssembly(source, reference.ResolutionScope);
                }

                return reference.ResolutionScope.Kind == HandleKind.AssemblyReference ?
                    source.Reader.GetString(source.Reader.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name) : null;
            }

            if (handle.Kind == HandleKind.TypeSpecification)
            {
                // Decode the owner token rather than reverse-matching display names.
                // Arrays and generic variables have CLR-synthesized members, while
                // a constructed game type retains its external definition token.
                var owner = source.Reader.GetTypeSpecification((TypeSpecificationHandle)handle)
                    .DecodeSignature(SignatureOwnerProvider.Instance, (object)null);
                return owner.IsNil ? null : ReferenceAssembly(source, owner);
            }

            if (handle.Kind == HandleKind.TypeDefinition)
            {
                return source.Reader.GetString(source.Reader.GetAssemblyDefinition().Name);
            }

            return null;
        }

        private sealed class SignatureOwnerProvider : ISignatureTypeProvider<EntityHandle, object>
        {
            public static readonly SignatureOwnerProvider Instance = new SignatureOwnerProvider();
            public EntityHandle GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => handle;
            public EntityHandle GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => handle;
            public EntityHandle GetTypeFromSpecification(MetadataReader reader, object context, TypeSpecificationHandle handle, byte rawTypeKind)
                => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
            public EntityHandle GetGenericInstantiation(EntityHandle genericType, ImmutableArray<EntityHandle> arguments) => genericType;
            public EntityHandle GetModifiedType(EntityHandle modifier, EntityHandle elementType, bool isRequired) => elementType;
            public EntityHandle GetPinnedType(EntityHandle elementType) => elementType;
            public EntityHandle GetGenericMethodParameter(object context, int index) => default;
            public EntityHandle GetGenericTypeParameter(object context, int index) => default;
            public EntityHandle GetPrimitiveType(PrimitiveTypeCode code) => default;
            public EntityHandle GetArrayType(EntityHandle elementType, ArrayShape shape) => default;
            public EntityHandle GetSZArrayType(EntityHandle elementType) => default;
            public EntityHandle GetByReferenceType(EntityHandle elementType) => default;
            public EntityHandle GetPointerType(EntityHandle elementType) => default;
            public EntityHandle GetFunctionPointerType(MethodSignature<EntityHandle> signature) => default;
        }

        public ResolvedType Type(string scope, string fullName)
        {
            var split = SplitType(fullName);
            string redirected = scope != null && scope.StartsWith("UnityEngine", StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(unityRedirect) ? unityRedirect : scope;
            return Type(redirected, split.name, split.arguments, new HashSet<string>(StringComparer.Ordinal));
        }

        private ResolvedType Type(string scope, string name, string[] arguments, HashSet<string> visited)
        {
            if (scope == null || !visited.Add(scope + ":" + name) || !assemblies.TryGetValue(scope, out var assembly))
            {
                throw new InvalidOperationException("Missing/cyclic runtime type " + scope + ":" + name);
            }

            if (assembly.TryType(name, out var handle))
            {
                return new ResolvedType { Assembly = assembly, Handle = handle, Arguments = arguments };
            }

            foreach (var exportedHandle in assembly.Reader.ExportedTypes)
            {
                var exported = assembly.Reader.GetExportedType(exportedHandle);
                string exportedName = ExportedName(assembly, exportedHandle);
                var implementation = exported.Implementation;
                while (implementation.Kind == HandleKind.ExportedType)
                {
                    implementation = assembly.Reader.GetExportedType((ExportedTypeHandle)implementation).Implementation;
                }

                if (exportedName == name && implementation.Kind == HandleKind.AssemblyReference)
                {
                    string forwarded = assembly.Reader.GetString(assembly.Reader.GetAssemblyReference((AssemblyReferenceHandle)implementation).Name);
                    return Type(forwarded, name, arguments, visited);
                }
            }

            throw new InvalidOperationException("Missing runtime type " + scope + ":" + name);
        }

        private static string ExportedName(CompiledAssemblyContract assembly, ExportedTypeHandle handle)
        {
            var type = assembly.Reader.GetExportedType(handle);
            if (type.Implementation.Kind == HandleKind.ExportedType)
            {
                return ExportedName(assembly, (ExportedTypeHandle)type.Implementation) + "+" + assembly.Reader.GetString(type.Name);
            }

            string ns = assembly.Reader.GetString(type.Namespace);
            return (ns.Length == 0 ? "" : ns + ".") + assembly.Reader.GetString(type.Name);
        }

        private IEnumerable<ResolvedType> Ancestry(ResolvedType type, bool inherited)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (type != null)
            {
                string key = type.Assembly.Path + ":" + type.Name + "<" + string.Join(",", type.Arguments) + ">";
                if (!visited.Add(key) || visited.Count > 64)
                {
                    throw new BadImageFormatException("Cyclic runtime inheritance: " + key);
                }

                yield return type;
                var parent = type.Assembly.Reader.GetTypeDefinition(type.Handle).BaseType;
                if (!inherited || parent.IsNil)
                {
                    yield break;
                }

                string scope = ReferenceAssembly(type.Assembly, parent);
                // Framework ancestry is not needed when all external game/Harmony
                // candidates have already been exhausted. Loaded forwarding roots
                // are followed when configured (including 1.7 mscorlib -> CoreLib).
                if (scope == null || !assemblies.ContainsKey(scope))
                {
                    yield break;
                }

                type = Type(scope, Substitute(type.Assembly.MemberName(parent), type.Arguments));
            }
        }

        public ResolvedMember Method(string scope, string owner, string name, MethodSignature<string> wanted)
        {
            var root = Type(scope, owner);
            string expected = MethodSignature(wanted, root.Arguments);
            foreach (var type in Ancestry(root, name != ".ctor" && name != ".cctor"))
            {
                var matches = type.Assembly.Reader.GetTypeDefinition(type.Handle).GetMethods()
                    .Where(h => type.Assembly.Reader.GetString(type.Assembly.Reader.GetMethodDefinition(h).Name) == name)
                    .Where(h => MethodSignature(type.Assembly.Signature(h), type.Arguments) == expected).ToArray();
                if (matches.Length > 1)
                {
                    throw new InvalidOperationException("Ambiguous runtime method " + owner + "." + name + " " + expected);
                }

                if (matches.Length == 1)
                {
                    return new ResolvedMember { Owner = type, Handle = matches[0], Signature = expected, Static = !wanted.Header.IsInstance };
                }
            }

            throw new InvalidOperationException("Missing runtime method " + scope + ":" + owner + "." + name + " " + expected);
        }

        public ResolvedMember Field(string scope, string owner, string name, string wanted, bool? isStatic)
        {
            var root = Type(scope, owner);
            wanted = Substitute(wanted, root.Arguments);
            foreach (var type in Ancestry(root, true))
            {
                var matches = type.Assembly.Reader.GetTypeDefinition(type.Handle).GetFields()
                    .Where(h => type.Assembly.Reader.GetString(type.Assembly.Reader.GetFieldDefinition(h).Name) == name).ToArray();
                if (matches.Length == 0)
                {
                    continue;
                }

                if (matches.Length != 1)
                {
                    throw new InvalidOperationException("Ambiguous runtime field " + owner + "." + name);
                }

                var field = type.Assembly.Reader.GetFieldDefinition(matches[0]);
                string actual = Substitute(field.DecodeSignature(type.Assembly.Names, (object)null), type.Arguments);
                bool actualStatic = (field.Attributes & FieldAttributes.Static) != 0;
                if (actual != wanted || (isStatic.HasValue && isStatic.Value != actualStatic))
                {
                    throw new InvalidOperationException("Incompatible runtime field " + owner + "." + name + ": " + actual + ", static=" + actualStatic);
                }

                return new ResolvedMember { Owner = type, Handle = matches[0], Signature = actual, Static = actualStatic };
            }

            throw new InvalidOperationException("Missing runtime field " + scope + ":" + owner + "." + name);
        }

        internal static string MethodSignature(MethodSignature<string> signature, string[] arguments)
            => (signature.Header.IsInstance ? "instance" : "static") + " generic=" + signature.GenericParameterCount +
                " callingConvention=" + signature.Header.CallingConvention + " requiredParameters=" + signature.RequiredParameterCount +
                " (" + string.Join(",", signature.ParameterTypes.Select(p => Substitute(p, arguments))) + ") -> " + Substitute(signature.ReturnType, arguments);

        internal static string Substitute(string signature, string[] arguments)
            => Regex.Replace(signature, @"(?<!!)!(\d+)", match =>
            {
                int index = int.Parse(match.Groups[1].Value);
                return index < arguments.Length ? arguments[index] : match.Value;
            });

        internal static (string name, string[] arguments) SplitType(string name)
        {
            var generic = Regex.Match(name, @"`\d+<");
            if (!generic.Success)
            {
                return (name, Array.Empty<string>());
            }

            int start = generic.Index + generic.Length - 1;

            if (!name.EndsWith(">", StringComparison.Ordinal))
            {
                throw new BadImageFormatException("Malformed generic owner " + name);
            }

            var arguments = new List<string>();
            int depth = 0, argumentStart = start + 1;
            for (int i = argumentStart; i < name.Length - 1; i++)
            {
                if (name[i] == '<' || name[i] == '[' || name[i] == '(')
                {
                    depth++;
                }
                else if (name[i] == '>' || name[i] == ']' || name[i] == ')')
                {
                    depth--;
                }
                else if (name[i] == ',' && depth == 0)
                {
                    arguments.Add(name.Substring(argumentStart, i - argumentStart));
                    argumentStart = i + 1;
                }
            }

            arguments.Add(name.Substring(argumentStart, name.Length - 1 - argumentStart));
            return (name.Substring(0, start), arguments.ToArray());
        }

        public void Dispose()
        {
            foreach (var assembly in assemblies.Values)
            {
                assembly.Dispose();
            }
        }
    }
}

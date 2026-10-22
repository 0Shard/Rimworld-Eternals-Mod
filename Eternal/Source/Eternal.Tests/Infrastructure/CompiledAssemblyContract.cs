// Author: 0Shard
// Description: Owned PE metadata, exact signatures and decoded instructions without game loading.
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Eternal.Tests.Infrastructure
{
    internal sealed class CompiledInstruction
    {
        public int Offset;
        public OpCode Code;
        public object Operand;
        public EntityHandle MemberHandle;
        public MethodSignature<string>? CallSignature;
    }

    internal sealed class CompiledAssemblyContract : IDisposable
    {
        private readonly Stream stream;
        private readonly PEReader pe;
        private readonly MetadataReader reader;
        private bool disposed;
        private readonly Dictionary<string, TypeDefinitionHandle> types;
        private static readonly Dictionary<short, OpCode> Codes = typeof(OpCodes).GetFields()
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)).ToDictionary(c => c.Value);
        public string Path { get; }
        public MetadataReader Reader { get { EnsureOpen(); return reader; } }
        public MetadataTypeNames Names { get; } = new MetadataTypeNames();

        public CompiledAssemblyContract(string path) : this(File.OpenRead(path), path) { }
        internal CompiledAssemblyContract(Stream source, string context)
        {
            Path = context;
            stream = source ?? throw new ArgumentNullException(nameof(source));
            try
            {
                pe = new PEReader(stream);
                if (!pe.HasMetadata)
                {
                    throw new BadImageFormatException("PE has no managed metadata");
                }

                reader = pe.GetMetadataReader();
                types = reader.TypeDefinitions.ToDictionary(h => MemberName(h), h => h, StringComparer.Ordinal);
            }
            catch (Exception error)
            {
                pe?.Dispose();
                stream.Dispose();
                throw new BadImageFormatException($"Cannot inspect {context}: {error.Message}", error);
            }
        }

        private void EnsureOpen()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(CompiledAssemblyContract), Path);
            }
        }

        public bool TryType(string name, out TypeDefinitionHandle type)
        {
            EnsureOpen();
            return types.TryGetValue(name, out type);
        }

        public TypeDefinitionHandle Type(string name)
        {
            if (!TryType(name, out var type))
            {
                throw new InvalidOperationException($"{Path}: missing type {name}");
            }

            return type;
        }

        public IEnumerable<MethodDefinitionHandle> Methods(string type, string method, bool inherited = false)
        {
            var current = Type(type);
            var visited = new HashSet<TypeDefinitionHandle>();
            var signatures = new HashSet<string>();
            while (!current.IsNil)
            {
                if (!visited.Add(current))
                {
                    throw new BadImageFormatException($"{Path}: cyclic ancestry of {type}");
                }

                var definition = Reader.GetTypeDefinition(current);
                var declaredSignatures = new HashSet<string>();
                foreach (var handle in definition.GetMethods())
                {
                    if (Reader.GetString(Reader.GetMethodDefinition(handle).Name) != method)
                    {
                        continue;
                    }

                    var signature = Signature(handle);
                    string key = signature.Header.IsInstance + ":" + signature.GenericParameterCount + ":" + string.Join(",", signature.ParameterTypes);
                    // Harmony sees the closest override; constructors are never inherited.
                    if (!signatures.Contains(key))
                    {
                        yield return handle;
                    }

                    declaredSignatures.Add(key);
                }
                signatures.UnionWith(declaredSignatures);
                if (!inherited || method == ".ctor" || method == ".cctor" || definition.BaseType.IsNil)
                {
                    yield break;
                }

                var baseName = MemberName(definition.BaseType);
                if (!TryType(baseName, out current))
                {
                    yield break;
                }
            }
        }

        public MethodSignature<string> Signature(MethodDefinitionHandle method)
        {
            try { return Reader.GetMethodDefinition(method).DecodeSignature(Names, (object)null); }
            catch (Exception error) when (error is BadImageFormatException || error is NotSupportedException)
            { throw new BadImageFormatException($"{Path}: malformed signature {MemberName(method)}: {error.Message}", error); }
        }

        public MethodDefinitionHandle Method(string type, string name, string[] parameters = null,
            string returns = null, int? genericArity = null, bool? isStatic = null, bool inherited = false)
        {
            var candidates = Methods(type, name, inherited).Where(h =>
            {
                var signature = Signature(h);
                return (parameters == null || signature.ParameterTypes.SequenceEqual(parameters)) &&
                    (returns == null || signature.ReturnType == returns) &&
                    (!genericArity.HasValue || signature.GenericParameterCount == genericArity.Value) &&
                    (!isStatic.HasValue || !signature.Header.IsInstance == isStatic.Value);
            }).ToList();
            string wanted = $"{type}.{name}({(parameters == null ? "unspecified" : string.Join(",", parameters))})";
            if (candidates.Count != 1)
            {
                throw new InvalidOperationException($"{Path}: {(candidates.Count == 0 ? "missing" : "ambiguous")} method {wanted}; " +
                    $"return={returns ?? "unspecified"}, generic={genericArity}, static={isStatic}; " +
                    "candidates: " + string.Join("; ", Methods(type, name, inherited).Select(Describe)));
            }

            return candidates.Single();
        }

        public string Describe(MethodDefinitionHandle method)
        {
            var signature = Signature(method);
            return $"{MemberName(method)}<{signature.GenericParameterCount}>({string.Join(",", signature.ParameterTypes)}) -> {signature.ReturnType}";
        }

        public string[] ParameterNames(MethodDefinitionHandle method)
        {
            var names = new string[Signature(method).ParameterTypes.Length];
            foreach (var handle in Reader.GetMethodDefinition(method).GetParameters())
            {
                var parameter = Reader.GetParameter(handle);
                if (parameter.SequenceNumber == 0)
                {
                    continue;
                }

                if (parameter.SequenceNumber > names.Length)
                {
                    throw new BadImageFormatException($"{Path}: invalid parameter sequence in {Describe(method)}");
                }

                names[parameter.SequenceNumber - 1] = Reader.GetString(parameter.Name);
            }
            return names;
        }

        public string MemberName(EntityHandle handle)
        {
            EnsureOpen();
            if (handle.IsNil)
            {
                return null;
            }

            switch (handle.Kind)
            {
                case HandleKind.MethodSpecification:
                    var specification = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                    // Validate generic operands while retaining stable member names for existing flow contracts.
                    specification.DecodeSignature(Names, (object)null);
                    return MemberName(specification.Method);
                case HandleKind.MethodDefinition:
                    var method = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                    return MemberName(method.GetDeclaringType()) + "." + reader.GetString(method.Name);
                case HandleKind.MemberReference:
                    var member = reader.GetMemberReference((MemberReferenceHandle)handle);
                    return MemberName(member.Parent) + "." + reader.GetString(member.Name);
                case HandleKind.TypeDefinition: return Names.GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0);
                case HandleKind.TypeReference: return Names.GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0);
                case HandleKind.TypeSpecification: return reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(Names, (object)null);
                case HandleKind.FieldDefinition: return reader.GetString(reader.GetFieldDefinition((FieldDefinitionHandle)handle).Name);
                default: throw new NotSupportedException($"{Path}: unsupported member token {handle.Kind}");
            }
        }

        public string AttributeText(CustomAttributeHandleCollection attributes)
            => string.Join(" ", attributes.Select(h => Reader.GetCustomAttribute(h))
                .Select(a => MemberName(a.Constructor) + " " + System.Text.Encoding.UTF8.GetString(Reader.GetBlobBytes(a.Value))));

        public CustomAttributeValue<string> DecodeAttribute(CustomAttributeHandle handle, string context)
        {
            try { return Reader.GetCustomAttribute(handle).DecodeValue(Names); }
            catch (Exception error) when (error is BadImageFormatException || error is NotSupportedException)
            { throw new BadImageFormatException($"{Path}: cannot decode attribute on {context}: {error.Message}", error); }
        }

        public MethodBodyBlock Body(MethodDefinitionHandle handle)
        {
            int address = Reader.GetMethodDefinition(handle).RelativeVirtualAddress;
            if (address == 0)
            {
                throw new InvalidOperationException($"{Path}: no IL body for {Describe(handle)}");
            }

            return pe.GetMethodBody(address);
        }

        public List<CompiledInstruction> Instructions(MethodDefinitionHandle handle)
            => Decode(Body(handle).GetILBytes(), Describe(handle));

        internal List<CompiledInstruction> Decode(byte[] bytes, string context)
        {
            EnsureOpen();
            var result = new List<CompiledInstruction>();
            int index = 0;
            void Require(int length)
            {
                if (length < 0 || length > bytes.Length - index)
                {
                    throw new BadImageFormatException($"{Path}: truncated IL in {context} at {index}");
                }
            }
            while (index < bytes.Length)
            {
                int offset = index;
                short value = bytes[index++];
                if (value == 0xfe) { Require(1); value = (short)(0xfe00 | bytes[index++]); }
                if (!Codes.TryGetValue(value, out var code))
                {
                    throw new BadImageFormatException($"{Path}: unknown opcode {value:X} in {context} at {offset}");
                }

                object operand = null;
                EntityHandle member = default;
                MethodSignature<string>? call = null;
                int length;
                switch (code.OperandType)
                {
                    case OperandType.InlineNone: length = 0; break;
                    case OperandType.ShortInlineI: Require(length = 1); operand = (int)(sbyte)bytes[index]; break;
                    case OperandType.ShortInlineVar: Require(length = 1); operand = (int)bytes[index]; break;
                    case OperandType.InlineVar: Require(length = 2); operand = (int)BitConverter.ToUInt16(bytes, index); break;
                    case OperandType.ShortInlineBrTarget: Require(length = 1); operand = index + length + (sbyte)bytes[index]; break;
                    case OperandType.InlineBrTarget: Require(length = 4); operand = index + length + BitConverter.ToInt32(bytes, index); break;
                    case OperandType.InlineI: Require(length = 4); operand = BitConverter.ToInt32(bytes, index); break;
                    case OperandType.InlineI8: Require(length = 8); operand = BitConverter.ToInt64(bytes, index); break;
                    case OperandType.InlineR: Require(length = 8); operand = BitConverter.ToDouble(bytes, index); break;
                    case OperandType.ShortInlineR: Require(length = 4); operand = BitConverter.ToSingle(bytes, index); break;
                    case OperandType.InlineSwitch:
                        Require(4);
                        int count = BitConverter.ToInt32(bytes, index);
                        if (count < 0 || count > (bytes.Length - index - 4) / 4)
                        {
                            throw new BadImageFormatException($"{Path}: malformed switch in {context} at {offset}");
                        }

                        Require(length = 4 + 4 * count);
                        operand = Enumerable.Range(0, count).Select(i => index + length + BitConverter.ToInt32(bytes, index + 4 + 4 * i)).ToArray();
                        break;
                    case OperandType.InlineString:
                        Require(length = 4); operand = Reader.GetUserString(MetadataTokens.UserStringHandle(BitConverter.ToInt32(bytes, index) & 0xffffff)); break;
                    case OperandType.InlineMethod:
                    case OperandType.InlineField:
                    case OperandType.InlineType:
                    case OperandType.InlineTok:
                        Require(length = 4); member = MetadataTokens.EntityHandle(BitConverter.ToInt32(bytes, index));
                        operand = MemberName(member);
                        if (code.OperandType == OperandType.InlineMethod)
                        {
                            call = MemberSignature(member);
                        }

                        break;
                    case OperandType.InlineSig:
                        Require(length = 4);
                        var signature = MetadataTokens.StandaloneSignatureHandle(BitConverter.ToInt32(bytes, index) & 0xffffff);
                        call = Reader.GetStandaloneSignature(signature).DecodeMethodSignature(Names, (object)null);
                        operand = call.Value.ReturnType;
                        break;
                    default: throw new NotSupportedException($"{Path}: unsupported operand {code.OperandType} in {context} at {offset}");
                }
                result.Add(new CompiledInstruction { Offset = offset, Code = code, Operand = operand, MemberHandle = member, CallSignature = call });
                index += length;
            }
            var offsets = new HashSet<int>(result.Select(i => i.Offset));
            foreach (var instruction in result)
            {
                IEnumerable<int> targets = instruction.Code.OperandType == OperandType.InlineSwitch ? (int[])instruction.Operand :
                    instruction.Code.OperandType == OperandType.InlineBrTarget || instruction.Code.OperandType == OperandType.ShortInlineBrTarget ? new[] { (int)instruction.Operand } : Array.Empty<int>();
                if (targets.Any(target => !offsets.Contains(target)))
                {
                    throw new BadImageFormatException($"{Path}: invalid branch target in {context} at {instruction.Offset}");
                }
            }
            return result;
        }

        private MethodSignature<string> MemberSignature(EntityHandle member)
        {
            if (member.Kind == HandleKind.MethodSpecification)
            {
                return MemberSignature(Reader.GetMethodSpecification((MethodSpecificationHandle)member).Method);
            }

            if (member.Kind == HandleKind.MethodDefinition)
            {
                return Signature((MethodDefinitionHandle)member);
            }

            if (member.Kind == HandleKind.MemberReference)
            {
                return Reader.GetMemberReference((MemberReferenceHandle)member).DecodeMethodSignature(Names, (object)null);
            }

            throw new BadImageFormatException($"{Path}: non-method call token {member.Kind}");
        }

        public List<CompiledInstruction> ReachableInstructions(string type, string method, bool finallyOnly)
        {
            var root = Method(type, method);
            var owner = Reader.GetMethodDefinition(root).GetDeclaringType();
            while (!Reader.GetTypeDefinition(owner).GetDeclaringType().IsNil)
            {
                owner = Reader.GetTypeDefinition(owner).GetDeclaringType();
            }

            var visited = new HashSet<MethodDefinitionHandle>();
            var pending = new Stack<MethodDefinitionHandle>();
            var result = new List<CompiledInstruction>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!visited.Add(current))
                {
                    continue;
                }

                if (visited.Count > 256)
                {
                    throw new InvalidOperationException($"{Path}: local call graph bound exceeded for {type}.{method}");
                }

                var body = Body(current);
                var instructions = Instructions(current);
                result.AddRange(finallyOnly ? instructions.Where(i => body.ExceptionRegions.Any(r => r.Kind == ExceptionRegionKind.Finally &&
                    i.Offset >= r.HandlerOffset && i.Offset < r.HandlerOffset + r.HandlerLength)) : instructions);
                foreach (var instruction in instructions)
                {
                    if (instruction.MemberHandle.Kind != HandleKind.MethodDefinition ||
                        !(instruction.Code == OpCodes.Call || instruction.Code == OpCodes.Callvirt || instruction.Code == OpCodes.Ldftn || instruction.Code == OpCodes.Newobj))
                    {
                        continue;
                    }

                    var target = (MethodDefinitionHandle)instruction.MemberHandle;
                    var targetOwner = Reader.GetMethodDefinition(target).GetDeclaringType();
                    while (!Reader.GetTypeDefinition(targetOwner).GetDeclaringType().IsNil)
                    {
                        targetOwner = Reader.GetTypeDefinition(targetOwner).GetDeclaringType();
                    }

                    if (targetOwner == owner)
                    {
                        pending.Push(target);
                    }
                }
            }
            return result;
        }

        public static int Local(CompiledInstruction instruction)
        {
            if (instruction.Code == OpCodes.Stloc_0 || instruction.Code == OpCodes.Ldloc_0)
            {
                return 0;
            }

            if (instruction.Code == OpCodes.Stloc_1 || instruction.Code == OpCodes.Ldloc_1)
            {
                return 1;
            }

            if (instruction.Code == OpCodes.Stloc_2 || instruction.Code == OpCodes.Ldloc_2)
            {
                return 2;
            }

            if (instruction.Code == OpCodes.Stloc_3 || instruction.Code == OpCodes.Ldloc_3)
            {
                return 3;
            }

            return instruction.Code.OperandType == OperandType.InlineVar || instruction.Code.OperandType == OperandType.ShortInlineVar
                ? instruction.Code.Name.Contains("loc") ? (int)instruction.Operand : -1 : -1;
        }

        public bool ReturnsConstantInt32(MethodDefinitionHandle method, int expected)
        {
            var il = Instructions(method);
            var positions = il.Select((instruction, index) => (instruction.Offset, index)).ToDictionary(p => p.Offset, p => p.index);
            var pending = new Stack<(int index, List<int?> stack, Dictionary<int, int?> locals)>();
            pending.Push((0, new List<int?>(), new Dictionary<int, int?>()));
            var visited = new HashSet<string>();
            var returns = new HashSet<int>();
            while (pending.Count > 0)
            {
                var state = pending.Pop();
                if (state.index < 0 || state.index >= il.Count)
                {
                    throw new BadImageFormatException("Constant-return path falls outside " + Describe(method));
                }

                string identity = state.index + ":" + string.Join(",", state.stack.Select(v => v?.ToString() ?? "?")) + ":" +
                    string.Join(",", state.locals.OrderBy(p => p.Key).Select(p => p.Key + "=" + (p.Value?.ToString() ?? "?")));
                if (!visited.Add(identity))
                {
                    continue;
                }

                if (visited.Count > 4096)
                {
                    throw new InvalidOperationException("Constant-return proof bound exceeded: " + Describe(method));
                }

                var instruction = il[state.index];
                int? Pop()
                {
                    if (state.stack.Count == 0)
                    {
                        throw new BadImageFormatException("Constant-return stack underflow: " + Describe(method));
                    }

                    int? value = state.stack[state.stack.Count - 1];
                    state.stack.RemoveAt(state.stack.Count - 1);
                    return value;
                }
                void Push(int index) => pending.Push((index, new List<int?>(state.stack), new Dictionary<int, int?>(state.locals)));
                if (instruction.Code == OpCodes.Ret)
                {
                    if (Pop() != expected || state.stack.Count != 0)
                    {
                        return false;
                    }

                    returns.Add(instruction.Offset);
                    continue;
                }
                if (instruction.Code == OpCodes.Ldc_I4_0)
                {
                    state.stack.Add(0);
                }
                else if (instruction.Code == OpCodes.Ldc_I4_1)
                {
                    state.stack.Add(1);
                }
                else if (instruction.Code == OpCodes.Ldc_I4 || instruction.Code == OpCodes.Ldc_I4_S)
                {
                    state.stack.Add((int)instruction.Operand);
                }
                else if (instruction.Code.Name.StartsWith("ldarg", StringComparison.Ordinal))
                {
                    state.stack.Add(null);
                }
                else if (instruction.Code.Name.StartsWith("stloc", StringComparison.Ordinal))
                {
                    state.locals[Local(instruction)] = Pop();
                }
                else if (instruction.Code.Name.StartsWith("ldloc", StringComparison.Ordinal))
                {
                    state.stack.Add(state.locals.TryGetValue(Local(instruction), out var value) ? value : throw new BadImageFormatException("Unknown constant-return local"));
                }
                else if (instruction.Code == OpCodes.Br || instruction.Code == OpCodes.Br_S) { Push(positions[(int)instruction.Operand]); continue; }
                else if (instruction.Code == OpCodes.Brtrue || instruction.Code == OpCodes.Brtrue_S || instruction.Code == OpCodes.Brfalse || instruction.Code == OpCodes.Brfalse_S)
                {
                    int? condition = Pop();
                    bool branchOnTrue = instruction.Code == OpCodes.Brtrue || instruction.Code == OpCodes.Brtrue_S;
                    if (!condition.HasValue || (condition.Value != 0) == branchOnTrue)
                    {
                        Push(positions[(int)instruction.Operand]);
                    }

                    if (condition.HasValue && (condition.Value != 0) == branchOnTrue)
                    {
                        continue;
                    }
                }
                else if (instruction.Code != OpCodes.Nop)
                {
                    throw new NotSupportedException($"Unsupported constant-return instruction {instruction.Code} in {Describe(method)}");
                }

                Push(state.index + 1);
            }
            return returns.Count > 0 && returns.Count == il.Count(i => i.Code == OpCodes.Ret);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            pe.Dispose();
            stream.Dispose();
        }
    }

    internal sealed class MetadataTypeNames : ISignatureTypeProvider<string, object>, ICustomAttributeTypeProvider<string>
    {
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            string name = reader.GetString(type.Name);
            if (type.IsNested)
            {
                return GetTypeFromDefinition(reader, type.GetDeclaringType(), rawTypeKind) + "+" + name;
            }

            string ns = reader.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            string name = reader.GetString(type.Name);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                return GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind) + "+" + name;
            }

            string ns = reader.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }
        public string GetTypeFromSpecification(MetadataReader reader, object context, TypeSpecificationHandle handle, byte rawTypeKind)
            => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
        public string GetPrimitiveType(PrimitiveTypeCode code) => "System." + code;
        public string GetArrayType(string element, ArrayShape shape) => element + "[" + new string(',', shape.Rank - 1) + "]";
        public string GetSZArrayType(string element) => element + "[]";
        public string GetByReferenceType(string element) => element + "&";
        public string GetPointerType(string element) => element + "*";
        public string GetGenericInstantiation(string generic, ImmutableArray<string> arguments) => generic + "<" + string.Join(",", arguments) + ">";
        public string GetGenericMethodParameter(object context, int index) => "!!" + index;
        public string GetGenericTypeParameter(object context, int index) => "!" + index;
        public string GetModifiedType(string modifier, string element, bool required) => element + (required ? " modreq(" : " modopt(") + modifier + ")";
        public string GetPinnedType(string element) => element + " pinned";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "method(" + string.Join(",", signature.ParameterTypes) + ")->" + signature.ReturnType;
        public string GetSystemType() => "System.Type";
        public bool IsSystemType(string type) => type == "System.Type";
        public string GetTypeFromSerializedName(string name) => SerializedTypeName(name);
        internal static string SerializedTypeName(string name)
        {
            int depth = 0;
            for (int i = 0; i < name.Length; i++)
            {
                if (name[i] == '[')
                {
                    depth++;
                }

                if (name[i] == ']')
                {
                    depth--;
                }

                if (depth < 0)
                {
                    throw new BadImageFormatException("Malformed serialized type " + name);
                }

                if (name[i] == ',' && depth == 0) { name = name.Substring(0, i).Trim(); break; }
            }
            int generic = name.IndexOf("[[", StringComparison.Ordinal);
            if (depth != 0)
            {
                throw new BadImageFormatException("Malformed serialized type " + name);
            }

            if (generic < 0)
            {
                return name.Trim();
            }

            var arguments = new List<string>();
            int position = generic + 1;
            while (position < name.Length && name[position] == '[')
            {
                int start = ++position;
                depth = 1;
                while (position < name.Length && depth > 0)
                {
                    if (name[position] == '[')
                    {
                        depth++;
                    }

                    if (name[position] == ']')
                    {
                        depth--;
                    }

                    position++;
                }
                if (depth != 0)
                {
                    throw new BadImageFormatException("Malformed generic serialized type " + name);
                }

                arguments.Add(SerializedTypeName(name.Substring(start, position - start - 1)));
                if (position < name.Length && name[position] == ',')
                {
                    position++;
                }
            }
            if (position >= name.Length || name[position] != ']')
            {
                throw new BadImageFormatException("Malformed generic serialized type " + name);
            }

            return name.Substring(0, generic) + "<" + string.Join(",", arguments) + ">" + name.Substring(position + 1);
        }
        public PrimitiveTypeCode GetUnderlyingEnumType(string type)
        {
            // Harmony's declared attribute enums use Int32. Other enums need an explicit schema.
            if (type.StartsWith("HarmonyLib.", StringComparison.Ordinal))
            {
                return PrimitiveTypeCode.Int32;
            }

            throw new NotSupportedException("Unknown custom attribute enum " + type);
        }
    }
}

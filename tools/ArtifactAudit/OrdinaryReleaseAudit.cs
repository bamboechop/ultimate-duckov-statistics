using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace ArtifactAudit;

public static class OrdinaryReleaseAudit
{
    private static readonly Dictionary<ushort, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => unchecked((ushort)code.Value));

    public static void Verify(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata)
        {
            if (PinnedDependencies.IsPinnedNative(assemblyPath, File.ReadAllBytes(assemblyPath))) return;
            throw new BadImageFormatException("Unexpected native dependency in an ordinary package.");
        }
        var metadata = pe.GetMetadataReader();
        var nativeMod = metadata.IsAssembly && metadata.GetString(metadata.GetAssemblyDefinition().Name) == "UltimateDuckovStatistics";
        var encounterTypes = new HashSet<string>(StringComparer.Ordinal);
        var encounterCalls = new HashSet<string>(StringComparer.Ordinal);
        _ = PinnedDependencies.IsPinnedManaged(assemblyPath, File.ReadAllBytes(assemblyPath));
        foreach (var typeHandle in metadata.TypeDefinitions)
        {
            var definition = metadata.GetTypeDefinition(typeHandle);
            var typeName = metadata.GetString(definition.Name);
            if (metadata.GetString(definition.Namespace) == "UltimateDuckovStatistics.Encounters") encounterTypes.Add(typeName);
            if (typeName is "NativeUiResourceDiagnostics" or "CorePersistenceTimings")
                throw new InvalidDataException("Ordinary Release contains the opt-in native resource diagnostic type.");
        }
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0 || IsDiagnosticType(metadata, method.GetDeclaringType())) continue;
            var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
            for (var offset = 0; offset < il.Length;)
            {
                var value = (ushort)il[offset++];
                if (value == 0xfe) value = (ushort)(0xfe00 | il[offset++]);
                if (!Codes.TryGetValue(value, out var code)) throw new BadImageFormatException("Unknown IL opcode.");
                if (code.OperandType == OperandType.InlineMethod)
                {
                    var target = MetadataTokens.EntityHandle(BitConverter.ToInt32(il, offset));
                    if (target.Kind == HandleKind.MethodSpecification)
                        target = metadata.GetMethodSpecification((MethodSpecificationHandle)target).Method;
                    var owner = target.Kind == HandleKind.MethodDefinition
                        ? metadata.GetMethodDefinition((MethodDefinitionHandle)target).GetDeclaringType()
                        : target.Kind == HandleKind.MemberReference
                            ? metadata.GetMemberReference((MemberReferenceHandle)target).Parent : default;
                    if (IsDiagnosticType(metadata, owner))
                        throw new InvalidDataException($"Ordinary Release contains a performance-diagnostic call site: {metadata.GetString(method.Name)}.");
                    if (nativeMod && target.Kind == HandleKind.MethodDefinition)
                    {
                        var called = metadata.GetMethodDefinition((MethodDefinitionHandle)target);
                        var callerType = metadata.GetString(metadata.GetTypeDefinition(method.GetDeclaringType()).Name);
                        var calledType = metadata.GetString(metadata.GetTypeDefinition(called.GetDeclaringType()).Name);
                        encounterCalls.Add(callerType + "." + metadata.GetString(method.Name) + " -> " + calledType + "." + metadata.GetString(called.Name));
                    }
                }
                offset += code.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => checked(4 + 4 * BitConverter.ToInt32(il, offset)),
                    OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod
                        or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType
                        or OperandType.ShortInlineR => 4,
                    _ => throw new BadImageFormatException("Unsupported IL operand.")
                };
                if (offset > il.Length) throw new BadImageFormatException("Truncated IL operand.");
            }
        }
        foreach (var typeHandle in metadata.TypeDefinitions)
        {
            var ns = metadata.GetString(metadata.GetTypeDefinition(typeHandle).Namespace);
            if (ns.StartsWith("UltimateDuckovStatistics.EncounterPrototype", StringComparison.Ordinal)
                || ns.StartsWith("UltimateDuckovStatistics.Encounters.Diagnostics", StringComparison.Ordinal))
                throw new InvalidDataException("Ordinary Release contains an opt-in encounter diagnostic type.");
        }
        if (nativeMod)
        {
            foreach (var name in new[] { "EncounterCaptureHost", "EncounterCapturePipeline", "NativeEncounterCombatObserver",
                "NativeEncounterLootObserver", "NativeEncounterMapObserver", "StoredEncounterRun" })
                if (!encounterTypes.Contains(name)) throw new InvalidDataException("Ordinary Release is missing encounter runtime: " + name);
            foreach (var call in new[] { "ModBehaviour.OnAfterSetup -> EncounterCaptureHost..ctor",
                "ModBehaviour.Update -> EncounterCaptureHost.Tick", "RunsView.Tick -> RunsView.TickEncounters" })
                if (!encounterCalls.Contains(call)) throw new InvalidDataException("Ordinary Release is missing encounter integration: " + call);
        }
    }

    private static bool IsDiagnosticType(MetadataReader metadata, EntityHandle handle)
    {
        var name = handle.Kind switch
        {
            HandleKind.TypeDefinition => metadata.GetString(metadata.GetTypeDefinition((TypeDefinitionHandle)handle).Name),
            HandleKind.TypeReference => metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)handle).Name),
            _ => ""
        };
        return name is "NativeHotPathDiagnostics" or "NativeHotPathCounterSnapshot" or "NativeHotPathMeasurement" or "CorePersistenceTimings";
    }
}

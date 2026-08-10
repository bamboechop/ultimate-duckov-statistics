using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

const string expectedGameVersion = "2.3.30";
const string expectedSteamBuild = "24013657";
const string expectedUnityVersion = "2022.3.62f2";
var minimumHarmonyVersion = new Version(2, 4, 1, 0);
string[] healthAddParameters = ["System.Single"];
string[] buffAddParameters = ["Duckov.Buffs.Buff", "CharacterMainControl", "System.Int32"];
string[] effectTriggerParameters = ["ItemStatsSystem.EffectTriggerEventContext"];

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("Usage: DuckovContractProbe <Duckov game root>");
    return 2;
}

try
{
    var gameRoot = Path.GetFullPath(args[0]);
    var managedRoot = Path.Combine(gameRoot, "Duckov_Data", "Managed");
    var corePath = Path.Combine(managedRoot, "TeamSoda.Duckov.Core.dll");
    var itemStatsPath = Path.Combine(managedRoot, "ItemStatsSystem.dll");

    RequireFile(corePath);
    RequireFile(itemStatsPath);

    var gameVersion = ReadIniValue(Path.Combine(gameRoot, "Info.ini"), "version");
    AssertEqual("Duckov version", expectedGameVersion, gameVersion);

    var steamApps = Directory.GetParent(Directory.GetParent(gameRoot)?.FullName ?? string.Empty)?.FullName
        ?? throw new ContractException("Could not derive the Steam apps directory from the game path.");
    var manifestPath = Path.Combine(steamApps, "appmanifest_3167020.acf");
    RequireFile(manifestPath);
    var manifest = File.ReadAllText(manifestPath);
    var buildMatch = Regex.Match(manifest, "\\\"buildid\\\"\\s+\\\"(?<id>[0-9]+)\\\"");
    if (!buildMatch.Success)
    {
        throw new ContractException($"Steam build ID was not found in {manifestPath}.");
    }
    AssertEqual("Steam build", expectedSteamBuild, buildMatch.Groups["id"].Value);

    var harmonyPath = Path.Combine(
        steamApps,
        "workshop",
        "content",
        "3167020",
        "3589088839",
        "0Harmony.dll");
    RequireFile(harmonyPath);
    var harmonyVersion = AssemblyName.GetAssemblyName(harmonyPath).Version ?? new Version(0, 0);
    if (harmonyVersion < minimumHarmonyVersion)
    {
        throw new ContractException(
            $"HarmonyLib version mismatch. Required at least '{minimumHarmonyVersion}', found '{harmonyVersion}'.");
    }
    VerifyHarmonyReflectionContract(harmonyPath);

    var globalGameManagersPath = Path.Combine(gameRoot, "Duckov_Data", "globalgamemanagers");
    RequireFile(globalGameManagersPath);
    var globalGameManagersText = Encoding.ASCII.GetString(File.ReadAllBytes(globalGameManagersPath));
    var unityMatch = Regex.Match(globalGameManagersText, "2022\\.3\\.[0-9]+f[0-9]+");
    if (!unityMatch.Success)
    {
        throw new ContractException("Unity version was not found in globalgamemanagers.");
    }
    AssertEqual("Unity version", expectedUnityVersion, unityMatch.Value);

    using (var core = new AssemblyMetadata(corePath))
    {
        core.RequireType("Duckov.Modding", "ModBehaviour");
        core.RequireMethod("Duckov.Modding", "ModBehaviour", "OnAfterSetup", parameterCount: 0, mustBeFamily: true, mustBeVirtual: true);
        core.RequireMethod("Duckov.Modding", "ModBehaviour", "OnBeforeDeactivate", parameterCount: 0, mustBeFamily: true, mustBeVirtual: true);

        core.RequireEvent(string.Empty, "CA_UseItem", "OnItemUsedByPlayer", "System.Action", "ItemStatsSystem.Item");
        core.RequireEvent(string.Empty, "RaidUtilities", "OnNewRaid", "System.Action", "RaidInfo");
        core.RequireEvent(string.Empty, "RaidUtilities", "OnRaidEnd", "System.Action", "RaidInfo");
        core.RequireEvent(string.Empty, "RaidUtilities", "OnRaidDead", "System.Action", "RaidInfo");
        foreach (var field in new[] { "valid", "ID", "dead", "ended", "raidBeginTime", "raidEndTime", "totalTime" })
        {
            core.RequireField(string.Empty, "RaidInfo", field, mustBePublic: true);
        }
        core.RequireEvent(string.Empty, "LevelManager", "OnLevelInitialized", "System.Action");
        core.RequireEvent(string.Empty, "LevelManager", "OnAfterLevelInitialized", "System.Action");
        core.RequireEvent(string.Empty, "LevelManager", "OnEvacuated", "System.Action", "EvacuationInfo");
        core.RequireEvent(string.Empty, "LevelManager", "OnMainCharacterDead", "System.Action", "DamageInfo");
        core.RequireEvent(string.Empty, "PauseMenu", "onPauseMenuOn", "System.Action");
        core.RequireEvent(string.Empty, "PauseMenu", "onPauseMenuOff", "System.Action");
        core.RequireEvent(string.Empty, "SceneLoader", "onStartedLoadingScene", "System.Action", "SceneLoadingContext");
        core.RequireEvent(string.Empty, "SceneLoader", "onFinishedLoadingScene", "System.Action", "SceneLoadingContext");
        core.RequireEvent(string.Empty, "SceneLoader", "onAfterSceneInitialize", "System.Action", "SceneLoadingContext");
        core.RequireEvent("Duckov.Scenes", "MultiSceneCore", "OnSubSceneWillBeUnloaded", "System.Action", "Duckov.Scenes.MultiSceneCore", "UnityEngine.SceneManagement.Scene");
        core.RequireEvent("Duckov.Scenes", "MultiSceneCore", "OnSubSceneLoaded", "System.Action", "Duckov.Scenes.MultiSceneCore", "UnityEngine.SceneManagement.Scene");
        core.RequireEvent(string.Empty, "CharacterMainControl", "OnSetPositionEvent", "System.Action", "CharacterMainControl", "UnityEngine.Vector3");
        core.RequireEvent(string.Empty, "LevelManager", "OnNewGameReport", "System.Action");
        core.RequireEvent("Saves", "SavesSystem", "OnSetFile", "System.Action");
        core.RequireEvent("Saves", "SavesSystem", "OnSaveDeleted", "System.Action");
        core.RequireEvent("Saves", "SavesSystem", "OnCollectSaveData", "System.Action");

        core.RequireProperty(string.Empty, "LevelManager", "IsRaidMap");
        core.RequireProperty(string.Empty, "LevelManager", "IsBaseLevel");
        core.RequireProperty(string.Empty, "LevelManager", "LevelInited", "System.Boolean", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "LevelManager", "MainCharacter", "CharacterMainControl", mustBePublic: true);
        core.RequireProperty(string.Empty, "InputManager", "InputActived", "System.Boolean", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "GameManager", "Paused", "System.Boolean", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "SceneLoader", "IsSceneLoading", "System.Boolean", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty("Duckov.Scenes", "MultiSceneCore", "IsLoading", "System.Boolean", mustBePublic: true);
        core.RequireProperty("Duckov.Scenes", "MultiSceneCore", "MainSceneID", "System.String", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "CharacterMainControl", "Main", "CharacterMainControl", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "CharacterMainControl", "IsMainCharacter", "System.Boolean", mustBePublic: true);
        core.RequireProperty(string.Empty, "CharacterMainControl", "Health", "Health", mustBePublic: true);
        core.RequireProperty(string.Empty, "CharacterMainControl", "CharacterWalkSpeed", "System.Single", mustBePublic: true);
        core.RequireProperty(string.Empty, "CharacterMainControl", "CharacterRunSpeed", "System.Single", mustBePublic: true);
        core.RequireProperty(string.Empty, "CharacterMainControl", "DashSpeed", "System.Single", mustBePublic: true);
        core.RequireProperty(string.Empty, "Health", "IsDead", "System.Boolean", mustBePublic: true);
        core.RequireProperty(string.Empty, "SceneInfoEntry", "ID", "System.String", mustBePublic: true);
        core.RequireProperty(string.Empty, "SceneInfoEntry", "DisplayName", "System.String", mustBePublic: true);
        core.RequireMethod(string.Empty, "LevelManager", "GetCurrentLevelInfo", parameterCount: 0);
        core.RequireMethod(string.Empty, "CharacterMainControl", "SetPosition", parameterCount: 1, mustBePublic: true, returnTypeFragment: "System.Void", parameterTypeFragments: ["UnityEngine.Vector3"]);
        core.RequireMethod(string.Empty, "SceneInfoCollection", "GetSceneID", parameterCount: 1, mustBePublic: true, returnTypeFragment: "System.String", parameterTypeFragments: ["System.Int32"]);
        core.RequireMethod(string.Empty, "SceneInfoCollection", "GetSceneInfo", parameterCount: 1, mustBePublic: true, returnTypeFragment: "SceneInfoEntry", parameterTypeFragments: ["System.String"]);
        core.RequireMethod(
            string.Empty,
            "Health",
            "AddHealth",
            parameterCount: 1,
            mustBePublic: true,
            returnTypeFragment: "System.Void",
            parameterTypeFragments: healthAddParameters);
        core.RequireProperty(string.Empty, "Health", "CurrentHealth", "System.Single");
        core.RequireProperty(string.Empty, "Health", "MaxHealth", "System.Single");
        core.RequireProperty(string.Empty, "Health", "IsMainCharacterHealth", "System.Boolean");
        core.RequireMethod(
            "Duckov.Buffs",
            "CharacterBuffManager",
            "AddBuff",
            parameterCount: 3,
            mustBePublic: true,
            returnTypeFragment: "System.Void",
            parameterTypeFragments: buffAddParameters);

        core.RequireType("Duckov.ItemUsage", "Drug");
        core.RequireType("Duckov.ItemUsage", "FoodDrink");
        core.RequireType("Duckov.ItemUsage", "AddBuff");
        core.RequireField("Duckov.ItemUsage", "AddBuff", "buffPrefab");
        core.RequireType(string.Empty, "HealAction");
        core.RequireType("Duckov.ItemUsage", "RemoveBuff");
        core.RequireType("Duckov.ItemUsage", "SpawnEgg");
    }

    using (var itemStats = new AssemblyMetadata(itemStatsPath))
    {
        itemStats.RequireEvent("ItemStatsSystem", "UsageUtilities", "OnItemUsedStaticEvent", "System.Action", "ItemStatsSystem.Item");
        itemStats.RequireEvent("ItemStatsSystem", "Item", "onUseStatic", "System.Action", "ItemStatsSystem.Item", "System.Object");
        itemStats.RequireField("ItemStatsSystem", "UsageUtilities", "behaviors");
        itemStats.RequireMethod(
            "ItemStatsSystem",
            "EffectAction",
            "NotifyTriggered",
            parameterCount: 1,
            mustBeAssembly: true,
            returnTypeFragment: "System.Void",
            parameterTypeFragments: effectTriggerParameters);

        foreach (var property in new[]
                 {
                     "TypeID", "DisplayName", "Stackable", "StackCount", "UseDurability", "MaxDurability", "Durability", "UsageUtilities"
                 })
        {
            itemStats.RequireProperty("ItemStatsSystem", "Item", property);
        }
    }

    Console.WriteLine("Duckov compatibility contract passed.");
    Console.WriteLine($"  Game: {gameVersion} (Steam build {expectedSteamBuild})");
    Console.WriteLine($"  Unity: {unityMatch.Value}");
    Console.WriteLine($"  TeamSoda.Duckov.Core.dll SHA-256: {HashFile(corePath)}");
    Console.WriteLine($"  ItemStatsSystem.dll SHA-256: {HashFile(itemStatsPath)}");
    Console.WriteLine($"  HarmonyLib: {harmonyVersion} SHA-256: {HashFile(harmonyPath)}");
    Console.WriteLine("  Native loader, item/healing, run lifecycle, pause/loading, map, main-duck position, and movement-speed contracts are present.");
    return 0;
}
catch (ContractException exception)
{
    Console.Error.WriteLine($"Compatibility contract failed: {exception.Message}");
    return 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Compatibility probe error: {exception}");
    return 1;
}

static void RequireFile(string path)
{
    if (!File.Exists(path))
    {
        throw new ContractException($"Required file does not exist: {path}");
    }
}

static string ReadIniValue(string path, string key)
{
    RequireFile(path);
    foreach (var line in File.ReadLines(path))
    {
        var index = line.IndexOf('=');
        if (index < 1)
        {
            continue;
        }

        if (string.Equals(line[..index].Trim(), key, StringComparison.OrdinalIgnoreCase))
        {
            return line[(index + 1)..].Trim();
        }
    }

    throw new ContractException($"Key '{key}' was not found in {path}.");
}

static void AssertEqual(string label, string expected, string actual)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new ContractException($"{label} mismatch. Expected '{expected}', found '{actual}'.");
    }
}

static string HashFile(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
}

static void VerifyHarmonyReflectionContract(string harmonyPath)
{
    var assembly = Assembly.LoadFrom(harmonyPath);
    var harmonyType = assembly.GetType("HarmonyLib.Harmony", throwOnError: true)!;
    var harmonyMethodType = assembly.GetType("HarmonyLib.HarmonyMethod", throwOnError: true)!;
    var patchesType = assembly.GetType("HarmonyLib.Patches", throwOnError: true)!;
    var patchType = assembly.GetType("HarmonyLib.Patch", throwOnError: true)!;
    var patchMethod = harmonyType.GetMethod(
        "Patch",
        BindingFlags.Instance | BindingFlags.Public,
        binder: null,
        [typeof(MethodBase), harmonyMethodType, harmonyMethodType, harmonyMethodType, harmonyMethodType],
        modifiers: null);
    var unpatchAllMethod = harmonyType.GetMethod(
        "UnpatchAll",
        BindingFlags.Instance | BindingFlags.Public,
        binder: null,
        [typeof(string)],
        modifiers: null);
    var getPatchInfoMethod = harmonyType.GetMethod(
        "GetPatchInfo",
        BindingFlags.Static | BindingFlags.Public,
        binder: null,
        [typeof(MethodBase)],
        modifiers: null);
    var prefixesMember = FindPublicInstanceMember(patchesType, "Prefixes");
    var postfixesMember = FindPublicInstanceMember(patchesType, "Postfixes");
    var transpilersMember = FindPublicInstanceMember(patchesType, "Transpilers");
    var finalizersMember = FindPublicInstanceMember(patchesType, "Finalizers");
    var ownerMember = patchType.GetProperty("owner", BindingFlags.Instance | BindingFlags.Public)
                      as MemberInfo
                      ?? patchType.GetField("owner", BindingFlags.Instance | BindingFlags.Public);
    var patchMethodProperty = patchType.GetProperty(
        "PatchMethod",
        BindingFlags.Instance | BindingFlags.Public);
    var priorityField = harmonyMethodType.GetField("priority", BindingFlags.Instance | BindingFlags.Public);
    var harmonyConstructor = harmonyType.GetConstructor([typeof(string)]);
    var harmonyMethodConstructor = harmonyMethodType.GetConstructor([typeof(MethodInfo)]);
    if (harmonyConstructor == null
        || harmonyMethodConstructor == null
        || patchMethod == null
        || unpatchAllMethod == null
        || getPatchInfoMethod == null
        || prefixesMember == null
        || postfixesMember == null
        || transpilersMember == null
        || finalizersMember == null
        || ownerMember == null
        || patchMethodProperty?.PropertyType != typeof(MethodInfo)
        || priorityField?.FieldType != typeof(int))
    {
        throw new ContractException("HarmonyLib reflection API required by UDS is missing or changed.");
    }
}

static MemberInfo? FindPublicInstanceMember(Type type, string name) =>
    type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
    ?? type.GetField(name, BindingFlags.Instance | BindingFlags.Public) as MemberInfo;

internal sealed class ContractException : Exception
{
    public ContractException(string message)
        : base(message)
    {
    }
}

internal sealed class AssemblyMetadata : IDisposable
{
    private readonly FileStream stream;
    private readonly PEReader peReader;
    private readonly MetadataReader reader;
    private readonly SignatureTypeNameProvider typeProvider = new();

    public AssemblyMetadata(string path)
    {
        stream = File.OpenRead(path);
        peReader = new PEReader(stream);
        if (!peReader.HasMetadata)
        {
            throw new ContractException($"Assembly has no managed metadata: {path}");
        }

        reader = peReader.GetMetadataReader();
    }

    public void Dispose()
    {
        peReader.Dispose();
        stream.Dispose();
    }

    public void RequireType(string @namespace, string name)
    {
        _ = FindType(@namespace, name);
    }

    public void RequireMethod(
        string @namespace,
        string typeName,
        string methodName,
        int parameterCount,
        bool mustBeFamily = false,
        bool mustBeVirtual = false,
        bool mustBePublic = false,
        bool mustBeAssembly = false,
        bool mustBeStatic = false,
        string? returnTypeFragment = null,
        string[]? parameterTypeFragments = null)
    {
        var type = reader.GetTypeDefinition(FindType(@namespace, typeName));
        foreach (var handle in type.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            if (!string.Equals(reader.GetString(method.Name), methodName, StringComparison.Ordinal))
            {
                continue;
            }

            var signature = method.DecodeSignature(typeProvider, reader);
            if (signature.ParameterTypes.Length != parameterCount)
            {
                continue;
            }

            if (returnTypeFragment != null
                && !signature.ReturnType.Contains(returnTypeFragment, StringComparison.Ordinal))
            {
                continue;
            }

            if (parameterTypeFragments != null
                && (parameterTypeFragments.Length != signature.ParameterTypes.Length
                    || parameterTypeFragments.Where(
                            (fragment, index) => !signature.ParameterTypes[index].Contains(
                                fragment,
                                StringComparison.Ordinal))
                        .Any()))
            {
                continue;
            }

            if (mustBeFamily && (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Family)
            {
                continue;
            }

            if (mustBePublic && (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
            {
                continue;
            }

            if (mustBeAssembly && (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Assembly)
            {
                continue;
            }

            if (mustBeVirtual && (method.Attributes & MethodAttributes.Virtual) == 0)
            {
                continue;
            }

            if (mustBeStatic && (method.Attributes & MethodAttributes.Static) == 0)
            {
                continue;
            }

            return;
        }

        throw new ContractException($"Required method not found: {@namespace}.{typeName}.{methodName}({parameterCount} parameter(s)).");
    }

    public void RequireProperty(
        string @namespace,
        string typeName,
        string propertyName,
        string? propertyTypeFragment = null,
        bool mustBePublic = false,
        bool mustBeStatic = false)
    {
        var type = reader.GetTypeDefinition(FindType(@namespace, typeName));
        foreach (var handle in type.GetProperties())
        {
            var property = reader.GetPropertyDefinition(handle);
            if (!string.Equals(reader.GetString(property.Name), propertyName, StringComparison.Ordinal)
                || (propertyTypeFragment != null
                    && !property.DecodeSignature(typeProvider, reader).ReturnType.Contains(
                        propertyTypeFragment,
                        StringComparison.Ordinal)))
            {
                continue;
            }


            var accessors = property.GetAccessors();
            var accessorHandle = !accessors.Getter.IsNil ? accessors.Getter : accessors.Setter;
            if (accessorHandle.IsNil)
            {
                continue;
            }

            var accessor = reader.GetMethodDefinition(accessorHandle);
            if (mustBePublic
                && (accessor.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
            {
                continue;
            }

            if (mustBeStatic && (accessor.Attributes & MethodAttributes.Static) == 0)
            {
                continue;
            }

            return;
        }

        throw new ContractException($"Required property not found: {@namespace}.{typeName}.{propertyName}.");
    }

    public void RequireField(string @namespace, string typeName, string fieldName, bool mustBePublic = false)
    {
        var type = reader.GetTypeDefinition(FindType(@namespace, typeName));
        foreach (var handle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(handle);
            if (string.Equals(reader.GetString(field.Name), fieldName, StringComparison.Ordinal)
                && (!mustBePublic
                    || (field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public))
            {
                return;
            }
        }

        throw new ContractException($"Required field not found: {@namespace}.{typeName}.{fieldName}.");
    }

    public void RequireEvent(string @namespace, string typeName, string eventName, params string[] parameterTypeFragments)
    {
        var type = reader.GetTypeDefinition(FindType(@namespace, typeName));
        foreach (var handle in type.GetEvents())
        {
            var eventDefinition = reader.GetEventDefinition(handle);
            if (!string.Equals(reader.GetString(eventDefinition.Name), eventName, StringComparison.Ordinal))
            {
                continue;
            }

            var accessors = eventDefinition.GetAccessors();
            if (accessors.Adder.IsNil || accessors.Remover.IsNil)
            {
                throw new ContractException($"Event has incomplete accessors: {@namespace}.{typeName}.{eventName}.");
            }

            var adder = reader.GetMethodDefinition(accessors.Adder);
            var signature = adder.DecodeSignature(typeProvider, reader);
            if (signature.ParameterTypes.Length != 1)
            {
                throw new ContractException($"Event add accessor has an unexpected signature: {@namespace}.{typeName}.{eventName}.");
            }

            var delegateType = signature.ParameterTypes[0];
            if (parameterTypeFragments.All(fragment => delegateType.Contains(fragment, StringComparison.Ordinal)))
            {
                return;
            }

            throw new ContractException(
                $"Event signature mismatch for {@namespace}.{typeName}.{eventName}. Found '{delegateType}'.");
        }

        throw new ContractException($"Required event not found: {@namespace}.{typeName}.{eventName}.");
    }

    private TypeDefinitionHandle FindType(string @namespace, string name)
    {
        foreach (var handle in reader.TypeDefinitions)
        {
            var definition = reader.GetTypeDefinition(handle);
            if (string.Equals(reader.GetString(definition.Namespace), @namespace, StringComparison.Ordinal)
                && string.Equals(reader.GetString(definition.Name), name, StringComparison.Ordinal))
            {
                return handle;
            }
        }

        var qualifiedName = string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
        throw new ContractException($"Required type not found: {qualifiedName}.");
    }
}

internal sealed class SignatureTypeNameProvider : ISignatureTypeProvider<string, MetadataReader>
{
    public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";

    public string GetByReferenceType(string elementType) => $"{elementType}&";

    public string GetFunctionPointerType(MethodSignature<string> signature) => "methodptr";

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        $"{genericType}<{string.Join(",", typeArguments)}>";

    public string GetGenericMethodParameter(MetadataReader genericContext, int index) => $"!!{index}";

    public string GetGenericTypeParameter(MetadataReader genericContext, int index) => $"!{index}";

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

    public string GetPinnedType(string elementType) => elementType;

    public string GetPointerType(string elementType) => $"{elementType}*";

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean => "System.Boolean",
        PrimitiveTypeCode.Byte => "System.Byte",
        PrimitiveTypeCode.Char => "System.Char",
        PrimitiveTypeCode.Double => "System.Double",
        PrimitiveTypeCode.Int16 => "System.Int16",
        PrimitiveTypeCode.Int32 => "System.Int32",
        PrimitiveTypeCode.Int64 => "System.Int64",
        PrimitiveTypeCode.IntPtr => "System.IntPtr",
        PrimitiveTypeCode.Object => "System.Object",
        PrimitiveTypeCode.SByte => "System.SByte",
        PrimitiveTypeCode.Single => "System.Single",
        PrimitiveTypeCode.String => "System.String",
        PrimitiveTypeCode.TypedReference => "System.TypedReference",
        PrimitiveTypeCode.UInt16 => "System.UInt16",
        PrimitiveTypeCode.UInt32 => "System.UInt32",
        PrimitiveTypeCode.UInt64 => "System.UInt64",
        PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
        PrimitiveTypeCode.Void => "System.Void",
        _ => typeCode.ToString()
    };

    public string GetSZArrayType(string elementType) => $"{elementType}[]";

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var definition = reader.GetTypeDefinition(handle);
        return Qualify(reader.GetString(definition.Namespace), reader.GetString(definition.Name));
    }

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var reference = reader.GetTypeReference(handle);
        return Qualify(reader.GetString(reference.Namespace), reader.GetString(reference.Name));
    }

    public string GetTypeFromSpecification(
        MetadataReader reader,
        MetadataReader genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind) => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    private static string Qualify(string @namespace, string name) =>
        string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
}

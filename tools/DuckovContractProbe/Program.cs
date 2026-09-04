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
string[] itemParameters = ["ItemStatsSystem.Item"];

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
    var sodaLocalizationPath = Path.Combine(managedRoot, "SodaLocalization.dll");
    var unityUiPath = Path.Combine(managedRoot, "UnityEngine.UI.dll");
    var textMeshProPath = Path.Combine(managedRoot, "Unity.TextMeshPro.dll");
    var pluginsPath = Path.Combine(managedRoot, "Plugins.dll");
    var resourcesPath = Path.Combine(gameRoot, "Duckov_Data", "resources.assets");

    RequireFile(corePath);
    RequireFile(itemStatsPath);
    RequireFile(sodaLocalizationPath);
    RequireFile(unityUiPath);
    RequireFile(textMeshProPath);
    RequireFile(pluginsPath);
    RequireFile(resourcesPath);

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
        core.RequireEvent(string.Empty, "LevelManager", "OnLevelBeginInitializing", "System.Action");
        core.RequireEvent(string.Empty, "LevelManager", "OnAfterLevelInitialized", "System.Action");
        core.RequireEvent(string.Empty, "LevelManager", "OnEvacuated", "System.Action", "EvacuationInfo");
        core.RequireEvent(string.Empty, "LevelManager", "OnMainCharacterDead", "System.Action", "DamageInfo");
        core.RequireEvent(string.Empty, "PauseMenu", "onPauseMenuOn", "System.Action");
        core.RequireEvent(string.Empty, "PauseMenu", "onPauseMenuOff", "System.Action");
        core.RequireField(string.Empty, "MainMenu", "OnMainMenuAwake", mustBePublic: true, mustBeStatic: true, fieldTypeFragment: "System.Action");
        core.RequireField(string.Empty, "MainMenu", "OnMainMenuDestroy", mustBePublic: true, mustBeStatic: true, fieldTypeFragment: "System.Action");
        core.RequireProperty(string.Empty, "PauseMenu", "Instance", "PauseMenu", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "PauseMenu", "Shown", "System.Boolean", mustBePublic: true);
        core.RequireProperty(string.Empty, "GameManager", "EventSystem", "UnityEngine.EventSystems.EventSystem", mustBePublic: true, mustBeStatic: true);
        core.RequireMethod("Duckov.UI", "NotificationText", "Push", 1, mustBePublic: true, mustBeStatic: true, parameterTypeFragments: ["System.String"]);
        core.RequireEvent(string.Empty, "SceneLoader", "onStartedLoadingScene", "System.Action", "SceneLoadingContext");
        core.RequireEvent(string.Empty, "SceneLoader", "onFinishedLoadingScene", "System.Action", "SceneLoadingContext");
        core.RequireEvent(string.Empty, "SceneLoader", "onAfterSceneInitialize", "System.Action", "SceneLoadingContext");
        core.RequireEvent("Duckov.Scenes", "MultiSceneCore", "OnSubSceneWillBeUnloaded", "System.Action", "Duckov.Scenes.MultiSceneCore", "UnityEngine.SceneManagement.Scene");
        core.RequireEvent("Duckov.Scenes", "MultiSceneCore", "OnSubSceneLoaded", "System.Action", "Duckov.Scenes.MultiSceneCore", "UnityEngine.SceneManagement.Scene");
        core.RequireEvent("Duckov", "CheatMode", "OnCheatModeStatusChanged", "System.Action", "System.Boolean");
        core.RequireEvent("Duckov.Rules", "GameRulesManager", "OnRuleChanged", "System.Action");
        core.RequireEvent(string.Empty, "CharacterMainControl", "OnSetPositionEvent", "System.Action", "CharacterMainControl", "UnityEngine.Vector3");
        core.RequirePublicStaticEvent(string.Empty, "InteractableLootbox", "OnStartLoot", "System.Action", "InteractableLootbox");
        core.RequirePublicStaticEvent(string.Empty, "ItemAgent_Gun", "OnMainCharacterShootEvent", "System.Action", "ItemAgent_Gun");
        core.RequireField(string.Empty, "CharacterMainControl", "OnMainCharacterSlotContentChangedEvent", mustBePublic: true);
        core.RequireField(string.Empty, "CharacterMainControl", "OnMainCharacterChangeHoldItemAgentEvent", mustBePublic: true);
        core.RequireField(string.Empty, "CharacterMainControl", "OnMainCharacterInventoryChangedEvent", mustBePublic: true);
        core.RequirePublicStaticEvent(string.Empty, "Health", "OnHurt", "System.Action", "Health", "DamageInfo");
        core.RequirePublicStaticEvent(string.Empty, "Health", "OnDead", "System.Action", "Health", "DamageInfo");
        core.RequireEvent(string.Empty, "LevelManager", "OnNewGameReport", "System.Action");
        core.RequireEvent("Saves", "SavesSystem", "OnSetFile", "System.Action");
        core.RequireEvent("Saves", "SavesSystem", "OnSaveDeleted", "System.Action");
        core.RequireEvent("Saves", "SavesSystem", "OnCollectSaveData", "System.Action");
        core.RequireEvent("Duckov.Economy", "EconomyManager", "OnMoneyChanged", "System.Action", "System.Int64");
        core.RequireEvent("Duckov.Economy", "EconomyManager", "OnMoneyPaid", "System.Action", "System.Int64");
        core.RequireEvent("Duckov.Economy", "EconomyManager", "OnEconomyManagerLoaded", "System.Action");
        core.RequireEvent("Duckov.Economy", "EconomyManager", "OnCostPaid", "System.Action", "Duckov.Economy.Cost");
        core.RequireEvent("Duckov.Economy", "StockShop", "OnItemSoldByPlayer", "System.Action", "Duckov.Economy.StockShop", "ItemStatsSystem.Item", "System.Int32");
        core.RequireEvent(string.Empty, "GameClock", "OnGameClockStep", "System.Action");
        core.RequireProperty(string.Empty, "GameClock", "Instance", "GameClock", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "GameClock", "Day", "System.Int64", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "GameClock", "TimeOfDay", "System.TimeSpan", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "GameClock", "Now", "System.TimeSpan", mustBePublic: true, mustBeStatic: true);
        core.RequireDoubleConstant(string.Empty, "GameClock", "SecondsPerDay", 86300d);
        core.RequireMethod(
            string.Empty,
            "GameClock",
            "Step",
            parameterCount: 1,
            mustBeAssembly: true,
            mustBeStatic: true,
            returnTypeFragment: "System.Void",
            parameterTypeFragments: ["System.Single"]);
        core.RequireMethod(
            string.Empty,
            "GameClock",
            "StepTimeTil",
            parameterCount: 1,
            mustBePublic: true,
            returnTypeFragment: "System.Void",
            parameterTypeFragments: ["System.TimeSpan"]);
        core.RequireField("Duckov.UI", "SleepView", "OnAfterSleep", mustBePublic: true, mustBeStatic: true, fieldTypeFragment: "System.Action");
        core.RequireEvent("Duckov.Quests", "Reward", "OnRewardClaimed", "System.Action", "Duckov.Quests.Reward");
        core.RequireEvent(string.Empty, "InteractablePickup", "OnPickupSuccess", "System.Action", "InteractablePickup", "CharacterMainControl");
        core.RequireEvent(string.Empty, "ItemUtilities", "OnPlayerItemOperation", "System.Action");
        core.RequireEvent(string.Empty, "PlayerStorage", "OnPlayerStorageChange", "System.Action", "PlayerStorage", "ItemStatsSystem.Inventory", "System.Int32");
        core.RequireEvent(string.Empty, "PlayerStorage", "OnLoadingFinished", "System.Action");

        core.RequireProperty(string.Empty, "LevelManager", "IsRaidMap");
        core.RequireProperty(string.Empty, "LevelManager", "IsBaseLevel");
        core.RequireProperty(string.Empty, "LevelManager", "Instance", "LevelManager", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "LevelManager", "LevelInited", "System.Boolean", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "LevelManager", "LevelInitializing", "System.Boolean", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "LevelManager", "MainCharacter", "CharacterMainControl", mustBePublic: true);
        core.RequireProperty(string.Empty, "LevelManager", "PetCharacter", "CharacterMainControl", mustBePublic: true);
        core.RequireProperty(string.Empty, "LevelManager", "PetProxy", "PetProxy", mustBePublic: true);
        core.RequireProperty(string.Empty, "InputManager", "InputActived", "System.Boolean", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "InputManager", "AimingEnemyHead", "System.Boolean", mustBePublic: true);
        core.RequireProperty(string.Empty, "GameManager", "Paused", "System.Boolean", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "SceneLoader", "IsSceneLoading", "System.Boolean", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty("Duckov.Scenes", "MultiSceneCore", "IsLoading", "System.Boolean", mustBePublic: true);
        core.RequireProperty("Duckov.Scenes", "MultiSceneCore", "MainSceneID", "System.String", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty("Duckov.Scenes", "MultiSceneCore", "ActiveSubSceneID", "System.String", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty("Duckov", "CheatMode", "Active", "System.Boolean", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty("Duckov.Rules", "GameRulesManager", "SelectedRuleIndex", "Duckov.Rules.RuleIndex", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "CharacterMainControl", "Main", "CharacterMainControl", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "CharacterMainControl", "IsMainCharacter", "System.Boolean", mustBePublic: true);
        core.RequireProperty(string.Empty, "CharacterMainControl", "Health", "Health", mustBePublic: true);
        core.RequireProperty(string.Empty, "CharacterMainControl", "CharacterItem", "ItemStatsSystem.Item", mustBePublic: true);
        core.RequireProperty(string.Empty, "CharacterMainControl", "CurrentHoldItemAgent", "DuckovItemAgent", mustBePublic: true);
        core.RequireProperty(string.Empty, "CharacterMainControl", "CharacterWalkSpeed", "System.Single", mustBePublic: true);
        core.RequireProperty(string.Empty, "CharacterMainControl", "CharacterRunSpeed", "System.Single", mustBePublic: true);
        core.RequireProperty(string.Empty, "CharacterMainControl", "DashSpeed", "System.Single", mustBePublic: true);
        core.RequireProperty(string.Empty, "LevelManager", "ControllingCharacter", "CharacterMainControl", mustBePublic: true);
        core.RequireProperty(string.Empty, "LevelManager", "PetCharacter", "CharacterMainControl", mustBePublic: true);
        core.RequireProperty("Duckov.Economy", "EconomyManager", "Money", "System.Int64", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty("Duckov.Economy", "EconomyManager", "Cash", "System.Int64", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty("Duckov.Economy", "EconomyManager", "Instance", "Duckov.Economy.EconomyManager", mustBePublic: true, mustBeStatic: true);
        core.RequireField("Duckov.Economy", "EconomyManager", "CashItemID", mustBePublic: true, fieldTypeFragment: "System.Int32");
        core.RequireField("Duckov.Economy", "EconomyManager", "money", mustBePrivate: true, fieldTypeFragment: "System.Int64");
        core.RequireNestedField("Duckov.Economy", "EconomyManager", "SaveData", "money", "System.Int64");
        core.RequireMethod(
            "Duckov.Economy", "EconomyManager", "Load", 0,
            mustBePrivate: true, returnTypeFragment: "System.Void");
        core.RequireMethod(
            "Duckov.Economy", "EconomyManager", "GenerateSaveData", 0,
            mustBePublic: true, returnTypeFragment: "System.Object");
        core.RequireMethod(
            "Duckov.Economy", "EconomyManager", "SetupSaveData", 1,
            mustBePublic: true, returnTypeFragment: "System.Void", parameterTypeFragments: ["System.Object"]);
        core.RequireMethod(
            "Saves", "SavesSystem", "KeyExisits", 1,
            mustBePublic: true, mustBeStatic: true, returnTypeFragment: "System.Boolean",
            parameterTypeFragments: ["System.String"]);
        core.RequireMethod(
            string.Empty, "ItemUtilities", "FindAllBelongsToPlayer", 1,
            mustBePublic: true, mustBeStatic: true,
            returnTypeFragment: "System.Collections.Generic.List",
            parameterTypeFragments: ["System.Predicate"]);
        core.RequireMethod(
            string.Empty, "ItemUtilities", "GetItemCount", 1,
            mustBePublic: true, mustBeStatic: true, returnTypeFragment: "System.Int32",
            parameterTypeFragments: ["System.Int32"]);
        core.RequireProperty(string.Empty, "PlayerStorage", "Inventory", "ItemStatsSystem.Inventory", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "PlayerStorage", "Loading", "System.Boolean", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty(string.Empty, "PetProxy", "Inventory", "ItemStatsSystem.Inventory", mustBePublic: true);
        core.RequireProperty(string.Empty, "PetProxy", "PetInventory", "ItemStatsSystem.Inventory", mustBePublic: true, mustBeStatic: true);
        core.RequireMethod(
            string.Empty, "ATMPanel", "Save", 1,
            mustBePublic: true, mustBeStatic: true, returnTypeFragment: "System.Boolean",
            parameterTypeFragments: ["System.Int64"]);
        core.RequireMethod(
            string.Empty, "ATMPanel", "Draw", 1,
            mustBePublic: true, mustBeStatic: true, returnTypeFragment: "Cysharp.Threading.Tasks.UniTask",
            parameterTypeFragments: ["System.Int64"]);
        core.RequireInt64Constant(string.Empty, "ATMPanel", "MaxDrawAmount", 10000000L);
        core.RequireField("Duckov.Economy", "Cost", "items", mustBePublic: true, fieldTypeFragment: "ItemEntry");
        core.RequireField("Duckov.Economy", "Cost", "money", mustBePublic: true, fieldTypeFragment: "System.Int64");
        core.RequireNestedField("Duckov.Economy", "Cost", "ItemEntry", "id", "System.Int32");
        core.RequireNestedField("Duckov.Economy", "Cost", "ItemEntry", "amount", "System.Int64");
        core.RequireProperty("Duckov.Economy", "Cost", "IsFree", "System.Boolean", mustBePublic: true);
        core.RequireProperty("Duckov.Economy", "Cost", "Enough", "System.Boolean", mustBePublic: true);
        core.RequireMethod(
            "Duckov.Economy", "Cost", "Pay", 2,
            mustBePublic: true, returnTypeFragment: "System.Boolean",
            parameterTypeFragments: ["System.Boolean", "System.Boolean"]);
        core.RequireMethod(
            "Duckov.Economy", "EconomyManager", "Pay", 3,
            mustBePublic: true, mustBeStatic: true, returnTypeFragment: "System.Boolean",
            parameterTypeFragments: ["Duckov.Economy.Cost", "System.Boolean", "System.Boolean"]);
        core.RequireField(string.Empty, "CraftingFormula", "id", mustBePublic: true, fieldTypeFragment: "System.String");
        core.RequireField(string.Empty, "CraftingFormula", "result", mustBePublic: true, fieldTypeFragment: "ItemEntry");
        core.RequireNestedField(string.Empty, "CraftingFormula", "ItemEntry", "id", "System.Int32");
        core.RequireNestedField(string.Empty, "CraftingFormula", "ItemEntry", "amount", "System.Int32");
        core.RequireField(
            string.Empty,
            "CraftingManager",
            "OnItemCrafted",
            mustBePublic: true,
            mustBeStatic: true,
            fieldTypeFragment: "CraftingFormula");
        core.RequireField(
            string.Empty,
            "CraftingManager",
            "OnItemCrafted",
            mustBePublic: true,
            mustBeStatic: true,
            fieldTypeFragment: "ItemStatsSystem.Item");
        core.RequireMethod(
            string.Empty,
            "CraftingManager",
            "Craft",
            parameterCount: 1,
            mustBePrivate: true,
            returnTypeFragment: "Cysharp.Threading.Tasks.UniTask",
            parameterTypeFragments: ["CraftingFormula"]);
        core.RequireMethod(
            string.Empty,
            "CraftingManager",
            "Craft",
            parameterCount: 1,
            mustBePrivate: true,
            returnTypeFragment: "ItemStatsSystem.Item",
            parameterTypeFragments: ["CraftingFormula"]);
        core.RequireMethod(
            string.Empty,
            "CraftingManager",
            "Craft",
            parameterCount: 1,
            mustBePublic: true,
            returnTypeFragment: "Cysharp.Threading.Tasks.UniTask",
            parameterTypeFragments: ["System.String"]);
        core.RequireMethod(
            string.Empty,
            "CraftingManager",
            "Craft",
            parameterCount: 1,
            mustBePublic: true,
            returnTypeFragment: "ItemStatsSystem.Item",
            parameterTypeFragments: ["System.String"]);
        core.RequireMethod(
            "Duckov.Economy",
            "Cost",
            "Return",
            parameterCount: 4,
            mustBeAssembly: true,
            returnTypeFragment: "Cysharp.Threading.Tasks.UniTask",
            parameterTypeFragments: ["System.Boolean", "System.Boolean", "System.Int32", "ItemStatsSystem.Item"]);
        core.RequireMethod(
            string.Empty,
            "PlayerStorage",
            "Push",
            parameterCount: 2,
            mustBePublic: true,
            mustBeStatic: true,
            returnTypeFragment: "System.Void",
            parameterTypeFragments: ["ItemStatsSystem.Item", "System.Boolean"]);
        core.RequireMethod(
            string.Empty,
            "ItemUtilities",
            "SendToPlayerCharacterInventory",
            parameterCount: 2,
            mustBePublic: true,
            mustBeStatic: true,
            returnTypeFragment: "System.Boolean",
            parameterTypeFragments: ["ItemStatsSystem.Item", "System.Boolean"]);
        core.RequireProperty("Duckov.Utilities", "GameplayDataSettings", "Prefabs", "PrefabsData", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty("Duckov.Utilities", "GameplayDataSettings", "UIPrefabs", "Duckov.UI.UIPrefabsReference", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty("Duckov.Utilities", "GameplayDataSettings", "UIStyle", "UIStyleData", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty("Duckov.UI", "UIPrefabsReference", "Button", "UnityEngine.UI.Button", mustBePublic: true);
        core.RequireProperty("Duckov.UI", "UIPrefabsReference", "ScrollRect", "UnityEngine.UI.ScrollRect", mustBePublic: true);
        core.RequireNestedProperty(
            "Duckov.Utilities",
            "GameplayDataSettings",
            "UIStyleData",
            "TemplateTextUGUI",
            "TMPro.TextMeshProUGUI",
            mustBePublic: true);
        core.RequireType(string.Empty, "CanvasScalerController");
        core.RequireProperty("Duckov", "GameMetaData", "Instance", "Duckov.GameMetaData", mustBePublic: true, mustBeStatic: true);
        core.RequireProperty("Duckov", "GameMetaData", "Version", "Duckov.VersionData", mustBePublic: true);
        core.RequireProperty(string.Empty, "PrefabsData", "LootBoxPrefab_Tomb", "InteractableLootbox", mustBePublic: true);
        core.RequireProperty(string.Empty, "DuckovItemAgent", "Holder", "CharacterMainControl", mustBePublic: true);
        core.RequireProperty(string.Empty, "ItemAgent_Gun", "GunItemSetting", "ItemSetting_Gun", mustBePublic: true);
        core.RequireProperty(string.Empty, "ItemSetting_Gun", "TargetBulletID", "System.Int32", mustBePublic: true);
        core.RequireProperty(string.Empty, "ItemSetting_Gun", "CurrentBulletName", "System.String", mustBePublic: true);
        core.RequireProperty(string.Empty, "Health", "IsDead", "System.Boolean", mustBePublic: true);
        core.RequireProperty(string.Empty, "SceneInfoEntry", "ID", "System.String", mustBePublic: true);
        core.RequireProperty(string.Empty, "SceneInfoEntry", "DisplayName", "System.String", mustBePublic: true);
        core.RequireMethod(string.Empty, "LevelManager", "GetCurrentLevelInfo", parameterCount: 0);
        core.RequireMethod(string.Empty, "CharacterMainControl", "SetPosition", parameterCount: 1, mustBePublic: true, returnTypeFragment: "System.Void", parameterTypeFragments: ["UnityEngine.Vector3"]);
        core.RequireMethod(string.Empty, "InteractableLootbox", "GetKey", 0, mustBePrivate: true, returnTypeFragment: "System.Int32");
        core.RequireMethod(string.Empty, "CharacterMainControl", "OnDead", 1, mustBePrivate: true, returnTypeFragment: "System.Void", parameterTypeFragments: ["DamageInfo"]);
        core.RequireMethod(string.Empty, "InteractableLootbox", "CreateFromItem", 6, mustBePublic: true, mustBeStatic: true,
            returnTypeFragment: "InteractableLootbox",
            parameterTypeFragments: ["ItemStatsSystem.Item", "UnityEngine.Vector3", "UnityEngine.Quaternion", "System.Boolean", "InteractableLootbox", "System.Boolean"]);
        core.RequireField(string.Empty, "InteractableBase", "interactCharacter", mustBeFamily: true, fieldTypeFragment: "CharacterMainControl");
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
        core.RequireField(string.Empty, "Health", "team", mustBePublic: true);
        core.RequireField(string.Empty, "Health", "isZombie", mustBePublic: true);
        core.RequireMethod(string.Empty, "Health", "Hurt", 1, mustBePublic: true, returnTypeFragment: "System.Boolean", parameterTypeFragments: ["DamageInfo"]);
        core.RequireMethod(string.Empty, "Health", "TryGetCharacter", 0, mustBePublic: true, returnTypeFragment: "CharacterMainControl");
        core.RequireMethod(string.Empty, "Projectile", "Init", 1, mustBePublic: true, returnTypeFragment: "System.Void", parameterTypeFragments: ["ProjectileContext"]);
        core.RequireMethod(string.Empty, "Projectile", "Update", 0, mustBePrivate: true, returnTypeFragment: "System.Void");
        core.RequireMethod(string.Empty, "Projectile", "Release", 0, mustBePrivate: true, returnTypeFragment: "System.Void");
        core.RequireMethod(string.Empty, "ItemAgent_MeleeWeapon", "CheckCollidersInRange", 1, mustBePrivate: true, returnTypeFragment: "System.Int32", parameterTypeFragments: ["System.Boolean"]);
        core.RequireField(string.Empty, "CharacterMainControl", "attackAction", mustBePublic: true);
        core.RequireField(string.Empty, "CharacterMainControl", "characterPreset", mustBePublic: true);
        core.RequireField(string.Empty, "CharacterRandomPreset", "nameKey", mustBePublic: true);
        core.RequireField(string.Empty, "PetAI", "master", mustBePublic: true);
        core.RequireField(string.Empty, "AICharacterController", "leader", mustBePublic: true, fieldTypeFragment: "CharacterMainControl");
        core.RequireField("Duckov.Buffs", "Buff", "fromWho", mustBePublic: true, fieldTypeFragment: "CharacterMainControl");
        core.RequireField("Duckov.Buffs", "Buff", "fromWeaponID", mustBePublic: true, fieldTypeFragment: "System.Int32");
        core.RequireMethod(string.Empty, "ZoneDamage", "Damage", 0, mustBePrivate: true, returnTypeFragment: "System.Void");
        foreach (var field in new[] { "damageType", "isFromBuffOrEffect", "damageValue", "finalDamage", "fromCharacter", "toDamageReceiver", "crit", "fromWeaponItemID", "isExplosion", "buff" })
        {
            core.RequireField(string.Empty, "DamageInfo", field, mustBePublic: true);
        }
        foreach (var field in new[] { "traceTarget", "fromCharacter", "realFromCharacter", "fromGunItemSetting", "fromWeaponItemID" })
        {
            core.RequireField(string.Empty, "ProjectileContext", field, mustBePublic: true);
        }
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
        itemStats.RequireEvent("ItemStatsSystem", "Item", "onItemTreeChanged", "System.Action", "ItemStatsSystem.Item");
        itemStats.RequireEvent("ItemStatsSystem", "Item", "onSlotContentChanged", "System.Action", "ItemStatsSystem.Item", "ItemStatsSystem.Items.Slot");
        itemStats.RequireProperty("ItemStatsSystem", "ItemAgent", "Item", "ItemStatsSystem.Item", mustBePublic: true);
        itemStats.RequireProperty("ItemStatsSystem", "Item", "IsBeingDestroyed", "System.Boolean", mustBePublic: true);
        itemStats.RequireProperty("ItemStatsSystem", "Item", "StackCount", "System.Int32", mustBePublic: true);
        itemStats.RequireMethod(
            "ItemStatsSystem", "Item", "MarkDestroyed", 0,
            mustBePublic: true, returnTypeFragment: "System.Void");
        itemStats.RequireMethod(
            "ItemStatsSystem", "Item", "set_StackCount", 1,
            mustBePublic: true, returnTypeFragment: "System.Void",
            parameterTypeFragments: ["System.Int32"]);
        itemStats.RequireMethod(
            "ItemStatsSystem", "Item", "Combine", 1,
            mustBePublic: true, returnTypeFragment: "System.Void",
            parameterTypeFragments: ["ItemStatsSystem.Item"]);
        itemStats.RequireField("ItemStatsSystem", "UsageUtilities", "behaviors");
        itemStats.RequireMethod(
            "ItemStatsSystem",
            "EffectAction",
            "NotifyTriggered",
            parameterCount: 1,
            mustBeAssembly: true,
            returnTypeFragment: "System.Void",
            parameterTypeFragments: effectTriggerParameters);
        itemStats.RequireMethod(
            "ItemStatsSystem", "Effect", "Trigger", 1, mustBeAssembly: true,
            returnTypeFragment: "System.Void", parameterTypeFragments: effectTriggerParameters);
        itemStats.RequireMethod(
            "ItemStatsSystem", "Effect", "SetItem", 1, mustBePublic: true,
            returnTypeFragment: "System.Void", parameterTypeFragments: itemParameters);
        itemStats.RequireType("ItemStatsSystem", "TickTrigger");
        itemStats.RequireType("ItemStatsSystem", "UpdateTrigger");
        itemStats.RequireField("ItemStatsSystem", "EffectTriggerEventContext", "source", mustBePublic: true);

        foreach (var property in new[]
                 {
                     "TypeID", "DisplayName", "DisplayNameRaw", "Slots", "Inventory", "Tags", "Stackable", "UseDurability", "MaxDurability", "Durability", "UsageUtilities"
                 })
        {
            itemStats.RequireProperty("ItemStatsSystem", "Item", property);
        }
        itemStats.RequireProperty("ItemStatsSystem.Items", "Slot", "Key", "System.String", mustBePublic: true);
        itemStats.RequireProperty("ItemStatsSystem.Items", "Slot", "DisplayName", "System.String", mustBePublic: true);
        itemStats.RequireProperty("ItemStatsSystem.Items", "Slot", "Content", "ItemStatsSystem.Item", mustBePublic: true);
        itemStats.RequireEvent("ItemStatsSystem.Items", "Slot", "onSlotContentChanged", "System.Action", "ItemStatsSystem.Items.Slot");
        itemStats.RequireProperty("ItemStatsSystem.Items", "SlotCollection", "Count", "System.Int32", mustBePublic: true);
        itemStats.RequireMethod(
            "ItemStatsSystem.Items",
            "SlotCollection",
            "GetEnumerator",
            parameterCount: 0,
            mustBePublic: true,
            returnTypeFragment: "System.Collections.Generic.IEnumerator");
        itemStats.RequireProperty("ItemStatsSystem", "Inventory", "Content", mustBePublic: true);
        itemStats.RequireProperty("ItemStatsSystem", "Inventory", "Loading", "System.Boolean", mustBePublic: true);
        itemStats.RequireEvent("ItemStatsSystem", "Inventory", "onContentChanged", "System.Action", "ItemStatsSystem.Inventory", "System.Int32");
        itemStats.RequireMethod(
            "ItemStatsSystem",
            "ItemAssetsCollection",
            "GetMetaData",
            parameterCount: 1,
            mustBePublic: true,
            mustBeStatic: true,
            returnTypeFragment: "ItemStatsSystem.ItemMetaData",
            parameterTypeFragments: ["System.Int32"]);
        itemStats.RequireProperty("ItemStatsSystem", "ItemMetaData", "Name", "System.String", mustBePublic: true);
        itemStats.RequireProperty("ItemStatsSystem", "ItemMetaData", "DisplayName", "System.String", mustBePublic: true);
        itemStats.RequireField("ItemStatsSystem", "ItemMetaData", "icon", mustBePublic: true, fieldTypeFragment: "UnityEngine.Sprite");
    }

    using (var plugins = new AssemblyMetadata(pluginsPath))
    {
        plugins.RequireType("UnityEngine.UI.ProceduralImage", "ProceduralImage");
        plugins.RequireType("UnityEngine.UI.ProceduralImage", "ProceduralImageModifier");
        plugins.RequireProperty("UnityEngine.UI.ProceduralImage", "ProceduralImage", "BorderWidth", "System.Single", mustBePublic: true);
        plugins.RequireProperty("UnityEngine.UI.ProceduralImage", "ProceduralImage", "FalloffDistance", "System.Single", mustBePublic: true);
        plugins.RequireType(string.Empty, "UniformModifier");
        plugins.RequireProperty(string.Empty, "UniformModifier", "Radius", "System.Single", mustBePublic: true);
        plugins.RequireMethod(string.Empty, "UniformModifier", "set_Radius", 1, mustBePublic: true, parameterTypeFragments: ["System.Single"]);
    }

    using (var localization = new AssemblyMetadata(sodaLocalizationPath))
    {
        localization.RequireField("SodaCraft.Localizations", "LocalizationManager", "overrideTexts", mustBePublic: true, mustBeStatic: true, fieldTypeFragment: "System.Collections.Generic.Dictionary");
        localization.RequireMethod("SodaCraft.Localizations", "LocalizationManager", "SetOverrideText", 2, mustBePublic: true, mustBeStatic: true, parameterTypeFragments: ["System.String", "System.String"]);
        localization.RequireMethod("SodaCraft.Localizations", "LocalizationManager", "RemoveOverrideText", 1, mustBePublic: true, mustBeStatic: true, parameterTypeFragments: ["System.String"]);
        localization.RequireMethod("SodaCraft.Localizations", "LocalizationManager", "GetPlainText", 1, mustBePublic: true, mustBeStatic: true, returnTypeFragment: "System.String", parameterTypeFragments: ["System.String"]);
        localization.RequireProperty("SodaCraft.Localizations", "TextLocalizor", "Key", "System.String", mustBePublic: true);
    }

    var craftingFormulaAudit = AuditCraftingFormulas(resourcesPath);

    Console.WriteLine("Duckov compatibility contract passed.");
    Console.WriteLine($"  Game: {gameVersion} (Steam build {expectedSteamBuild})");
    Console.WriteLine($"  Unity: {unityMatch.Value}");
    Console.WriteLine($"  TeamSoda.Duckov.Core.dll SHA-256: {HashFile(corePath)}");
    Console.WriteLine($"  ItemStatsSystem.dll SHA-256: {HashFile(itemStatsPath)}");
    Console.WriteLine($"  SodaLocalization.dll SHA-256: {HashFile(sodaLocalizationPath)}");
    Console.WriteLine($"  resources.assets SHA-256: {HashFile(resourcesPath)}");
    Console.WriteLine($"  HarmonyLib: {harmonyVersion} SHA-256: {HashFile(harmonyPath)}");
    Console.WriteLine($"  Crafting formulas: {craftingFormulaAudit.FormulaCount}; serialized bytes: {craftingFormulaAudit.SerializedBytes}; item-cost entries: {craftingFormulaAudit.ItemCostEntryCount}; empty item-cost arrays: {craftingFormulaAudit.EmptyItemCostCount}; repeated resource ids within one formula: {craftingFormulaAudit.RepeatedResourceIdCount}; maximum item-cost entries/formula: {craftingFormulaAudit.MaximumItemCostEntries}.");
    if (craftingFormulaAudit.NonzeroCurrencyFormulas.Count == 0)
        Console.WriteLine("  Crafting formulas with nonzero Cost.money: none.");
    else
    {
        Console.WriteLine($"  Crafting formulas with nonzero Cost.money: {craftingFormulaAudit.NonzeroCurrencyFormulas.Count}.");
        foreach (var formula in craftingFormulaAudit.NonzeroCurrencyFormulas)
            Console.WriteLine($"    {formula.FormulaId} -> output {formula.OutputItemId}: money={formula.Money}; tags={formula.Tags}; items={formula.ItemCosts}");
    }
    Console.WriteLine("  Native loader, multi-map route identity/transition, item/healing, run lifecycle, movement, weapon, combat, lossless M14 equipment-slot enumeration, containers, M12 world-clock/sleep, M13 crafting task/delivery, M15 authoritative Money/Cash holdings, M16 CraftingFormula.cost item/currency plus repeated-stack mutation/transfer, and M17 retained UI/menu/localization/item-icon/toast/focus/procedural-image contracts are present.");
    Console.WriteLine("  M4 loaded-ammunition consumption, M6 tote activation, M13 crafting workstation/run-map/multiple-output attribution, and M16 Money/Cash charge splitting remain unavailable; M5 accuracy uses completed player projectiles from the independently verified Projectile.Release contract.");
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

static CraftingFormulaAudit AuditCraftingFormulas(string resourcesPath)
{
    var data = File.ReadAllBytes(resourcesPath);
    var name = Encoding.UTF8.GetBytes("CraftingFormulas");
    var pattern = new byte[sizeof(int) + name.Length];
    BitConverter.GetBytes(name.Length).CopyTo(pattern, 0);
    name.CopyTo(pattern, sizeof(int));
    var offset = data.AsSpan().IndexOf(pattern);
    if (offset < 0)
        throw new ContractException("CraftingFormulaCollection serialized object was not found in resources.assets.");
    if (data.AsSpan(offset + 1).IndexOf(pattern) >= 0)
        throw new ContractException("CraftingFormulaCollection serialized object was not unique in resources.assets.");

    var reader = new UnityAssetReader(data, offset);
    if (!string.Equals(reader.ReadString(), "CraftingFormulas", StringComparison.Ordinal))
        throw new ContractException("CraftingFormulaCollection serialized name was invalid.");
    var formulaDataStart = reader.Position;
    var formulaCount = reader.ReadBoundedCount("crafting formula", 100_000);
    if (formulaCount == 0)
        throw new ContractException("CraftingFormulaCollection contained no formulas.");

    var formulaIds = new HashSet<string>(StringComparer.Ordinal);
    var nonzeroCurrency = new List<NonzeroCraftingCurrencyFormula>();
    var itemCostEntryCount = 0;
    var emptyItemCostCount = 0;
    var repeatedResourceIdCount = 0;
    var maximumItemCostEntries = 0;
    for (var formulaIndex = 0; formulaIndex < formulaCount; formulaIndex++)
    {
        var formulaId = reader.ReadString();
        var outputItemId = reader.ReadInt32();
        var outputAmount = reader.ReadInt32();
        if (string.IsNullOrWhiteSpace(formulaId) || !formulaIds.Add(formulaId) || outputAmount <= 0)
            throw new ContractException($"Crafting formula {formulaIndex} has invalid id/result evidence.");

        var tagCount = reader.ReadBoundedCount("crafting tag", 1024);
        var tags = new List<string>(tagCount);
        for (var tagIndex = 0; tagIndex < tagCount; tagIndex++) tags.Add(reader.ReadString());

        var money = reader.ReadInt64();
        if (money < 0)
            throw new ContractException($"Crafting formula '{formulaId}' has negative Cost.money {money}.");
        var itemCount = reader.ReadBoundedCount("crafting item cost", 1024);
        itemCostEntryCount = checked(itemCostEntryCount + itemCount);
        if (itemCount == 0) emptyItemCostCount++;
        maximumItemCostEntries = Math.Max(maximumItemCostEntries, itemCount);
        var itemIds = new HashSet<int>();
        var itemCosts = new List<string>(itemCount);
        for (var itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            var itemId = reader.ReadInt32();
            var amount = reader.ReadInt64();
            if (amount <= 0)
                throw new ContractException($"Crafting formula '{formulaId}' item {itemId} has non-positive Cost.items amount {amount}.");
            if (!itemIds.Add(itemId)) repeatedResourceIdCount++;
            itemCosts.Add($"{itemId.ToString(System.Globalization.CultureInfo.InvariantCulture)} x {amount.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        }

        reader.ReadBoolean("unlockByDefault");
        reader.Align4();
        reader.ReadBoolean("lockInDemo");
        reader.Align4();
        _ = reader.ReadString();
        reader.ReadBoolean("hideInIndex");
        reader.Align4();

        if (money != 0)
        {
            nonzeroCurrency.Add(new NonzeroCraftingCurrencyFormula(
                formulaId,
                outputItemId,
                money,
                tags.Count == 0 ? "none" : string.Join("; ", tags),
                itemCosts.Count == 0 ? "none" : string.Join("; ", itemCosts)));
        }
    }

    return new CraftingFormulaAudit(
        formulaCount,
        checked(reader.Position - formulaDataStart),
        itemCostEntryCount,
        emptyItemCostCount,
        repeatedResourceIdCount,
        maximumItemCostEntries,
        nonzeroCurrency);
}

static void VerifyHarmonyReflectionContract(string harmonyPath)
{
    var assembly = Assembly.LoadFrom(harmonyPath);
    var harmonyType = assembly.GetType("HarmonyLib.Harmony", throwOnError: true)!;
    var harmonyMethodType = assembly.GetType("HarmonyLib.HarmonyMethod", throwOnError: true)!;
    var patchesType = assembly.GetType("HarmonyLib.Patches", throwOnError: true)!;
    var patchType = assembly.GetType("HarmonyLib.Patch", throwOnError: true)!;
    var patchInfoType = assembly.GetType("HarmonyLib.PatchInfo", throwOnError: true)!;
    var sharedStateType = assembly.GetType("HarmonyLib.HarmonySharedState", throwOnError: true)!;
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
    var patchStateField = sharedStateType.GetField("state", BindingFlags.Static | BindingFlags.NonPublic);
    var patchStateType = patchStateField?.FieldType;
    var patchStateArguments = patchStateType is { IsGenericType: true }
        ? patchStateType.GetGenericArguments()
        : [];
    var updatePatchInfoMethod = sharedStateType.GetMethod(
        "UpdatePatchInfo",
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
        binder: null,
        [typeof(MethodBase), typeof(MethodInfo), patchInfoType],
        modifiers: null);
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
        || priorityField?.FieldType != typeof(int)
        || patchStateType is not { IsGenericType: true }
        || patchStateType.GetGenericTypeDefinition() != typeof(Dictionary<,>)
        || patchStateArguments.Length != 2
        || patchStateArguments[0] != typeof(MethodBase)
        || patchStateArguments[1] != typeof(byte[])
        || patchStateField!.IsPublic
        || !patchStateField.IsStatic
        || !patchStateField.IsInitOnly
        || updatePatchInfoMethod?.ReturnType != typeof(void))
    {
        throw new ContractException("HarmonyLib reflection API required by UDS is missing or changed.");
    }
}

static MemberInfo? FindPublicInstanceMember(Type type, string name) =>
    type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
    ?? type.GetField(name, BindingFlags.Instance | BindingFlags.Public) as MemberInfo;

internal sealed record NonzeroCraftingCurrencyFormula(
    string FormulaId,
    int OutputItemId,
    long Money,
    string Tags,
    string ItemCosts);

internal sealed record CraftingFormulaAudit(
    int FormulaCount,
    int SerializedBytes,
    int ItemCostEntryCount,
    int EmptyItemCostCount,
    int RepeatedResourceIdCount,
    int MaximumItemCostEntries,
    IReadOnlyList<NonzeroCraftingCurrencyFormula> NonzeroCurrencyFormulas);

internal sealed class UnityAssetReader
{
    private readonly byte[] data;

    public UnityAssetReader(byte[] data, int position)
    {
        this.data = data;
        Position = position;
    }

    public int Position { get; private set; }

    public int ReadInt32()
    {
        EnsureAvailable(sizeof(int));
        var value = BitConverter.ToInt32(data, Position);
        Position += sizeof(int);
        return value;
    }

    public long ReadInt64()
    {
        EnsureAvailable(sizeof(long));
        var value = BitConverter.ToInt64(data, Position);
        Position += sizeof(long);
        return value;
    }

    public string ReadString()
    {
        var length = ReadInt32();
        if (length < 0 || length > 1_048_576)
            throw new ContractException($"Serialized Unity string length {length} is invalid at offset {Position - sizeof(int)}.");
        EnsureAvailable(length);
        var value = Encoding.UTF8.GetString(data, Position, length);
        Position += length;
        Align4();
        return value;
    }

    public int ReadBoundedCount(string label, int maximum)
    {
        var value = ReadInt32();
        if (value < 0 || value > maximum)
            throw new ContractException($"Serialized {label} count {value} is invalid at offset {Position - sizeof(int)}.");
        return value;
    }

    public void ReadBoolean(string label)
    {
        EnsureAvailable(1);
        var value = data[Position++];
        if (value > 1)
            throw new ContractException($"Serialized {label} boolean {value} is invalid at offset {Position - 1}.");
    }

    public void Align4()
    {
        Position = checked((Position + 3) & ~3);
        EnsureAvailable(0);
    }

    private void EnsureAvailable(int count)
    {
        if (count < 0 || Position < 0 || Position > data.Length - count)
            throw new ContractException($"Serialized Unity object exceeded resources.assets at offset {Position}.");
    }
}

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
        bool mustBePrivate = false,
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

            if (mustBePrivate && (method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Private)
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

    public void RequireField(
        string @namespace,
        string typeName,
        string fieldName,
        bool mustBePublic = false,
        bool mustBePrivate = false,
        bool mustBeFamily = false,
        bool mustBeStatic = false,
        string? fieldTypeFragment = null)
    {
        var type = reader.GetTypeDefinition(FindType(@namespace, typeName));
        foreach (var handle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(handle);
            if (string.Equals(reader.GetString(field.Name), fieldName, StringComparison.Ordinal)
                && (fieldTypeFragment == null
                    || field.DecodeSignature(typeProvider, reader).Contains(fieldTypeFragment, StringComparison.Ordinal))
                && (!mustBePublic
                    || (field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public)
                && (!mustBePrivate
                    || (field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Private)
                && (!mustBeFamily
                    || (field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Family)
                && (!mustBeStatic || (field.Attributes & FieldAttributes.Static) != 0))
            {
                return;
            }
        }

        throw new ContractException($"Required field not found: {@namespace}.{typeName}.{fieldName}.");
    }

    public void RequireNestedField(
        string @namespace,
        string typeName,
        string nestedTypeName,
        string fieldName,
        string fieldTypeFragment)
    {
        var declaringType = reader.GetTypeDefinition(FindType(@namespace, typeName));
        foreach (var nestedHandle in declaringType.GetNestedTypes())
        {
            var nested = reader.GetTypeDefinition(nestedHandle);
            if (!string.Equals(reader.GetString(nested.Name), nestedTypeName, StringComparison.Ordinal)) continue;
            foreach (var fieldHandle in nested.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                if (string.Equals(reader.GetString(field.Name), fieldName, StringComparison.Ordinal)
                    && (field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public
                    && field.DecodeSignature(typeProvider, reader).Contains(fieldTypeFragment, StringComparison.Ordinal))
                    return;
            }
        }
        throw new ContractException(
            $"Required nested field not found: {@namespace}.{typeName}.{nestedTypeName}.{fieldName}.");
    }

    public void RequireNestedProperty(
        string @namespace,
        string typeName,
        string nestedTypeName,
        string propertyName,
        string propertyTypeFragment,
        bool mustBePublic = false)
    {
        var declaringType = reader.GetTypeDefinition(FindType(@namespace, typeName));
        foreach (var nestedHandle in declaringType.GetNestedTypes())
        {
            var nested = reader.GetTypeDefinition(nestedHandle);
            if (!string.Equals(reader.GetString(nested.Name), nestedTypeName, StringComparison.Ordinal)) continue;
            foreach (var propertyHandle in nested.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                if (!string.Equals(reader.GetString(property.Name), propertyName, StringComparison.Ordinal)
                    || !property.DecodeSignature(typeProvider, reader).ReturnType.Contains(
                        propertyTypeFragment,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var accessors = property.GetAccessors();
                var accessorHandle = !accessors.Getter.IsNil ? accessors.Getter : accessors.Setter;
                if (accessorHandle.IsNil) continue;
                var accessor = reader.GetMethodDefinition(accessorHandle);
                if (mustBePublic
                    && (accessor.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                {
                    continue;
                }
                return;
            }
        }
        throw new ContractException(
            $"Required nested property not found: {@namespace}.{typeName}.{nestedTypeName}.{propertyName}.");
    }

    public void RequireDoubleConstant(string @namespace, string typeName, string fieldName, double expected)
    {
        var type = reader.GetTypeDefinition(FindType(@namespace, typeName));
        foreach (var handle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(handle);
            if (!string.Equals(reader.GetString(field.Name), fieldName, StringComparison.Ordinal)) continue;
            var constantHandle = field.GetDefaultValue();
            if (constantHandle.IsNil) break;
            var constant = reader.GetConstant(constantHandle);
            if (constant.TypeCode != ConstantTypeCode.Double) break;
            var blob = reader.GetBlobReader(constant.Value);
            if (blob.ReadDouble().Equals(expected)) return;
            break;
        }
        throw new ContractException($"Required double constant mismatch: {@namespace}.{typeName}.{fieldName}={expected}.");
    }

    public void RequireInt64Constant(string @namespace, string typeName, string fieldName, long expected)
    {
        var type = reader.GetTypeDefinition(FindType(@namespace, typeName));
        foreach (var handle in type.GetFields())
        {
            var field = reader.GetFieldDefinition(handle);
            if (!string.Equals(reader.GetString(field.Name), fieldName, StringComparison.Ordinal)) continue;
            var constantHandle = field.GetDefaultValue();
            if (constantHandle.IsNil) break;
            var constant = reader.GetConstant(constantHandle);
            if (constant.TypeCode != ConstantTypeCode.Int64) break;
            var blob = reader.GetBlobReader(constant.Value);
            if (blob.ReadInt64() == expected) return;
            break;
        }
        throw new ContractException($"Required Int64 constant mismatch: {@namespace}.{typeName}.{fieldName}={expected}.");
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

    public void RequirePublicStaticEvent(
        string @namespace,
        string typeName,
        string eventName,
        params string[] parameterTypeFragments)
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
                break;
            }

            var adder = reader.GetMethodDefinition(accessors.Adder);
            var remover = reader.GetMethodDefinition(accessors.Remover);
            var signature = adder.DecodeSignature(typeProvider, reader);
            var access = MethodAttributes.MemberAccessMask;
            if ((adder.Attributes & access) == MethodAttributes.Public
                && (remover.Attributes & access) == MethodAttributes.Public
                && (adder.Attributes & MethodAttributes.Static) != 0
                && (remover.Attributes & MethodAttributes.Static) != 0
                && signature.ParameterTypes.Length == 1
                && parameterTypeFragments.All(fragment => signature.ParameterTypes[0].Contains(fragment, StringComparison.Ordinal)))
            {
                return;
            }

            break;
        }

        throw new ContractException($"Required public static event not found: {@namespace}.{typeName}.{eventName}.");
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

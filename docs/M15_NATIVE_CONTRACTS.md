# M15 installed-native current-holdings contracts

This audit fixes the M15 implementation boundary to the locally installed Escape from Duckov baseline below. It is a versioned compatibility contract, not a claim about later game builds.

| Component | Verified value |
| --- | --- |
| Duckov | `2.3.30`, Steam build `24013657` |
| Unity | `2022.3.62f2` |
| `TeamSoda.Duckov.Core.dll` | SHA-256 `298d5d5885427632d5a94b2f3ce587f8ebc9528ec71e575a475158c326ecae8f` |
| `Assembly-CSharp.dll` | SHA-256 `e5f6f893763d3bfdd49c4c4e778f54562cab2ef237023c2c87c3806a8895e8cf` |
| `ItemStatsSystem.dll` | SHA-256 `a276e15c022f71b2214bd05e1b9b0f2e620c16561df576fed0b79c2fe4402e60` |
| HarmonyLib | `2.4.1.0`, SHA-256 `353daafec180bb8e7bbe4da78f2a7cdc78067392e3a4e79dc8e7af295f2371e6` |

The executable compatibility probe requires the `EconomyManager` singleton, private `Int64` field, public `Money`/`Cash`, nested save payload, load/generate/setup methods, `EconomyData` key check, the exact ItemUtilities ownership/count helpers, all main/storage/pet roots and loading surfaces, inventory content/change contracts, scene/level lifecycle gates, and ATM Save/Draw conversion methods. Manual decompilation establishes the method-body ordering and 1:1 conversion semantics that metadata alone cannot prove.

## Authoritative values and units

### Money

Confirmed: `Duckov.Economy.EconomyManager` owns a private `long money` field. Public static `Money` returns that field only while the Unity `EconomyManager.Instance` is live; it otherwise returns `0`, so the property result alone cannot distinguish an authoritative zero from a missing manager. `EconomyManager.SaveData.money` is a `long`. `GenerateSaveData()` writes the current Money value and `SetupSaveData(object)` hydrates the private field from the selected Duckov save.

`EconomyManager.Awake()` installs the singleton, subscribes to `SavesSystem.OnCollectSaveData` and `SavesSystem.OnSetFile`, and immediately calls its private `Load()`. `Load()` reads the `EconomyData` save key when present and invokes `OnEconomyManagerLoaded` after that read. `SavesSystem.SetFile(int)` first changes and caches `CurrentSlot`, then invokes `OnSetFile` synchronously. Money therefore becomes authoritative for a selected slot only after the matching economy load has completed and the matching UDS save generation is open. A missing `EconomyData` key does not overwrite the existing private field before `OnEconomyManagerLoaded`; M15 consequently does not treat that callback alone as proof for a newly selected empty save.

Confirmed mutation surface: the private Money setter captures the previous value, writes the new `long`, and invokes `OnMoneyChanged(oldValue, newValue)`. Native `Add(long)` and successful payment use that setter. A supported zero received while the manager and generation gates are valid is an authoritative zero.

### Cash

Confirmed: physical Cash is item type `EconomyManager.CashItemID == 451`. Public static `EconomyManager.Cash` delegates to `ItemUtilities.GetItemCount(451)`. `FindAllBelongsToPlayer` defines the owned roots as:

- top-level entries in `PlayerStorage.Inventory`;
- top-level entries in `LevelManager.Instance.MainCharacter.CharacterItem.Inventory`;
- top-level entries in `LevelManager.Instance.PetProxy.Inventory`.

The native helper concatenates those three collections and sums each matching item's `StackCount`. It does not recursively count nested item slots. Its accumulator and return type are `int`, even though `EconomyManager.Cash` exposes the result as `long`; the native helper is therefore not a checked large-total implementation. M15 preserves the installed root and item-identity definition while de-duplicating overlapping inventory/item references and summing non-negative stack counts with checked `long` arithmetic. Missing roots, a loading root, unreadable identity/count data, duplicate native roots that cannot be reconciled, or arithmetic failure cannot become zero. Enumeration accepts at most 16,384 top-level entries per root, matching the installed storage loader's explicit hydration capacity; exceeding that verified bound disables Cash rather than attempting an unbounded scan.

Confirmed comparability: Money and Cash use the same one-unit amount domain. `ATMPanel.Save(long amount)` consumes exactly `amount` units of item 451 and then calls `EconomyManager.Add(amount)`. `ATMPanel.Draw(long amount)` generates exactly `amount` units of item 451 and pays exactly `amount` Money. `EconomyManager.IsEnough` and its private payment path add Money and Cash directly when both are permitted. M15 may therefore expose `Liquid wealth` as the checked sum of current Money and current Cash. It is not item value, sale value, profit, secured raid Cash, or net flow.

## Hydration and live-observation boundaries

Confirmed main inventory boundary: `LevelManager.InitLevel` awaits `LoadOrCreateCharacterItemInstance()`, creates the main character from that hydrated character item, and then exposes `MainCharacter.CharacterItem.Inventory`. `CharacterMainControl.SetTeam(player)` subscribes that inventory's `onContentChanged` event and republishes it as `OnMainCharacterInventoryChangedEvent`. Stack-count changes invoke `Inventory.NotifyContentChanged`, so stack merges, splits, partial consumption, additions, and removals reach the same coalescing surface.

Confirmed storage boundary: `PlayerStorage.Start()` begins its asynchronous `Load()`. The storage sets `Loading = true`, loads save key `Inventory/PlayerStorage`, recalculates capacity, sets `Loading = false`, invokes `OnLoadingFinished`, and only then reports `HasInitialized() == true`. `PlayerStorage` registers as a `LevelManager` initialization dependency, so `LevelManager.OnLevelInitialized` cannot occur before storage initialization finishes. Its inventory `onContentChanged` callback republishes `OnPlayerStorageChange`.

Confirmed pet boundary: `PetProxy.Start()` starts `ItemSavesUtilities.LoadInventory("Inventory_Safe", inventory)` when `LevelConfig.SavePet` is true. That loader exposes `Inventory.Loading`, but `PetProxy` is not a `LevelManager` initialization dependency and has no completion callback. M15 may perform only an O(1) loading-state check during a pending hydration observation and must wait until the live pet inventory reports `Loading == false`. It subscribes directly to that exact inventory's `onContentChanged` event. At base initialization, `LevelManager` may call `PetProxy.DestroyItemInBase()` before the initialized events; the first authoritative Cash observation therefore occurs no earlier than the post-initialization boundary and after every required root is present and not loading.

Confirmed scene boundary: `LevelManager.OnLevelBeginInitializing`, `OnLevelInitialized`, and `OnAfterLevelInitialized`, plus `SceneLoader.onStartedLoadingScene`, `onFinishedLoadingScene`, and `onAfterSceneInitialize`, delimit destruction and replacement of the live character, storage, and pet roots. A scene-load start ends current Cash authority. A later observation becomes current only when `LevelManager.LevelInited` is true, the main/storage/pet roots are the live roots for that scene, and all loading gates are clear. Main-menu state has no authoritative owned-item root set; the matching generation may retain a labelled last observation, but it is not current.

The installed `SceneLoader.LoadScene` order saves the current level, sets `IsSceneLoading`, invokes `onStartedLoadingScene`, and only then loads the curtain scene in `Single` mode, which destroys the old inventory roots before the next `LevelManager.OnLevelBeginInitializing`. Live M15 qualification proved that the M9 flow adapter's older level-initialization-only suspension could observe PetProxy teardown as a false Cash outflow. M9 adapter `public-events-v12` therefore flushes legitimate pending Cash once and suspends its ownership baseline at `onStartedLoadingScene`, retaining `OnLevelBeginInitializing` as an idempotent fallback. This correction changes neither the M15 observation boundary nor the persisted holdings value.

## Save generation, recovery, reset, restart, and shutdown

Confirmed save selection: `SavesSystem.SetFile(int)` updates `CurrentSlot` before its synchronous `OnSetFile`. `EconomyManager` subscribes during its earlier native `Awake`, so it can load the newly selected Money and raise `OnEconomyManagerLoaded` before the later UDS coordinator subscriber runs. The coordinator therefore enqueues the save transition and immediately publishes a transition-ID-bearing holdings-only start notification before invoking any profile-transition flusher. Adapter `authoritative-roots-v2` idempotently enters profile-changing state at that notification, discards the dirty signal from the newly loaded Money, and preserves the prior generation's last authoritative value. The general queued `ProfileChanging` event retains its original timing for run lifecycle and other subscribers. A matching holdings completion removes that transition ID; only after every pending save-slot handoff completes does the adapter request fresh Money for the final open generation. Cash additionally waits for the replacement scene roots. Nested backup restoration or deferred transitions can produce overlapping IDs without briefly re-enabling observation between handoffs.

Confirmed save generation: `SavesSystem.OnCollectSaveData` is the native save-generation collection boundary. `EconomyManager` writes Money there, `PlayerStorage` writes only when it is not loading, `PetProxy` writes only in save-pet scenes, and `LevelManager` writes the main character before native scene/save calls. M15 flushes a coalesced authoritative observation and its deferred profile snapshot at save, export, transition, reset, deactivation, application quit, and repository-close barriers. It does not write Duckov save data.

Confirmed deletion/new game: `DeleteCurrentSave()` raises `OnSaveDeleted`; `LevelManager.Start()` raises `OnNewGameReport` for a save not yet marked reported. These are existing UDS generation-rotation boundaries. A value from the prior generation is never displayed for the new generation. A new current observation is required after native hydration.

Confirmed UDS reset: F8 rotates only the UDS generation and does not alter Duckov Money, Cash, inventories, or saves. The new schema-15 snapshot begins unavailable and is immediately eligible for a fresh observation from still-live authoritative roots. Reset must never manufacture zero holdings.

Confirmed restart/recovery: a persisted snapshot is not live native confirmation. On profile open, any valid matching-generation component is downgraded from `Current` to `LastObserved`; an absent, invalid, mismatched-generation, or pre-schema-15 component is `Unavailable`. Recovery never derives holdings from M9 inflow/outflow aggregates. Liquid wealth is recomputed only while both components have a live `Current` observation.

Confirmed shutdown: application quit and ordinary mod deactivation are terminal observation/persistence barriers. Current evidence is first coalesced when its native roots remain authoritative, then persisted; event subscriptions are removed and live state is no longer current. No inventory scan is permitted on ordinary frames. Frames may perform constant-time pending/root/loading checks, while an inventory-wide Cash scan occurs only after a trustworthy hydration/change/lifecycle request and is coalesced across same-frame native callbacks.

## Capability and failure boundary

Money, Cash, and liquid wealth are independent capabilities. Money requires the live singleton/property, load, change, save-selection, and generation contracts. Cash requires the exact three-root definition, hydration/loading state, item identity/count, and mutation contracts. Liquid wealth requires proven 1:1 native comparability and both component capabilities; its value is unavailable unless both component observations are current and their checked sum succeeds. A native incompatibility disables only the affected capability and makes that component unavailable rather than exposing evidence captured under a contract that is no longer trusted. Ordinary scene or restart freshness loss retains a valid matching-generation value only as `LastObserved`. Missing or stale evidence remains unavailable where no matching value exists; a proven zero remains zero.

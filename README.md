# Ultimate Duckov Statistics

Ultimate Duckov Statistics (UDS) is a local, single-player statistics mod for Escape From Duckov. Version 0.11.0 is the published M11 combat-ownership GitHub pre-release. It keeps M0-M10 intact while separating proven player kills from other deaths observed in the world.

The mod never modifies Duckov save files. Its data is stored under:

```text
%USERPROFILE%\AppData\LocalLow\TeamSoda\Duckov\UltimateDuckovStatistics\
```

## Status

> **Pre-1.0 support boundary:** Every UDS `0.x` build and GitHub pre-release is a development artifact for voluntary testing, not an officially distributed build or supported installation channel. Persisted-profile migration between `0.x` versions is best-effort development continuity rather than a supported upgrade guarantee; existing migration code and tests remain internal robustness evidence only through pre-1.0 development. M15 removes those historical `0.x` migration paths before the first v1 release candidate, so no `0.x`-to-v1 upgrade path will ship. The v1 package will support clean installation and reinstallation against its own current-format data; supported upgrade guarantees begin with the first version explicitly declared as officially distributed through a supported channel.

M0-M11 are published through the [v0.11.0 GitHub pre-release](https://github.com/bamboechop/ultimate-duckov-statistics/releases/tag/v0.11.0). M8.1 product commit `90384352d323e6ea19dfa607c7da18162dbcefcb` completed its performance, gameplay, package, projection, and shutdown gates and merged before M9; it was not released separately, so its accepted changes ship in v0.9.0.

M11 PR #12 merged as `875f53792b7dab7ac35a27d8957966ecc9e5c2be`. The merged release passes 704/704 tests, the installed-game compatibility and exact five-file package gates, progressed schema migration, player-versus-world-death gameplay, complete 24-file export agreement, recovery, and clean shutdown. The remaining planned path to v1.0 is M12 world-time and sleep statistics (`v0.12.0`), M13 crafted-item statistics (`v0.13.0`), M14 native UI overhaul (`v0.14.0`), and M15 feature-frozen `v1.0.0-rc.1` qualification. See [PLAN.md](PLAN.md) for the contracts and acceptance boundaries; immutable completed-release evidence remains in [TESTING.md](TESTING.md) and [RELEASE_NOTES.md](RELEASE_NOTES.md).

## Build prerequisites

- Windows
- .NET 8 SDK
- A local Escape From Duckov installation
- Steam Workshop item [HarmonyLib (2.4.1.0)](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839), installed and enabled
- `DUCKOV_PATH` set to the game root, for example `E:\SteamLibrary\steamapps\common\Escape from Duckov`

Game assemblies are referenced locally with copy-local disabled. They are never committed, downloaded by CI, or included in release packages. UDS discovers the separately installed HarmonyLib at runtime. Healing, M5 combat attribution, and M7 corpse provenance each use distinct, minimal, version-checked patch owners; a missing/incompatible contract or unsafe foreign patch disables the affected metrics. Failed UDS unpatch cleanup retains the exact owner, is retried, and blocks unsafe same-process replacement. UDS never bundles `0Harmony.dll`.

> **Activation check:** Open **Mods** after a cold launch and confirm both HarmonyLib and UDS are active before selecting a save. See [INSTALL.md](INSTALL.md#required-harmonylib-workshop-item).

UDS fingerprints saves read-only and never modifies them. While active, it records a short-lived pre-save intent in its own external profile from Duckov's public save-collection event, so a normal save completed immediately before a crash or same-process re-selection of the current slot can retain the same UDS generation. If a save changes while UDS is inactive, that proof is unavailable and UDS conservatively archives the prior statistics generation rather than risk merging a reused slot.

Runs begin only when the native raid is initialized and the live main duck actually has player control. Active duration excludes pause and loading. Movement is sampled at approximately 5 Hz with the real monotonic sample interval and verified native walk/run/dash speeds. Explicit-position, implausible, and long-gap displacement inside an active map is retained as teleport distance; displacement caused solely by loading or a map boundary is retained independently as transition-excluded distance and never inflates physical or teleport totals. Interrupted and integrity-flagged runs stay visible but do not enter default duration records; each Runs row shows its integrity tag and an explicit eligible/excluded Records status with the exclusion reason.

Version 0.8.1 keeps one expedition active across proven full-scene and subscene transitions. It stores starting/ending maps, an ordered stable-ID route, distinct repeated visits, segment duration and movement, and source/outcome segment associations for delayed healing/combat. Loading displacement is separate from physical and proven teleport distance. Complete-run records remain overall and by starting map; route-aware per-map totals are built only from segments. Pre-M8 route history remains explicitly unavailable.

M9 treats account-style `Money` and physical Cash item type `451` as different currencies. Public `EconomyManager.OnMoneyChanged` proves exact post-mutation Money amount and direction without treating load initialization as activity. Completed StockShop sales and `QuestReward_Money` claims can enrich a matching exact delta; a StockShop callback first reconciles the already-completed physical-Cash return, so an unrelated same-amount Money inflow cannot steal Cash-sale attribution. All ambiguous or otherwise unmatched wallet changes remain visible as `UnknownAdjustment` rather than being guessed from sign, price, or UI state. Physical Cash uses event-coalesced, runtime-identity-deduplicated owned totals across storage, main inventory, and pet inventory. Full-scene inventory hydration is suspended until level initialization completes and then becomes a non-economic baseline; carried-in/load-time Cash and stack split/merge/internal movement do not become flows. Completed `Cost.items` Cash is subtracted from a coalesced decrease before retaining player-drop magnitude, so a simultaneous cost cannot erase drop/re-pickup evidence. Successful exact-main-character world-pickup callbacks can become raid acquisition only with a matching positive owned-total delta and bounded drop/re-pickup exclusion. The common corpse/container loot transfer does not emit that callback in the tested build, so its exact Cash delta remains `UnknownAdjustment` and is not fabricated as acquired. The release intentionally reports terminal Cash disposition as unavailable: the installed public contract cannot prove secured versus lost units after fungible inventory mixing, so any proven acquired amounts become `Unresolved` at extraction, death, or interruption instead of fabricated secured/lost values.

Economy idempotency uses constant-size activation/sequence replay cursors instead of retaining one identity per transaction. The verified native adapter creates flows synchronously under one random activation ID with strictly increasing positive sequence numbers; each directly recording aggregate stores only that activation and its closed-through sequence. A newly registered activation is saved synchronously at closed-through sequence zero before registration returns, including after slot selection, reset, deletion, or new-game rotation. Lifetime, run, segment, starting-map, and route-map totals therefore continue exactly beyond 2,048 flows without a transaction-count hard stop or an ever-growing ID list. Current-schema recovery also rejects any supported unsaturated run/segment or completed/start-map/route-map economy fan-out mismatch before a corrupt primary can defeat an intact backup. Schema-9 `RecentEventIds` and `DeduplicationSaturated` remain legacy-candidate fields only: they are validated through recovery and compacted after no surviving checkpoint can replay them. An old saturated candidate keeps its exact recorded totals and is marked `LegacyIdentitySaturationIncomplete`; current capture resumes under a fresh activation. JSON exposes the replay cursor and legacy-incomplete marker, while economy CSVs expose `legacy_identity_saturation_incomplete`.

Schema 10 replaces the M8 raw association journal for new events with exact buckets keyed by event family plus source/outcome segment. Each accepted shot, combat outcome, item use, healing outcome, or unique-container access increments a checked 64-bit count and retains exact endpoint maps plus first/last UTC evidence; durable state is bounded by five event families and the legitimate 64-segment route cardinality rather than event volume. Schema-9 raw rows remain finite `LegacyRaw` evidence. Unsaturated history migrates exactly; a previously saturated run remains explicitly incomplete while the separate current-capture capability reports that schema-10 capture is available. JSON and the append-only `routes.csv`, `segments.csv`, and `segment_events.csv` columns distinguish exact aggregates, historical incompleteness, and current capture.

Schema 11 makes the primary combat outcome **Kills by you**. It increments only for a fatal enemy transition whose credited, physical, and damage actors consistently prove the main duck, including the verified native controlling-character owner chain. Fatal transitions owned by `Companion`, `Other NPC`, an explicitly actorless `Environmental` zone, or conflicting/missing `Unknown` evidence remain separate **Observed world deaths**. Weapon identity never supplies actor identity. Projectile ownership is refreshed at impact so reflection is not frozen to launch-time attribution; melee, explosion, buff, and delayed damage scopes carry their native actor evidence. Losing trusted effect-scope observation makes every otherwise unscoped health transition `Unknown`, including native buff-driven explosions that do not set Duckov's buff/effect marker. Non-player world deaths cannot create player equipment-kill credit. Disabled combat totals render as Unsupported in every panel detail, while each affected flattened CSV row carries the corresponding capability state beside its retained partial total. Schema-10 combat rows retain provable player/companion subsets and expose the rest as `LegacyUnclassifiedDeaths` with historical provenance rather than silently relabelling old ambiguity.

If adding an exact flow would exceed the signed 64-bit aggregate range, M9 preserves the prior exact value, marks only that currency `arithmetic_saturated`, and disables further capture for that currency rather than clamping the event into an apparently exact total. Money saturation does not disable Cash, and vice versa.

M4 uses the public `ItemAgent_Gun.OnMainCharacterShootEvent` from the verified Duckov build. Each accepted callback receives a unique UDS event ID and proves one firing action plus event-time weapon/ammunition identity. The event occurs after calls that may conditionally skip ammunition consumption or projectile initialization, so loaded-ammunition and projectile counts are explicitly unavailable rather than inferred from cached ammunition or configured `ShotCount`. Reloads, magazine transfers, inventory movement, base activity, loading, pause, non-main-duck actors, and dry fire do not create firing-action records.

M5/M11 measure `Health.Hurt` before/after HP and therefore exclude rejected damage and overkill. A reliable ranged hit is one completed proven-player projectile that caused positive actual enemy HP loss; penetration or repeated damage from that projectile cannot inflate the numerator, while each separately fatal target may still count as a kill by you. Accuracy uses those hits over completed player projectiles, not M4 firing actions. Critical hits never imply headshots.

M6 observes the exact main duck's public character-slot tree, ordinary inventory, and native slot/hold/inventory callbacks. Persisted identities use stable `Item.TypeID`, slot keys, and deterministic attachment signatures; runtime object IDs and localized names never determine identity. Durations use the same monotonic active-raid clock as M3 and therefore exclude pause/loading. Direct slotted totems with usable durability are proven active by the verified item-effect control flow. A totem plugged into the public `AnyThing` slot of a built-in Tote Bag (`Item.TypeID` 1255) carried in top-level ordinary character inventory is recorded as present with activation `Unknown`: tote activation is not inferred and the capability remains `DisabledIncompatible`. Equipment/combat rows are event-time temporal associations, not proof that an item or totem caused an outcome. Only loadouts observed in at least two completed runs enter lifetime recurring-loadout rankings; every run retains its own summary.

M7 observes public `InteractableLootbox.OnStartLoot`, after the interaction timer and inventory checks have succeeded. It requires the event-time interaction owner to be the exact main duck, reads the native private `GetKey()` contract reflectively for per-run deduplication, and excludes native enemy corpses plus persisted/player tombs using a separate version-checked corpse-provenance patch. Reopening a container in the same run does not increment; the same stable key may count again in another run. Proximity, attempts, locked/cancelled/failed interactions, corpses, base activity, item transfers, and loot value do not count.

The in-game panel enables Overview, Runs, Records, Combat, Equipment, Items, Economy, and Diagnostics. Runs shows a compact route with expandable segment evidence. One export action writes `statistics.json` plus twenty-three flattened CSV files. M9 adds `economy_totals.csv`, `economy_sources.csv`, `economy_contexts.csv`, and `cash_raid_outcomes.csv`; the existing nineteen CSV contracts remain unchanged.

## Development commands

```powershell
$env:DUCKOV_PATH = 'E:\SteamLibrary\steamapps\common\Escape from Duckov'
dotnet restore .\UltimateDuckovStatistics.sln
dotnet test .\tests\UltimateDuckovStatistics.Tests\UltimateDuckovStatistics.Tests.csproj -c Release --no-restore
dotnet build .\src\UltimateDuckovStatistics\UltimateDuckovStatistics.csproj -c Release --no-restore
```

Create the validated installable ZIP and SHA-256 sidecar with:

```powershell
.\scripts\create-release.ps1 -DuckovPath $env:DUCKOV_PATH -Version 0.11.0
```

See [INSTALL.md](INSTALL.md) for installation and compatibility details.

## License

MIT. Copyright 2026 Ultimate Duckov Statistics contributors.

# Remaining M17 tabs: local delivery evidence

For the subsequent screenshot-driven fixes, see [M17 screenshot corrections](M17_SCREENSHOT_CORRECTIONS.md). The artifact facts below describe the original delivery revision.

On 2026-09-07, implementation revision `d0db13ef754bac0034c8367dee983d9ba3822317` was built, packaged and transactionally deployed to the local Duckov installation. This is a voluntary pre-1.0 development artifact, not an official supported distribution or a gameplay/visual acceptance result. No push, merge or release publication was performed.

## Delivered scope

- [Economy](M17_RETAINED_ECONOMY.md): independently current/last-observed/unavailable holdings, conditional liquid wealth, separate Money/Cash flows with sources and contexts, subordinate proven raid Cash acquisition, recent-run expansion and exact Runs navigation.
- [Crafting](M17_RETAINED_CRAFTING.md): successful-action output rankings, separate produced units, exact consumed-resource rankings and reciprocal recorded relationships.
- [Item Use](M17_RETAINED_ITEM_USE.md): raid-only uses, consumption and actual HP, all group filters/effects, expanded items, recent-run details and exact navigation, throwable distinctions and the accepted empty state.
- [Diagnostics](M17_RETAINED_DIAGNOSTICS.md): settings/hotkey, last successful save, actionable issues, recovery and bounded log, actual native capability groups/contracts, menu fallback, blocking Cancel-first reset confirmation and asynchronous single-flight export with truthful clipboard/results.

All nine tabs are mounted through the shipped retained panel and shell. New rendering uses existing recorded data and native controls; it adds no persisted statistic, schema change or tracking hook. Storage changes address the proven export snapshot sharing and reset failure settlement defects. Existing unsupported native metrics remain unavailable rather than zero. Reciprocal crafting produced quantity remains unavailable where the recorded association cannot prove it.

## Automated and source verification

| Gate | Verified result |
| --- | --- |
| Economy focused Debug | 34/34 passed |
| Crafting focused Debug | 34/34 passed |
| Item Use focused Debug | 42/42 passed after the reviewed route/header correction |
| Diagnostics/controller focused Debug | 82/82 passed |
| Reset/export service focused Debug | 15/15 passed, including 10 new isolated safety tests and 5 existing composition tests |
| Complete Debug suite | 1823/1823 passed; zero failures/skips |
| Complete Release suite | 1823/1823 passed; zero failures/skips |
| Installed native build | Final Release succeeded with zero warnings/errors |
| Installed compatibility probe | Passed: Duckov 2.3.30, Steam build 24013657, Unity 2022.3.62f2, HarmonyLib 2.4.1.0 |
| Frame-time analyzer build | Release succeeded with zero warnings/errors; no new runtime performance qualification claimed |
| Package/ZIP inventory | Exactly the five permitted files; no game assets/assemblies, Harmony or framework dependencies |
| ZIP content verification | Every entry hash equals its package file hash |
| Local deployment | Established transactional workflow succeeded after checking Duckov was closed; every installed hash equals the package/ZIP entry hash |

The full Debug suite was rerun after the final Item Use correction; the Release workflow ran from the implementation commit. Independent frozen reviews covered each new tab, the native input/lifecycle composition and the corrected reset/export services. Confirmed findings were fixed and their bounded corrections checked. The final source hash manifest matched all 47 committed implementation/documentation/test files after build and deployment. No review finding remained open at delivery.

Operation tests use temporary fixtures. They cover detached exports across later gameplay/profile mutation, safe reset failures without later automatic rotation, staged crafting and live holdings preservation, exact transition tokens and postcommit retry. Native input and screenshots remain manual checks. No real user profile reset, save edit, gameplay action, game launch or termination was performed.

## Artifact and deployed files

Local archive: `artifacts/release/UltimateDuckovStatistics-v0.17.0.zip`, **658922 bytes**.

ZIP SHA-256: `7beb9a8c88928cfc6d784c6d9ecaff7831b56b0823224af6a8e8dd2eeb9a90de`.

Deployment destination: `E:\SteamLibrary\steamapps\common\Escape from Duckov\Duckov_Data\Mods\UltimateDuckovStatistics`.

| File | Bytes | Package/ZIP/deployed SHA-256 |
| --- | ---: | --- |
| info.ini | 339 | `ca27d89a668246851dea12c8e9bce498a5e5cd82ef214a91f67086f01ac4753c` |
| INSTALL.md | 24026 | `4567b84900b7651f3885ee4b42e3360b6125d9b2b5f313ae675bf53bab8254fd` |
| LICENSE | 1117 | `0f7558f2469ad0901074f6c380ada1ed91861d55adf905267bc70b26cd2e3ccc` |
| UltimateDuckovStatistics.Core.dll | 736256 | `4fb27df297d06dae80000b7d5ebdf58a62597056b7e875c50cd23acfec8cb8d8` |
| UltimateDuckovStatistics.dll | 1142272 | `7572ea18c6227fefbd4e63b97577350659faf900746aedc778fad05c25a99c27` |

Ignored evidence is under `artifacts/m17-remaining-tabs/`: `full-debug.log`, its TRX, `release-workflow.log`, `deployment.log`, package/deployed hash manifests, the acceptance checklist and frozen reviewed patch/source hashes. Review snapshots also retain the exact corrected scopes.

## Minimum user-controlled acceptance

Manual visual, gameplay, input and audio acceptance was pending at delivery. Follow [the full protocol](M17_MANUAL_VALIDATION.md) as needed; the minimum pass is:

1. Open all four tabs at desktop and 1024×768 with a representative UI scale and long/localized names. Expand rows, scroll both columns to their true ends, verify cues and native icons/sounds, and repeat close/reopen and tab switching.
2. Compare one ordinary transaction, multi-unit craft, raid consumable and throwable with recorded profile/export evidence. Check distinct units, current versus stale holdings, reciprocal resources, filters and exact View run links.
3. Inspect Diagnostics groups/contracts/log filters and hotkey change/cancel. Open reset confirmation, verify Cancel-first focus, Escape and blocked background controls. A real reset remains an optional deliberate user action.
4. If exercising export, verify the 37-file JSON/CSV bundle and actual clipboard path, duplicate-input rejection and completion feedback. Use isolated test evidence for destructive or otherwise unsafe-to-create failure states.

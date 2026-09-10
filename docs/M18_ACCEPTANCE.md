# M18 acceptance record

M18 hardens the completed M17 feature baseline, starting at `443fad6a268916a6038c919501a714d1f17da371`. The first RC has not been published or fully accepted. **M18 remains incomplete only for the specific native observations, coverage decisions and publication steps below.** Live branch, PR and CI state belongs on [GitHub](https://github.com/bamboechop/ultimate-duckov-statistics/pull/19), not in this durable record.

The [item 2 closeout](M18_ITEM2_CLOSEOUT.json) records the latest exact source, complete validation, independent review, ordinary and diagnostic artifacts, deployment readback and user-data preservation. It supersedes older candidate identities as the current handoff. The [remaining manual procedure](M18_MANUAL_QUALIFICATION.md) contains only actions that require the user.

## Acceptance summary

| Requirement | Evidence | Assessment |
| --- | --- | --- |
| Supported native baseline | Duckov 2.3.30 / Steam 24013657 / Unity 2022.3.62f2 / Harmony 2.4.1.0; installed probe and assembly identities in item 2 closeout | Verified installed contracts |
| Removal and v1 baseline | [Removal inventory](M18_REMOVAL_INVENTORY.md); separate `uds-profile-v1` namespace; unsupported predecessor migrations, impossible adapters, immediate renderer and production visual-acceptance gates removed | Complete; supported metrics and reachable safety behavior retained |
| Current-format persistence/recovery | Production repository/coordinator/lifecycle tests, isolated real file-lock and invalid-file cases; [native reinstall](M18_REINSTALLATION_QUALIFICATION.json), [backup recovery](M18_SLOT3_BACKUP_RECOVERY_QUALIFICATION.json), [reset](M18_SLOT3_RESET_QUALIFICATION.json), [save isolation](M18_SAVE_ISOLATION_QUALIFICATION.json) | Automated production composition and stated native cases pass. Forced termination, real-file corruption and separate live tutorial death are unobserved, not additional required destructive tests |
| Large-history correctness | Item 2 production persistence/projection/export test; existing 144,000-event workload with 0/200 prior runs and exact six-segment reopen | Bounded managed correctness. There is no fixed retained-run maximum; native unlimited-history cost is not claimed |
| Tutorial and ordinary raid | [Tutorial extraction](M18_TUTORIAL_NATIVE_QUALIFICATION.json), [regular raid/healing](M18_REGULAR_RAID_QUALIFICATION.json), [multi-map/revisit](M18_MULTIMAP_QUALIFICATION.json), [death](M18_DEATH_QUALIFICATION.json), [grenade](M18_GRENADE_QUALIFICATION.json) and [burning/switch kill](M18_BURNING_SWITCH_KILL_QUALIFICATION.json) | Recorded routes, outcomes, attribution, profile/JSON/CSV and observed UI agree; historical failures remain preserved |
| Economy, crafting, sleep | [Paid craft](M18_PAID_CRAFT_QUALIFICATION.json), [shop sale](M18_SHOP_SALE_QUALIFICATION.json), [base actions](M18_BASE_ACTIONS_QUALIFICATION.json), [craft/ATM controls](M18_BASE_PERFORMANCE_QUALIFICATION.json), [direct quit](M18_DIRECT_QUIT_NATIVE_QUALIFICATION.json) | Completed native observations and data agreement; supported Unknown/Unavailable/partial states preserved |
| Language, layout and input | [Native names](M18_NATIVE_NAME_QUALIFICATION.json), [shortcut isolation](M18_SHORTCUT_QUALIFICATION.json), [World time layout](M18_WORLD_TIME_NATIVE_QUALIFICATION.json), [Diagnostics width](M18_DIAGNOSTICS_WIDTH_NATIVE_QUALIFICATION.json); real modal tests and installed input-asset audit in item 2 | Supported keyboard/mouse paths qualified within stated bounds. Installed controls contain no Gamepad/Joystick bindings; no controller-only remainder |
| Degradation | [Missing Harmony](M18_MISSING_HARMONY_REPEAT_QUALIFICATION.json), [restoration](M18_HARMONY_RESTORATION_QUALIFICATION.json), production foreign-hook drift regression in item 2 | Actionable diagnostics and supported siblings preserved. No arbitrary conflicting mod installation required |
| UI ownership | Native ownership audit and linked-production regressions in item 2; UDS-created totem shadow mesh now released on destruction | Source/isolated lifecycle verified. Actual Unity deferred reclamation remains one prepared native check |
| Build and release artifacts | Item 2 exact Debug/Release totals, warning-free builds, installed probe, formatting/analyzers, ordinary IL/privacy audit, extraction, reproducibility and deployment evidence | Latest immutable results recorded in closeout |
| Reviews | Prior complete M18 integration review through `4068480`, subsequent correction/evidence reviews, and latest immutable item 2 review | Independent code/native composition review; not a substitute for actual Unity resource measurement |

## Performance decision and preserved measurements

The user explicitly [accepted all observed performance deviations](M18_PERFORMANCE_ACCEPTANCE.json) because no hindrance to gameplay was noticed. Valid unfavorable frames, numerical failures, thresholds, invalid-attempt records and control limitations remain unchanged. An accepted numerical miss is not rewritten as a numerical pass. No new captures or speculative optimization are scheduled solely to meet the old 5% median / 10% p99 targets.

The ordinary measured source was `88efddb5eacdab40a40498b62f90fdde14d167af`. Item 2 correctness/resource corrections require their affected native observation, not a repetition of unaffected gameplay or the full frame-time matrix.

| Workload | Recorded result and limits |
| --- | --- |
| Raid idle | [Fixed-D/current-control reconciliation](M18_IDLE_CONTROL_RECONCILIATION.json): +2.32632% median / +1.74807% p99, 46,501 retained frames; earlier missed comparisons preserved |
| SR-3M empty / regular duck | [Empty](M18_HIGH_RATE_EMPTY_QUALIFICATION.json) misses engineering targets, weaker ceiling passes; [single target](M18_HIGH_RATE_SINGLE_QUALIFICATION.json) passes bounded numerical gates, including retained isolated 511.189 ms frame |
| MMG empty / regular duck | [Empty](M18_SECOND_AUTO_EMPTY_QUALIFICATION.json) and [single target](M18_SECOND_AUTO_SINGLE_QUALIFICATION.json) pass bounded rules; invalid gameplay and encounter/loadout corrections preserved |
| M700 / extraction | [Slower firing](M18_SLOWER_CONTROL_QUALIFICATION.json) and [extraction](M18_TERMINAL_BOUNDARY_QUALIFICATION.json) pass numerical rules; extraction ends on Evakuiert, excluding Weiter/base loading; all spikes retained |
| Med-Kit (S) | [Healing](M18_MEDKIT_QUALIFICATION.json): three candidate uses / 36 HP, bounded numerical rules pass |
| Base idle / inventory / crafting / export | [Four comparisons](M18_BASE_PERFORMANCE_QUALIFICATION.json): inventory p99 and export median engineering misses; other stated gates pass; paid craft and ATM profile/export agreement verified |
| Paused UI | [Original comparison](M18_BASE_UI_QUALIFICATION.json): whole +18.47977%/+23.10336%, action +21.59640%/+36.28332%, engineering and ceiling misses; [bounded diagnostic](M18_UI_TIMING_RESULT.json) does not justify speculative correction |
| Natural multi-map soak | [188,332-frame observation](M18_NATURAL_SOAK_QUALIFICATION.json): five map visits, 470 shots, 40 kills, 20 uses, Died; elapsed p99 +20.77437% misses 20% bound. Different scenes and the native death hitch prevent a UDS-only causal claim |
| Progressed UI/export | [Six new candidate captures](M18_PROGRESSED_UI_EXPORT_QUALIFICATION.json): five runs, 1,337,253-byte profile; UI action p99 +22.42466% against historical reference; nearby export frames 475–506 ms. Three 37-file exports agree with production projections. Static paused UI limits subjective hitch detection; no noticeable disruption reported |
| Controlled multi-target | [Feasibility](M18_MULTI_TARGET_FEASIBILITY.json): Not exercised because repeatable paired encounters were unavailable. Soak observations do not silently replace the matched cell |

The 144,000-event managed workloads preserve exact totals through persistence and reopen; their event processing, allocations and file writes are diagnostic evidence, not native frame-time acceptance. Serialization grows with history. Native resource retention and arbitrarily large histories cannot be inferred from process-memory snapshots taken after later gameplay.

## What still needs the user

Remote delivery is approval-blocked: the final commits and prepared PR update are saved locally. Automatic approval review requires explicit authorization for the exact final push to `bamboechop/ultimate-duckov-statistics`, branch `codex/m18-release-hardening`. Local validation and deployment are complete; the final push/PR update and subsequent CI readback depend on that approval.

1. Run the prepared short totem/resource session, then close Duckov. Codex will verify deferred mesh cleanup and restore the already-built ordinary package. One ordinary cold activation/export then confirms the replacement.
2. Decide whether to accept the explicit unexercised controlled multi-target case and bounded-history coverage for this release. The observed-performance acceptance does not silently waive either limitation.
3. Approve eventual merge/tag/release/Workshop actions separately. The [release procedure](RELEASE_PROCESS.md), [Workshop description](WORKSHOP_DESCRIPTION.md) and [local data guide](LOCAL_DATA.md) are prepared; preview approval and subscription-install verification remain user-controlled.

No tutorial replay, deliberate profile corruption, forced termination, new farming, controller setup or repeated completed performance batch is requested. Deferred text/layout polish and unrelated PLAN follow-ups remain separate. The older chronological acceptance records are preserved in Git history and the individual immutable evidence files; obsolete instructions in those records are not the current work queue.

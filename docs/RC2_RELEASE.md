# RC2 release record

RC2 combines the four post-M18 implementation batches with the accepted M18 baseline. This record describes delivered scope and user acceptance; [GitHub releases](https://github.com/bamboechop/ultimate-duckov-statistics/releases) and [Actions](https://github.com/bamboechop/ultimate-duckov-statistics/actions) are authoritative for publication and CI state.

## Merged scope

| Batch | Delivery | Merge commit |
| --- | --- | --- |
| UI fixes | [PR #20](https://github.com/bamboechop/ultimate-duckov-statistics/pull/20), [behavior and evidence](RC2_UI_FIXES.md) | `e8f59b9a07d4c02ea0344035a5b5c1b964b27d4c` |
| Combat statistics | [PR #21](https://github.com/bamboechop/ultimate-duckov-statistics/pull/21), [definitions and evidence](RC2_COMBAT_STATISTICS.md) | `48428b32cf0108d59bd73c1a981a7acdc2078f68` |
| About and community invitation | [PR #22](https://github.com/bamboechop/ultimate-duckov-statistics/pull/22), [content and evidence](RC2_ABOUT_COMMUNITY.md) | `68623d4f32190b7c73f1368b10d3ee3f8b73cb09` |
| Opacity and base distance | [PR #23](https://github.com/bamboechop/ultimate-duckov-statistics/pull/23), [contracts and behavior](RC2_OPACITY_BASE_DISTANCE.md), [implementation package receipt](RC2_OPACITY_DELIVERY.json) | `413cbcebbab98e06b13acde31a7604850745c38e` |

All four merges occurred on 2026-09-10. The final implementation package source is `3c06fd3216b8a15f0f7e450003120eab1f765536`; subsequent batch delivery documentation and the merge preserve that runtime implementation. The combined release changes its version identity to `1.0.0-rc.2` and updates documentation. It does not add gameplay behavior beyond the reviewed batches.

Changes include smaller centered empty states, hidden empty tables, whole-number restored-HP display, export feedback dismissal, spacing and label fixes, stable totem shading and native ammunition appearance. Combat adds visible effect-kill and overall/ranged/melee accuracy breakdowns, selected-weapon firing-action accuracy and owned tooltips. About precedes Diagnostics and provides the approved author, support, Discord and translation invitation. The shared dimmer is 85%, with 75% inner panels/dark rows. Overview and exports distinguish recorded raid/base distance and their combined total; base distance persists separately with collection-start and partial-coverage evidence. No blur or translation service is included.

The translation invitation and optional caption are prepared in [the Workshop description](WORKSHOP_DESCRIPTION.md). Publishing the description and choosing a preview remain Workshop release tasks; no promo image or hosted translation tool is required to close implementation.

## User acceptance

On 2026-09-11 the user confirmed that the final in-game checks for the merged changes all passed, explicitly including About links, opacity/readability, and base -> raid -> base distance with export and restart persistence. This is user-reported native acceptance, not an automated capture or new numerical frame-time measurement. It supersedes pending manual-check wording in the individual batch delivery records.

The [M18 performance and coverage decisions](M18_ACCEPTANCE.md) remain accepted for unchanged behavior. Recorded numerical misses remain misses; controlled multi-target firing remains unexercised, and native history coverage remains bounded. Do not claim a repeated performance campaign, new controller qualification or unlimited-history performance from the user's confirmation.

## Exact release identity

[v1.0.0-rc.1](https://github.com/bamboechop/ultimate-duckov-statistics/releases/tag/v1.0.0-rc.1) was published as a GitHub pre-release on 2026-09-10 from `58591d5a397abd484284291fd95ab92d949f46fb`. RC2 gets a new tag and new assets; do not replace RC1's assets or reuse the earlier batch ZIPs, which still report `rc.1`.

The RC2 version is declared consistently in build properties, `ProductInfo`, `mod/info.ini`, the release-script default and the packaged install guide. Profile format `uds-profile-v1`, data directory `v1` and schema identity stay unchanged. There is no old-build migration or downgrade machinery.

Follow [the release procedure](RELEASE_PROCESS.md) to freeze the exact release commit, run final tests/native builds, audit the ordinary artifacts, reproduce DLL/PDB/ZIP bytes from two clean roots, independently extract the payload and record hashes. The local release manifest and prepared release body identify that exact source, validation and assets. Evidence files and PDBs are outside the installable five-file package. Tag/release publication and post-publication download verification remain separate operations.

## Published RC2 verification

[v1.0.0-rc.2](https://github.com/bamboechop/ultimate-duckov-statistics/releases/tag/v1.0.0-rc.2) was published as a non-draft GitHub pre-release at 2026-09-10 22:22:51 UTC (2026-09-11 in Europe/Vienna). Its lightweight tag resolves exactly to `f6cd8840b2cdbaface9ad3aeaf99efc9770b9021`.

Independent downloads of the ZIP and checksum sidecar matched the qualified local assets byte-for-byte. The ZIP is 647,365 bytes with SHA-256 `51376393c9d57d1902b3054c35c77aa387ae745b405156af9d465ab4bb6e8ec3`. The sidecar names that same archive and hash. Independent extraction verified exactly the five required files and matched each length, digest and byte sequence against the qualified package. The [publication receipt](RC2_PUBLICATION.json) records all identities and the verification timestamp.

The release source passed 2,020 main, 67 ordinary shell and 71 diagnostic shell tests in each Debug/Release configuration, native Debug/Release builds, installed contracts, changed-source formatting/analyzers, source safety, ordinary artifact audits, two-root DLL/PDB/ZIP reproducibility and independent extraction. [Exact-source CI](https://github.com/bamboechop/ultimate-duckov-statistics/actions/runs/34536377349) completed successfully. The later publication-record commit changes documentation only and does not move the tag or replace assets. Final v1.0 promotion and Workshop publication remain separate user decisions.

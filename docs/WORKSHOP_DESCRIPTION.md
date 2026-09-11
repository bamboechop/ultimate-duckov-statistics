# Workshop description for 1.0.0

This is the prepared description for the first 1.0.0 listing; no Workshop item has been created. M18 implementation and bounded native qualification are recorded in [M18 acceptance](M18_ACCEPTANCE.md), with follow-ups in the [release notes](../RELEASE_NOTES.md). Complete the [release procedure](RELEASE_PROCESS.md), choose a preview from the current native UI, and verify subscription installation before declaring this a supported distribution channel. Publish only the description between the separators below; the preparation notes and optional promo caption are not part of the item description.

---

**Ultimate Duckov Statistics** keeps local, per-save statistics for Escape from Duckov: raids and routes, combat, equipment, item use and healing, economy, crafting, world time and sleep.

Open Statistics outside a raid from the main menu, base pause menu or F8. Ten tabs cover your statistics, recorded expeditions and About. About introduces UDS and its author, and invites community translation help. Diagnostics explains unavailable or partial measurements and exports a JSON snapshot with CSV tables. Resetting UDS statistics archives its own generation; it does not change Duckov saves.

**Languages:** English and German. UDS follows the game's language, including changes while the statistics panel is open.

**Dependency:** install and enable [HarmonyLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839) separately. UDS does not bundle Harmony or game assemblies. The qualification baseline is Windows, Duckov 2.3.30 / Steam build 24013657, Unity 2022.3.62f2 and Harmony 2.4.1.0. Later game updates or conflicting patches may disable affected statistics while independently supported metrics continue.

**Privacy:** no telemetry, account or network service. Profiles, recovery files and exports remain on your computer. UDS does not open external links. Exports can reveal gameplay history, timestamps and save-slot metadata; inspect them before sharing.

**Measurement limits:** UDS records evidence the game exposes. It does not infer rejected trigger attempts, ammunition consumption from firing callbacks, tote-effect activation, crafting workstation or map attribution, crafting Money/Cash split, or the final disposition of fungible Cash. Unknown and partial evidence remains visibly identified.

Overview shows recorded base distance, raid distance and their combined total. Base distance starts when UDS first observes it; earlier movement cannot be reconstructed. Run and route statistics remain raid-only.

**Development-data transition:** 1.0.0 establishes the first public schema at 1 and starts fresh from development/RC schema 18. Incompatible profiles are preserved in an archive, not converted. Older `0.x` data remains untouched and is not imported. Valid final-schema statistics survive reinstallation. Keep one active UDS installation when switching from a manual install to Workshop.

Installation, local data, limitations and troubleshooting: [project documentation](https://github.com/bamboechop/ultimate-duckov-statistics). Source is available under the MIT license.

**Created by bamboechop.**

Want to help translate UDS? Leave a comment on the Steam Workshop page and mention the language you could help with.

---

Optional future promo caption (unpublished): “Help translate UDS. Tell me your language in a Steam Workshop comment.”

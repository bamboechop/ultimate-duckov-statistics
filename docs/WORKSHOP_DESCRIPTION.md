# Workshop description prepared for review

Publication is pending the M18 acceptance gates and explicit user instruction. The text below is the prepared item description; it is not evidence of an existing Workshop item or a qualified native candidate. Choose the final preview from the user-qualified native UI captures, then verify subscription installation before declaring this a supported distribution channel.

---

**Ultimate Duckov Statistics** keeps local, per-save statistics for Escape from Duckov: expeditions and routes, combat, equipment, item use and healing, economy, crafting, world time and sleep.

Open Statistics outside a raid from the main menu, base pause menu or F8. Nine tabs show your totals and recorded expeditions. Diagnostics explains unavailable or partial measurements and exports a JSON snapshot with CSV tables. Resetting UDS statistics archives its own generation; it does not change Duckov saves.

**Dependency:** install and enable [HarmonyLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839) separately. UDS does not bundle Harmony or game assemblies. The qualification baseline is Windows, Duckov 2.3.30 / Steam build 24013657, Unity 2022.3.62f2 and Harmony 2.4.1.0. Later game updates or conflicting patches may disable affected statistics while independently supported metrics continue.

**Privacy:** no telemetry, account or network service. Profiles, recovery files and exports remain on your computer. Exports can reveal gameplay history, timestamps and save-slot metadata; inspect them before sharing.

**Measurement limits:** UDS records evidence the game exposes. It does not infer rejected trigger attempts, ammunition consumption from firing callbacks, tote-effect activation, crafting workstation or map attribution, crafting Money/Cash split, or the final disposition of fungible Cash. Unknown and partial evidence remains visibly identified.

**Development-data transition:** v1 uses a separate data namespace. Older UDS development profiles remain untouched and are not imported. Keep one active UDS installation.

Installation, local data, limitations and troubleshooting: [project documentation](https://github.com/bamboechop/ultimate-duckov-statistics). Source is available under the MIT license.

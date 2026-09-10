# Workshop description prepared for review

M18 implementation and bounded native qualification are complete as recorded in [M18 acceptance](M18_ACCEPTANCE.md). Publication still requires final release-package verification and explicit user instruction. The text below is the prepared item description; it is not evidence of an existing Workshop item. Choose the final preview from the user-qualified native UI captures, then verify subscription installation before declaring this a supported distribution channel.

---

**Ultimate Duckov Statistics** keeps local, per-save statistics for Escape from Duckov: expeditions and routes, combat, equipment, item use and healing, economy, crafting, world time and sleep.

Open Statistics outside a raid from the main menu, base pause menu or F8. Ten tabs cover your statistics, recorded expeditions and About. About introduces UDS and its author, with optional support and community links. Diagnostics explains unavailable or partial measurements and exports a JSON snapshot with CSV tables. Resetting UDS statistics archives its own generation; it does not change Duckov saves.

**Dependency:** install and enable [HarmonyLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839) separately. UDS does not bundle Harmony or game assemblies. The qualification baseline is Windows, Duckov 2.3.30 / Steam build 24013657, Unity 2022.3.62f2 and Harmony 2.4.1.0. Later game updates or conflicting patches may disable affected statistics while independently supported metrics continue.

**Privacy:** no telemetry, account or network service. Profiles, recovery files and exports remain on your computer. Community links open in your browser only when you choose them. Exports can reveal gameplay history, timestamps and save-slot metadata; inspect them before sharing.

**Measurement limits:** UDS records evidence the game exposes. It does not infer rejected trigger attempts, ammunition consumption from firing callbacks, tote-effect activation, crafting workstation or map attribution, crafting Money/Cash split, or the final disposition of fungible Cash. Unknown and partial evidence remains visibly identified.

**Development-data transition:** v1 uses a separate data namespace. Older UDS development profiles remain untouched and are not imported. Keep one active UDS installation.

Installation, local data, limitations and troubleshooting: [project documentation](https://github.com/bamboechop/ultimate-duckov-statistics). Source is available under the MIT license.

**Created by bamboechop.** If you would like to support my work, [Ko-fi](https://ko-fi.com/bamboechop) is optional. Join my [Discord](https://discord.gg/8ngDVJ7jHH) to chat or get in touch.

Want to help translate UDS? Contact me on Discord and mention the language you could help with. Once the Steam Workshop page is available, you can leave a comment there too.

---

Optional future promo caption (unpublished): “Help translate UDS. Tell me your language on Discord or in a Workshop comment once the page is available.”

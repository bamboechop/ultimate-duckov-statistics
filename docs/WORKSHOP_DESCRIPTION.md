# Workshop description for 1.1.0

Paste the contents of the code block below into the Steam Workshop description. It uses [Steam formatting tags](https://steamcommunity.com/comment/ForumTopic/formattinghelp); do not copy the Markdown code fences or these preparation notes. The final link is the GitHub repository, following the author attribution and AI usage disclosure.

The preview is tracked at [mod/preview.png](../mod/preview.png), with the short in-game description in [mod/info.ini](../mod/info.ini). This longer description is maintained on the Workshop page. Follow the [release procedure](RELEASE_PROCESS.md) for publication and subscription-install verification. Detailed measurement and development-data limitations remain in [INSTALL.md](../INSTALL.md); completed qualification is recorded in [M18 acceptance](M18_ACCEPTANCE.md) and [release notes](../RELEASE_NOTES.md).

```text
[h2]Every raid has a story. See yours in numbers.[/h2]

[b]Ultimate Duckov Statistics (UDS)[/b] brings your raids, combat, equipment, item use, economy, and crafting together in one place. Review individual runs, track your personal bests, and discover which weapons and loadouts you use most.

[h2]What can you track?[/h2]
[list]
[*][b]Raids and routes:[/b] Revisit recorded runs, the maps you visited, active raid time, distance travelled, extractions, and containers opened.
[*][b]Map & Kills:[/b] Watch your recorded route unfold, including teleport connections. Select an encounter to see where it happened, the distance between you and the enemy when both positions are known, damage exchanged, weapons, ammunition, and inspected loot. Repeated visits to the same map stay separate.
[*][b]Personal records:[/b] Follow highlights such as your fastest extraction and longest successful raid.
[*][b]Combat:[/b] Explore damage, kills, ranged and melee accuracy, headshots, and weapon and ammunition statistics.
[*][b]Equipment:[/b] See your loadouts, weapons, attachments, armor, and totems, including time equipped during raids.
[*][b]Item use and healing:[/b] Find your most-used items and see how much health you've restored.
[*][b]Economy and crafting:[/b] Follow Money and Cash holdings and flows, crafted items, and resources spent on crafting.
[*][b]Movement and world time:[/b] Check distance travelled in raids and at your base, along with world time and sleep statistics.
[/list]

[h2]Getting started[/h2]
[olist]
[*]Subscribe to UDS and the required [url=https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839]HarmonyLib[/url] dependency.
[*]Enable both in the game's Mods menu and restart if prompted. Keep only one active copy of UDS if you previously installed it manually.
[*]Outside a raid, press [b]F8[/b] or open Statistics from the main menu or base pause menu. You can change the shortcut in Diagnostics.
[/olist]

Tracking begins while UDS is enabled; earlier gameplay cannot be reconstructed. Statistics are stored separately for each save.

[h2]Your statistics stay yours[/h2]
UDS stores its data locally on your computer. [b]No account, no telemetry, and no changes to your Duckov save files.[/b] Export your statistics as a ZIP containing JSON from Diagnostics for your own analysis or sharing. You can also restore a saved JSON or ZIP export for the same save slot, with a preview and confirmation before replacing your current UDS statistics.

[h2]Languages and translation help[/h2]
[b]English and German[/b] are included. UDS follows the game's language setting.

Want to help bring UDS to your language? [b]Leave a comment below and tell me which language you'd like to help translate.[/b]

[h2]Compatibility and feedback[/h2]
Tested on [b]Windows with Duckov 2.3.30[/b]. UDS records the statistics exposed by the game and labels incomplete or unavailable measurements. Game updates or other mods can affect tracking; check Diagnostics if something looks wrong.

Found a bug or have a suggestion? Leave a comment or open a GitHub issue with your UDS version, game version, and steps to reproduce it.

[b]Created by bamboechop.[/b]

[h2]AI usage disclosure[/h2]
AI tools were used extensively for development, code reviews, and documentation. The Workshop preview artwork was generated with AI using an in-game screenshot as a reference. Feature decisions and in-game testing were handled by bamboechop.

[url=https://github.com/bamboechop/ultimate-duckov-statistics]GitHub repository — source code, documentation, and issue tracker[/url]
```

Optional future promo caption (unpublished): “Help translate UDS. Tell me your language in a Steam Workshop comment.”

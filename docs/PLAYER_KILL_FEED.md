# Player kill feed

The passive raid HUD shows confirmed player kills and player deaths. It excludes unrelated NPC/world deaths, companion kills and assists. It uses blue for the Steam player name and yellow for the other actor, with a white weapon silhouette between them. Incoming entries put the killer first. Native colored item icons are the fallback when a silhouette cannot be made; an unknown weapon uses a dash. A verified outgoing headshot adds a white head/crosshair pictogram. Smoke, blindness and no-scope status are not displayed.

The feed starts at the native storm information box's left edge, eight native UI units beneath its bottom. If that box is hidden, the bottom follows the weather/day text block. Rows have an eight-unit gap and follow native canvas scaling. Their background color, opacity, corner radius and padding come from the native storm box. Names and the headshot icon are centered against a compact 32-unit weapon icon with a 16-unit distance caption one unit beneath it; measured text heights keep the distance visible. No native weather layout is modified. Default duration is ten unscaled seconds, newest first, maximum six entries, with a short entrance and one-second exit fade.

## Settings

Open **Diagnostics → Data & settings → Kill feed** outside a raid. English and German controls provide:

- Feed enabled (default on).
- Duration, 1–30 seconds (default 10).
- Maximum entries, 1–6 (default 6).
- Size, 50–200% (default 100%).
- Left/right screen alignment (default left).
- Horizontal and vertical offsets, in native UI units; positive horizontal moves right and positive vertical moves down (default zero).
- Distance and headshot visibility (both on by default).
- Reset only the kill-feed settings.

Settings share UDS's existing `settings.json`; saving them preserves the configured panel shortcut. Changes apply to the live owner and persist between sessions. This is a temporary home for HUD settings. Disable the standalone **CS Like Kill Feed** mod to avoid duplicate feeds. UDS neither disables it nor edits its configuration.

## Evidence and lifetime

The existing combat adapter publishes an optional transient notification after validating raid context. Supported player final blows and player deaths enter a bounded six-row queue; a bounded recent-ID window rejects repeated notifications. Generic critical hits are not headshots. Incoming headshot information is not independently proven and is never shown.

Distance is horizontal player-to-other-actor separation at the fatal health assignment, before corpse cleanup. It uses the same map and finite-position rules as Map & Kills and Overview distance highlights. For delayed damage, this is separation at death, not projectile travel or the firing position. Missing, invalid or cross-map evidence omits the distance. The optional encounter observer supplies those snapshots; losing it does not suppress an independently confirmed feed entry.

Callbacks only copy data. Native text, Steam-name lookup and icon generation happen in the HUD tick, outside damage callbacks. A failed feed callback cannot abort statistics recording. The feed does not read historical runs, write encounter history or change a Duckov save. Existing adapter trust checks remain in force; this adds no Harmony patches or third-party dependency. The installed Steamworks assembly is referenced but not shipped.

Scene/save transitions and disposal clear the feed. Native HUD visibility controls its visibility. Discovery is limited to ten half-second attempts after activation or a lifecycle change, and stops once bound. Six row objects are reused; unchanged text/layout is not rebuilt every frame. White icons are cached up to 64 item types, with native icons thereafter. All generated sprites/textures and owned UI are released on disposal. Native assets are never destroyed or modified.

## Validation

Automated tests cover player-only filtering, verified headshots, incoming deaths, finite/map-matched distance, bounded queue/expiry, settings round trips and shortcut preservation. Production callback tests cover position capture before corpse movement, recording failure independence, callback exception isolation and the public-death/health-postfix sequence. Source-linked HUD tests cover native anchor bounds, hidden storm fallback, right alignment and offsets, localization, passive input, expiry, bounded discovery, scene/save changes, destroyed UI recovery and cleanup.

Unity/TMP, Steam and GPU boundaries in those tests are simulated. In-game appearance, icon rendering, placement under the real storm bar, FPC/third-person play, and frame-time impact require native testing.

Local checks on 2026-09-27 passed 2,562 core tests and 208 shell tests in both Debug and Release, plus 210 shell tests with both diagnostic flags enabled. Ordinary Release and combined-diagnostic Debug native builds completed with no warnings or errors. The installed contract probe and ordinary 13-file package audit passed. These checks do not constitute native visual acceptance.

The subsequent row-layout correction passed 211 Release shell tests and a zero-warning native Release build. Its regressions check native background/padding reuse, measured distance-label bounds and vertical centering with distance enabled or hidden. Inspection of the installed Storm prefab found black at alpha 109/255 and radius 15; live components supply those values to the feed instead of independent styling constants.

Native test checklist:

1. Disable CS Like Kill Feed and restart Duckov with this UDS build. Keep the normal mod set, including FPC if desired.
2. Check the settings section in English and German. Change a setting, reopen UDS and restart once to check persistence; the panel shortcut must remain unchanged.
3. During a raid, kill enemies at different distances, including a confirmed headshot and a body-shot kill. Verify names, icon, distance under the weapon, optional headshot icon, left alignment and spacing below the storm box.
4. Make more than six quick kills: only the newest six should remain. After ten seconds each entry should expire. NPC-versus-NPC deaths must not appear.
5. On a convenient player death, check killer → weapon/distance → player order without a fabricated headshot icon. Unknown environmental deaths may lack weapon/distance.
6. Transition between maps/base, pause/hide HUD and return. Check for stale or duplicate entries, persistent input blocking, exceptions in Player.log, and visible performance changes.

# Run attribution and map-total completeness

UDS records an event's outcome counters separately from the association between its source segment and outcome segment. An unresolved source does not imply that damage, kills, healing, or other outcomes were missed.

## Native effect origins

The inspected Duckov 2.3.30 `Duckov.Buffs.CharacterBuffManager.AddBuff` reuses the first existing same-ID buff and calls `NotifyIncomingBuffWithSameID`. A subsequent application can therefore arrive on the same runtime instance after an equipment change. The combat origin resolver retains equipment and route ambiguity independently: conflicting equipment does not discard a matching map/segment, while applications in different segments cannot silently retain the first segment even if equipment is identical. A later matching application does not erase a conflict. Run/generation boundaries still prevent correlation reuse.

`ItemStatsSystem.Effect.SetItem` notifies the effect's triggers of the observed item application. The existing callback captures that map/segment for non-player effects as well as player effects. Non-player effects never borrow the player's equipment. No new hook, per-frame scan, or native state mutation is involved. Unobserved origins remain unknown.

Inspected assembly SHA-256 values:

- `TeamSoda.Duckov.Core.dll`: `298d5d5885427632d5a94b2f3ce587f8ebc9528ec71e575a475158c326ecae8f`
- `ItemStatsSystem.dll`: `a276e15c022f71b2214bd05e1b9b0f2e620c16561df576fed0b79c2fe4402e60`

## Persistence and consumers

- `RouteCapabilities.EventAttribution` and `HistoricalEventAttributionIncomplete` describe source/outcome associations. Only fully proven joins enter `SegmentEventAssociations`.
- `RouteCapabilities.RouteAwareMapTotals` independently describes outcome aggregation into segments. Missing source evidence alone does not disable it. A missing or mismatched active destination, unavailable capture, route repair, or stopped association capture still keeps affected map totals unavailable/partial.
- Run and lifetime route-map aggregation, normalization, save/reload, Runs presentation, and exports preserve this distinction. `segments.csv` includes an appended `route_map_totals_capability` column; existing association columns retain their meaning.
- Runs uses map-total capability and the relevant metric capability/repair state to qualify per-map values. Item Use uses its own run healing-completeness evidence. Genuine partial values and unavailable zero remain distinct from proven complete values and zero.
- Existing historical records are not upgraded, reconstructed, or edited. In particular, a record that already disabled both association and map-total capabilities remains conservative; matching sums are not proof of complete capture.

The tracker retains the first failed association's event kind, UTC timestamp, and source/outcome map and segment identifiers in the existing provenance field. If a later event first loses destination-counter completeness, its detail is retained independently in the map-total capability provenance. Each identifier is limited to 128 characters. Repeated failures neither append history nor format new diagnostic strings, and no diagnostic write is added to a gameplay callback.

## Verification boundaries

Regression tests exercise the native buff/effect and health callback composition, equipment changes, non-player damage, conflicting map origins and revisits, unresolved source versus unresolved destination, failure ordering, repeated failures, persistence/normalization, route-map aggregation, JSON/CSV export, and English/German presentation. Healing capture-loss and repaired-data checks remain separate.

On September 12, 2026, Debug and Release each passed 2,055 main tests and 90 shell tests. The native Release build completed without warnings or errors; the installed contract probe and ordinary six-file package validation passed, including the check that performance-diagnostic call sites are absent from the Release assembly.

These checks do not identify the exact failed event in the original September 11 playtest: that build retained only generic provenance. Live acceptance requires a new run with repeated effects and equipment changes, followed by a map transition, extraction, export, and restart. Any remaining association gap can then be inspected using the retained diagnostic detail.

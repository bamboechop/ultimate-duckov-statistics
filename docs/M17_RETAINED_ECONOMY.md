# M17 retained Economy

Economy consumes the existing M9 flows and M15 holdings through the shipped retained shell. The accepted references are [Economy](../mockups/uds-ui-economy.jpg) and [Economy with non-current holdings](../mockups/uds-ui-economy-partial.jpg). Their example values are not fixtures. The user's later presentation decision removes the mockup's development-history banner; stored historical evidence remains unchanged, and genuinely unavailable current metrics remain explicit.

## Acceptance mapping

| Reference section or state | Production source and retained control | Automated coverage |
| --- | --- | --- |
| Current holdings | `EconomyHoldingsReducer.Project(Profile.Statistics.Holdings)` supplies three separate metric cards: liquid wealth, Money and Cash | Current zero, independent unavailable components, stale timestamps, non-comparable inputs and Int64 overflow |
| Money | Authoritative account/ATM observation from `NativeEconomyHoldingsAdapter`, distinct from owned item currency | Flow values cannot substitute for a holding; Money survives unavailable Cash |
| Cash | Authoritative exact main inventory, storage and pet-inventory roots, recorded by the existing holdings adapter | Unavailable or last-observed Cash does not erase independently current Money |
| Liquid wealth | The existing checked projection requires both components current, exact-generation and supported native 1:1 units | Missing, stale, unsupported comparability and checked-addition overflow remain unavailable |
| Money flow and Cash flow | Exact `Profile.Statistics.Economy.Currencies` gross inflow, gross outflow and derived net; separate adjacent desktop sections | Independent currency states, recorded net zero, known-zero versus absent capture, current failure and arithmetic saturation |
| Sources | The currency's recorded source dictionary, in deterministic semantic order | Source-attribution failure does not hide exact amounts or supported contexts; equal text never joins identities |
| Contexts | Recorded context dictionary, ordered Base, Raid, Shop, Reward, Paused and Unknown | Missing contexts are not reconstructed from source names, holdings or run summaries |
| Of which proven acquired | `Economy.CashAcquired`, displayed beneath Raid context in the inflow column only | Strict subordinate placement, no net/secured/profit claim, and missing context evidence remains unavailable |
| Recent runs | Existing twelve-run publication, descending end time with ordinal Run ID ties; each run's own `Economy` aggregate | Same-name runs remain separate; exact-generation navigation, own net values, ordering and existing bound |
| Expanded run | One selected native header with exactly two peer cards, Money net and Cash net | Collapsed/expanded state, 10px following gap, one enclosing 50% black surface and independently qualified values |
| View run | Shared styled native control with the unchanged generation and Run ID; visible for the expanded run | Right inset and vertical centering against the measured map title; missing or stale IDs never substitute another run |
| Empty and unavailable | Supported no-change scope displays zero/no recorded changes; missing capture remains Unavailable | No anonymous legacy-only cards, no invented zero and no automatic history banner |
| Desktop and narrow layout | Desktop holdings/flow panel uses two-thirds width and Recent runs one-third; narrow layout stacks the primary panel first and stacks Money/Cash flow sections | Measured containment, long names, large values, table fallback, complete section retention and bounded scroll offsets |
| Refresh and teardown | Factory-bound publication, copied primitive presentation, exact selection and separate scroll offsets | Replaced publication members, generation loss, removed selection, stale callbacks and clamped offsets |

## Production path and evidence

`NativeEconomyAdapter` records accepted currency changes and their captured source/context through `EconomyStatisticsReducer`. `NativeEconomyHoldingsAdapter` independently publishes observed holdings and freshness. The current capability snapshots enter `StatisticsPanelProjectionFactory`, together with the active profile's recorded aggregates and recent runs. `EconomyProjectionBinding` ties all retained input roots to that exact factory publication. `EconomyPresentationFactory` copies values, strings and read-only collections before the view receives them. The view does not retain a mutable profile or perform profile I/O, inventory scans, recipe queries or tracking operations.

Holdings do not come from gross inflow or net flow. Money and Cash remain separate currencies throughout the flow tables and per-run cards. A recorded zero net with positive inflow and outflow remains a real recorded value when current capture fails; the affected flow receives a concise current notice. No-row zero requires supported current and captured amount/direction evidence without a repaired-data marker. Source, context, acquisition and amount capabilities are evaluated independently, preserving valid sibling data. Unknown adjustments describe known direction and amount without inventing a cause.

Last-observed holdings keep their own value and local `yyyy-MM-dd - HH:mm:ss` timestamp. One current component does not promote a stale sibling or make combined wealth current. The native holdings reducer remains the owner of comparability and checked addition. The [M15 contract](M15_NATIVE_CONTRACTS.md) defines authoritative root hydration, save-generation boundaries and observation freshness.

Recent-run cards route only through exact recorded Run IDs from the same generation. Their ordinal follows the existing Runs ordering; map identities determine the route and distinct-map count, never display-name equality. Missing route evidence has an unavailable map count rather than an inferred map total. Selecting a run that disappears on refresh clears the expansion instead of selecting a neighboring record.

## Native appearance and lifecycle

The retained view reuses the shell's native TMP font/material, outcome badges, `RunsHistoryButton`, `ButtonAnimation`, keyboard/controller feedback and styled View run control. Physical Cash imagery resolves native item TypeID 451 through the existing resolver. The Money heading borrows the installed native `MoneyDisplay/Money/Image` sprite, rather than substituting the physical Cash icon or a replacement currency font.

The native icon audit on 2026-09-07 found the same reference on fourteen serialized `MoneyDisplay` hierarchies in `resources.assets`: the Money image's Unity Image sprite field points to Sprite path ID `5970`, named `Cash`, containing Duckov's double-stem D currency symbol. The physical Cash sibling points to Sprite path ID `6387`, named `Banknote`. Runtime lookup uses the exact MoneyDisplay-relative path, requires sprite name `Cash`, and accepts only one distinct native sprite reference. Missing/ambiguous imagery uses the existing unknown-icon mark and cannot change a metric or block opening. No extracted image, native assembly or game asset is included in the package.

The audited `resources.assets` SHA-256 is `93c4ab6ad71fdb3bf4a331bbb2ac6bc2f7db7b0f12efe60dd43ea019ab2e543d`; `TeamSoda.Duckov.Core.dll` is `298d5d5885427632d5a94b2f3ce587f8ebc9528ec71e575a475158c326ecae8f`, matching the [M17 installed-native baseline](M17_NATIVE_CONTRACTS.md).

Headings and tables begin at their panel's actual 30px padding. Flow sections use the established 40px desktop gap; expanded run cards retain a 10px following gap. Holdings, flow values and expanded run nets use measured native text, so large values and long localized names can wrap. Tables switch to a label-over-values arrangement when the measured value columns would leave too little label width. This is a layout decision, never a panel-opening gate.

Both desktop panels scroll independently through the shared clamped Runs configuration, rounded masks and directional overflow cues. Below the established 1180px viewport threshold, the primary panel stacks before Recent runs inside a bounded outer scroll region. Each inner viewport remains bounded and forwards scrolling at its limits. Down from the Economy tab enters the main panel; Right enters recent run headers; Left returns to the main panel or tab strip. The expanded View run button remains reachable by keyboard. Generation/role/Run ID bindings prevent a pressed control from changing its action during refresh. Same-generation refresh retains a still-present selection and scroll position; invalidation hides old controls immediately. Disposal releases listeners, native badge resources and owned objects while preserving shared assets.

## Qualification

The focused `RetainedEconomyTests` Debug batch passed **34/34** on 2026-09-07. Tests exercise the actual reducers, shared projection, retained factory and deterministic layout/selection policies. An independent read-only review of the three Economy implementation files found no concrete reachable semantic, scrolling or input-binding defect. The native Money icon addition was audited separately against the installed serialized assets. Integrated full-suite, build, native-probe, package and deployment results belong to the overall delivery record.

Gameplay and visual acceptance remain user-controlled under [M17_MANUAL_VALIDATION.md](M17_MANUAL_VALIDATION.md). Minimum checks are current holdings in base versus last-observed holdings in the main menu; separate Money/Cash flows around an ordinary transaction; expanded recent-run values and exact View run navigation; narrow/desktop scrolling and text wrapping; native currency symbols, outcome badges, hover/submit audio, and repeated open/close. No real profile reset or Duckov gameplay action was executed for this tab.

# Equipment duration reconciliation and completion retry correction

## Observed failure

The September 6, 2026 diagnostic inspection found a Ground Zero starting-map
aggregate whose melee weapon (`duckov:weapon:306`) had two attachment-signature
durations, `373.3314548999999` and `572.4438914` seconds. Their binary floating-point
sum was `945.7753462999999`, while observation of its `3:Gem/` nested path was
`945.7753463`. The strict parent-duration invariant correctly rejected that
inconsistent aggregate. The difference was about `1.14e-13` seconds.

The log first reported this aggregate failure after an earlier 83-second run.
The later approximately 807-second extracted run could not complete because its
completion handler first drained the failing deferred profile snapshot. Its
terminal checkpoint remained on disk with `PendingTerminalOutcome = Extracted`.
This is a current-session accumulation failure, independent of pre-v1 migration.

## Arithmetic correction

Equipment duration counters now use decimal seconds, including character and
nested observation/state counters and structured totem durations. Absolute
observed active-time boundaries are converted before subtracting the interval;
every parent and child receives the same interval. Accumulation, attachment
partitions, run/map merges, cloning and JSON serialization keep decimal values.
Display projections convert only after summation.

The parent inequality remains strict. The existing state/observation reconciliation
rules retain their previous relative tolerance; no tolerance is added to the
parent inequality. Arithmetic overflow is rejected before duration
publication rather than saturating a parent counter. JSON field names and numeric
shapes remain unchanged. Decimal has no NaN/infinity values; out-of-range numeric
input is rejected during deserialization.

There is no clamping, rounding of persisted counters, automatic statistics repair,
or rewrite of user data. Previously inconsistent snapshots remain subject to the
existing candidate validation and recovery policy. A code fix cannot by itself
establish trustworthy missing historical observations.

## Retry and diagnostic behavior

- Completed-run attempts use monotonic exponential delays of 1, 2, 4, 8, 16,
  32 and then 60 seconds. Frame updates, profile transitions and cleanup use the
  same completion budget. False results and thrown exceptions retain the exact
  pending summary. Success alone clears it and resets the budget.
- Deferred profile capture/write failures retain dirty state and the failure
  result. Frame submissions and explicit flushes share a backoff budget; marking
  newer data dirty does not bypass it. A permitted flush retains its existing
  single immediate retry, then backs off if it still fails. A delayed failed
  flush reports failure, never a successful durability barrier.
- Persistence diagnostic reporting emits the full exception and context at most
  once per 60 seconds. Completion status messages have their own 60-second
  cadence. Repeated cleanup calls do not add redundant completion warnings.
- Pending checkpoints, summaries, profile barriers and repository ownership stay
  intact until durability is established. Duckov saves are not touched by this
  correction.

## Regression coverage

`FractionalAttachmentIntervalsReconcileAcrossRunsMapsAndPersistence` reproduces
the differing addition groups from ordinary attachment changes in three runs,
then validates lifetime, starting-map and route-map totals after save/reopen.
An additional `1e-15` parent deficit is still rejected.
The failed deferred save also leaves primary/backup profile bytes and an extracted
terminal checkpoint byte-for-byte intact.

`PersistenceBackoffTests` checks sustained frame calls, thrown and returned
failures, retained ownership, bounded attempts, diagnostic cadence, shared
tick/flush budgets, new dirty notifications, and eventual successful retry.
`NativeExtractionCompletionRetriesAndCleanupRespectTheSameBudget` exercises the
production lifecycle adapter, including its unchanged extracted terminal
checkpoint and blocked cleanup/profile transition.
`NativePersistenceDiagnosticTests` counts full exception emissions at the
production coordinator boundary.

Live gameplay, installation, and recovery of the inspected user profile are
separate from these automated checks. After deployment, an authorized one-time
UDS data recovery preserved the original files, recalculated duration counters
from retained evidence, refreshed UDS save-identity metadata, and persisted all
13 runs with the pending run marked Extracted exactly once. Both the recovered
profile and its 12-run fallback passed validation and isolated startup checks.
The terminal checkpoint was retained for normal startup deduplication; Duckov
saves were unchanged. This was a separate data operation, not an automatic
repair path added to the mod. Live gameplay qualification remains separate.

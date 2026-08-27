# Repository agent instructions

## Pre-1.0 distribution and migration policy

- Every UDS `0.x` build, tag, GitHub pre-release, and downloadable ZIP is a development artifact for voluntary testing. GitHub is not a supported installation channel for these builds.
- Supported upgrade and persisted-profile migration guarantees begin only with the first version explicitly declared as officially distributed through a supported channel. No current `0.x` build establishes that baseline.
- Existing pre-1.0 migration code, fixtures, and exact-migration tests are internal robustness work and best-effort development continuity. They do not create an end-user compatibility promise between `0.x` versions, even when a milestone document records an exact migration result.
- During implementation or review, do not report a defect as actionable when its only reachable path is carrying UDS-owned persisted data from one `0.x` build into another. It becomes actionable if the same root cause also affects a clean/current installation, same-version current-schema operation or recovery, a future officially supported migration baseline, or data safety independently of the unsupported predecessor.
- Through M17, do not remove existing pre-1.0 migration code or tests merely because that path is unsupported; they remain useful development hardening while the `0.x` schemas are still changing.
- M18 explicitly ends that retention policy. Before the first v1 release candidate is packaged, remove schema-by-schema `0.x` migration branches, compatibility-only serialized members, fixtures, tests, and documentation whose sole purpose is carrying unsupported pre-v1 UDS data forward. The shipping v1 codebase must support a clean first install and reinstallation against its own current-format data, while retaining current-schema validation, backup/temporary recovery, and other data-safety behavior that remains reachable after v1. Do not ship speculative migration machinery for versions no supported user could have installed.

## Repository documentation policy

- Treat branch, pull-request, review, and CI status as mutable. Durable documentation should link to the authoritative GitHub surface instead of embedding a live status snapshot that will become stale.
- Record immutable completed-delivery facts such as merged commit IDs, tags, publication dates, test totals, artifact sizes, and checksums only after they have been verified.
- Do not add unit tests that enforce editorial prose, exact Markdown headings, milestone names, or the presence of a particular documentation paragraph.
- Test documentation only when it is a machine-consumed contract, such as required package inventory, generated output, an executable example, or a formally parsed schema. Use normal review for narrative accuracy and status transitions.
- Update milestone and release wording when its state changes; do not preserve obsolete candidate or draft-PR language merely to satisfy an automated check.

## GitHub authentication on Windows

- GitHub CLI authentication for this checkout is stored in the Windows credential manager. A `gh auth status` command executed inside the Codex sandbox can incorrectly report that the stored token is invalid because the sandbox cannot access the keyring.
- Do not ask the user to reauthenticate, open a browser, or log in to GitHub based only on a sandboxed authentication failure.
- Prefer the connected GitHub tools for supported GitHub reads and operations.
- When the `gh` CLI is genuinely required, rerun the exact command with the necessary approval outside the sandbox so it can access the Windows keyring.
- Ask the user to reauthenticate only if `gh auth status` also fails outside the sandbox.
- Never work around the sandbox by using `--insecure-storage`, copying the token into repository files, or persisting it in an environment variable.

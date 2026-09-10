# RC qualification and publication

The single release decision record is [M18 acceptance](M18_ACCEPTANCE.md). Live branch, PR, checks and release state belong on [GitHub](https://github.com/bamboechop/ultimate-duckov-statistics). Do not replace unexercised native gates with automated-test or historical-performance results.

M18 merged through [PR #19](https://github.com/bamboechop/ultimate-duckov-statistics/pull/19) on 2026-09-10 as `6e2d8b9bb7fda5b8cc0b9296cb399256315b4319`. Two independent reviews and the recorded native qualification are complete within the accepted performance and coverage bounds. The next release is `v1.0.0-rc.1`; the steps below prepare its exact assets and publication without repeating unaffected gameplay.

## Prepare the exact candidate

1. Freeze the exact `main` commit after the merge and documentation updates. Build from a clean isolated snapshot if the working checkout contains unrelated edits. Run `scripts/build.ps1` with the installed Duckov root, formatting/analyzers and source safety checks. Run the mixed workload alone only when collecting new reportable timing/memory evidence; existing accepted captures do not need repetition.
2. Run `scripts/verify-reproducibility.ps1 -DuckovPath $env:DUCKOV_PATH -SourceCommit <40-character-commit>`. It archives that exact Git tree, builds in two distinct roots, audits DLL/PDB/package identities, independently extracts ZIPs and compares both DLLs, both PDBs and ZIP bytes.
3. Run `scripts/create-release.ps1 -DuckovPath $env:DUCKOV_PATH -Version 1.0.0-rc.1` to prepare the local ZIP, checksum and inventory. Compare its hashes with the immutable proof. This script does not publish a GitHub release.
4. Record source, native baseline, test results, review range, ZIP and file hashes in the acceptance record and local manifest. Keep the five-file package exact; evidence and portable PDBs are separate artifacts.
5. Confirm Duckov is closed and run the authorized UDS-only `scripts/deploy.ps1`. It stages outside `Duckov_Data/Mods`, keeps a verified prior copy under repository `artifacts/deployment-backups`, promotes the package and reads back every hash. If needed, redeploy that verified prior package using the script's `PackagePath`; never restore game saves as part of package rollback.

The qualified `830ef3f` package remains historical evidence with its recorded hashes. The post-merge install-guide update changes a packaged file, so regenerate the ZIP and checksum from the final release commit and record their new identities. Compare both ordinary DLLs against the qualified binaries; a documentation-only change must not silently replace runtime qualification with an assumption of equivalence.

## User-controlled qualification

Use [M18 manual qualification](M18_MANUAL_QUALIFICATION.md) and the frozen [performance campaign](../PERFORMANCE.md). The user controls game launch, save selection, actions, captures, real reset and shutdown. During controlled captures, stop all builds, tests, indexing and other agent-side work. Preserve unfavorable valid captures.

Behavior-changing corrections require regression tests, independent re-review, new artifact identities and requalification of affected behavior. A documentation-only change does not require repeating unaffected gameplay; preserve the exact qualified binary identities and distinguish them from the later documentation commit.

## Approval for publication

After the final package checks pass, prepare the exact release body, tag target, ZIP, checksum and dependency declaration for user approval. The completed merge does not itself publish a release.

For the GitHub RC, use tag and title `v1.0.0-rc.1`, target the exact verified documentation-bearing commit, mark the release as a pre-release, and attach the installable ZIP plus its `.sha256` sidecar. Include the recorded measurement/coverage limits and link to [M18 acceptance](M18_ACCEPTANCE.md). After publication, independently download both assets, verify the tag target and checksum, and extract/compare the exact five-file payload with the local verified package. Record completed publication facts only after that readback.

Workshop readiness requires the verified package, project license, [prepared description/limitations](WORKSHOP_DESCRIPTION.md), preview image, supported game/build, separately installed HarmonyLib item `3589088839`, correct content root and install/use instructions. Select the final preview from qualified native UI captures. The Workshop item ID, preview approval, upload and subscription-install check remain outstanding; do not invent a published item or claim a supported channel before it exists.

Publish `v1.0.0-rc.1` only on instruction. Later behavior-changing RCs increment the suffix; eventual v1.0 promotion needs its own acceptance and approval. The first explicitly supported distribution defines the supported upgrade baseline. No development `0.x` build creates a migration promise.

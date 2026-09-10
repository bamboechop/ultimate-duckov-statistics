# RC qualification and publication

[M18 acceptance](M18_ACCEPTANCE.md) records the accepted baseline. The [RC2 release record](RC2_RELEASE.md) adds the four merged follow-up batches and the user's 2026-09-11 confirmation that their final in-game checks passed. Live branch, checks and release state belong on [GitHub](https://github.com/bamboechop/ultimate-duckov-statistics).

RC1 was published from `58591d5a397abd484284291fd95ab92d949f46fb`. RC2 was published from `f6cd8840b2cdbaface9ad3aeaf99efc9770b9021`; its [independent publication readback](RC2_PUBLICATION.json) passed. The steps below record the RC2 preparation procedure. For a later candidate or final v1.0, select its new version and exact source explicitly. Earlier tags, release assets and batch packages retain their historical identities.

## Prepare the exact candidate

1. Update the build version, `ProductInfo`, `mod/info.ini`, release-script default and packaged install guide together. Commit the scoped release changes on `main`, preserving unrelated local work. Freeze its full commit SHA.
2. Use a clean archive of that exact commit. Run `scripts/create-release.ps1 -DuckovPath $env:DUCKOV_PATH -Version 1.0.0-rc.2`. It runs the Debug/Release main and ordinary shell suites, installed-native probe, builds and package audits, then creates the ZIP and checksum. Also run the diagnostic shell suites, native Debug build, changed-source formatting/analyzers and tracked-source safety checks. Do not collect reportable workload timings alongside other builds/tests.
3. Run `scripts/verify-reproducibility.ps1 -DuckovPath $env:DUCKOV_PATH -SourceCommit <40-character-commit>` from the repository. It builds that exact archived tree in two distinct roots and compares both DLLs, both PDBs and deterministic ZIP bytes. Compare the release ZIP and binaries with this proof.
4. Independently extract the release ZIP and verify exactly five files: `info.ini`, `INSTALL.md`, `LICENSE`, `UltimateDuckovStatistics.Core.dll` and `UltimateDuckovStatistics.dll`. Compare every file with the clean package and verify the checksum sidecar. Record the exact source, SDK/native baseline, tests, ZIP and file hashes in the local release manifest. Keep PDBs, logs and evidence outside the installable ZIP.
5. Inspect the release changes against the final reviewed implementation. A version/documentation-only preparation preserves gameplay behavior but changes artifact hashes; neither byte equality with RC1 nor a repeated unaffected gameplay campaign is required. If behavior changes, stop and requalify that change.
6. Verify the remote `main` SHA and its CI checks before publication. If source or packaged inputs changed after qualification, generate and verify a new package from the new exact commit.

## User-controlled qualification and installation

The user controls game launch, save selection, gameplay, resets and shutdown. Their completed RC2 checks are recorded in [RC2_RELEASE.md](RC2_RELEASE.md). Preserve accepted M18 performance deviations and coverage limits without reopening the completed campaign. A new behavior-changing correction requires affected tests, independent review, artifact identities and native qualification.

If installing the exact RC2 package locally, confirm Duckov is closed and use the authorized UDS-only `scripts/deploy.ps1 -DuckovPath $env:DUCKOV_PATH -PackagePath <verified-extracted-folder>`. It keeps a verified prior-mod copy outside the loader scan root and reads back the deployed hashes. Never restore or alter game saves as a package rollback. Installation and publication are distinct; neither is implied by building the ZIP.

## Publish the GitHub pre-release

Prepare the exact release body, full tag target, ZIP and `.sha256` sidecar before asking the user to publish. No tag, draft release or upload is created by the preparation scripts.

Use tag and title `v1.0.0-rc.2`, target the exact verified release commit and select **Set as a pre-release**. Attach only the installable ZIP and checksum sidecar. Include the four-batch change summary, separately installed HarmonyLib dependency, native baseline, final validation results and retained M18 measurement/coverage limits. Link the RC2 and M18 records at the frozen source commit.

After the user publishes, verify the tag target, pre-release flag and assets. Independently download both assets, calculate the ZIP hash, compare the sidecar, extract the exact five-file payload and compare it with the local verified package. Record publication facts only after that readback.

## Final v1.0 and Workshop

RC2 publication does not publish the mod to Steam Workshop or establish the final supported upgrade baseline. Final v1.0 promotion needs its own version/package checks and user decision; repeat gameplay only if source behavior changes.

Workshop preparation requires the verified package, license, [description and translation invitation](WORKSHOP_DESCRIPTION.md), approved preview, supported native baseline, separately installed HarmonyLib item `3589088839`, content root and install/use instructions. Item creation/upload, preview selection and subscription-install verification remain user-controlled. Do not claim a supported Workshop channel before it exists and has been verified. No development `0.x` build creates a migration promise.

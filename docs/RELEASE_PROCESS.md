# 1.0.0 qualification and publication

[Release notes](../RELEASE_NOTES.md) describe the 1.0.0 scope: the accepted M18/RC2 baseline, German localization/layout and the simplified About invitation. The [GitHub release v1.0.0](https://github.com/bamboechop/ultimate-duckov-statistics/releases/tag/v1.0.0) was published from `2cf01cdeb1d2f1f1be453a2aee705b4d42b0919f`. Its verified five-file archive remains unchanged. The first Workshop listing and subscription-install verification are pending. Live branch, checks and release state belong on [GitHub](https://github.com/bamboechop/ultimate-duckov-statistics).

RC1 was published from `58591d5a397abd484284291fd95ab92d949f46fb`. RC2 was published from `f6cd8840b2cdbaface9ad3aeaf99efc9770b9021`; its [independent publication readback](RC2_PUBLICATION.json) passed. Current packaging adds the tracked Workshop preview as a sixth file without changing the 1.0.0 runtime DLLs. Earlier tags, release assets and batch packages retain their historical identities.

## Prepare a future GitHub release build

For the current Workshop preparation, build/package and deploy using the workflow below. Do not rerun `create-release.ps1 -Version 1.0.0` to replace the already published GitHub archive. The release qualification procedure remains applicable to future release candidates with their own version and frozen source identity.

1. Set the build version, `ProductInfo`, `mod/info.ini`, release-script default, version contract test and packaged install guide to the chosen new release version. Confirm Diagnostics and both assembly product versions agree. Profile schema versions are independent of release numbers: retain the schema-1 baseline unless a separately designed format change requires migration. Verify clean/current-schema reinstallation, recovery and supported upgrade behavior. Commit the scoped release changes, preserving unrelated local work, and freeze the full final source SHA after merge to `main`.
2. Use a clean archive of that exact commit. Run `scripts/create-release.ps1 -DuckovPath $env:DUCKOV_PATH -Version <new-version>`. It runs the Debug/Release main and ordinary shell suites, installed-native probe, builds and package audits, then creates the versioned ZIP and its checksum. Also run the diagnostic shell suites, native Debug build, changed-source formatting/analyzers and tracked-source safety checks. Do not collect reportable workload timings alongside other builds/tests. An archive built from an uncommitted working tree is a local preparation artifact until rebuilt and verified from the frozen source.
3. Run `scripts/verify-reproducibility.ps1 -DuckovPath $env:DUCKOV_PATH -SourceCommit <40-character-commit>` from the repository. It builds that exact archived tree in two distinct roots and compares both DLLs, both PDBs and deterministic ZIP bytes. Compare the release ZIP and binaries with this proof.
4. Independently extract a newly built release ZIP and verify exactly six files: `info.ini`, `preview.png`, `INSTALL.md`, `LICENSE`, `UltimateDuckovStatistics.Core.dll` and `UltimateDuckovStatistics.dll`. Compare every file with the clean package and verify the checksum sidecar. Record the exact source, SDK/native baseline, tests, ZIP and file hashes in the local release manifest. Keep PDBs, logs and evidence outside the installable ZIP. Historical published archives use the inventory defined at their own source commit.
5. Inspect the release changes against the final reviewed implementation and retain affected review/native acceptance. A version/documentation-only preparation changes artifact hashes without adding gameplay behavior. Requalify affected behavior when it changes; do not repeat unaffected gameplay or the accepted performance campaign solely for a version bump.
6. Verify the remote `main` SHA and its CI checks before publication. If source or packaged inputs changed after qualification, generate and verify a new package from the new exact commit.

## User-controlled qualification and installation

The user controls game launch, save selection, gameplay, resets and shutdown. Their completed RC2 checks are recorded in [RC2_RELEASE.md](RC2_RELEASE.md); later language/layout work is recorded in [GERMAN_UI_LAYOUT.md](GERMAN_UI_LAYOUT.md). Confirm the current About text in English/German and its keyboard/scroll behavior after link removal. Preserve accepted M18 performance deviations and coverage limits without reopening the completed campaign. A new behavior-changing correction requires affected tests, independent review, artifact identities and native qualification.

To install the verified 1.0.0 package locally, confirm Duckov is closed and use the authorized UDS-only `scripts/deploy.ps1 -DuckovPath $env:DUCKOV_PATH -PackagePath <verified-extracted-folder>`. It keeps a verified prior-mod copy outside the loader scan root and reads back the deployed hashes. Never restore or alter game saves as a package rollback. Final schema 1 starts fresh from RC/development schema 18. Delete obsolete local UDS data only with the user's explicit cleanup authorization, after inspecting the exact UDS-only paths and with Duckov closed; otherwise the mod preserves incompatible profiles in its archive. Valid schema-1 statistics remain compatible on reinstallation. Installation and publication are distinct; neither is implied by building the ZIP.

## Publish a future GitHub release

Prepare the exact release body, full tag target, ZIP and `.sha256` sidecar before asking the user to publish. No tag, draft release or upload is created by the preparation scripts.

When the user approves publication, use the chosen version for the tag/title and target the exact verified release commit. Set the pre-release flag for release candidates and clear it for final releases. Attach only the installable ZIP and checksum sidecar. Use that version's release notes, separately installed HarmonyLib dependency, native baseline, final validation results and retained M18 measurement/coverage limits. Link the release notes and evidence at the frozen source commit. Add the verified Workshop destination when available; do not invent an item URL or imply that GitHub publication alone establishes the supported Workshop baseline. The existing `v1.0.0` tag and assets remain unchanged.

After the user publishes a new release, verify the tag target, final-release flag and assets. Independently download both assets, calculate the ZIP hash, compare the sidecar, extract the exact payload defined at that tag and compare it with the local verified package. Record publication facts only after that readback. Never replace historical RC or final assets with a later package under the same tag.

## First Steam Workshop listing

The final version is 1.0.0; `publishedFileId = 0` remains correct until the first Workshop item is created. Do not fabricate an ID or create another listing for subsequent updates. Record the assigned ID, item URL, visibility and verified payload after creation, then use that same item for updates.

The source of truth for the thumbnail is [mod/preview.png](../mod/preview.png); title, short description, category tags and Workshop ID live in [mod/info.ini](../mod/info.ini). Replace/update those tracked files, validate, commit and push them before uploading. There is no separate `info.txt` and no manual copy into the installed mod folder. The chosen preview is a square PNG below 1 MiB. The prepared [description and translation invitation](WORKSHOP_DESCRIPTION.md) includes the preview's AI-art disclosure; use the full description on the Workshop page after creation.

Build and deploy from the repository with Duckov closed:

```powershell
./scripts/build.ps1 -DuckovPath $env:DUCKOV_PATH
./scripts/deploy.ps1 -DuckovPath $env:DUCKOV_PATH
```

If the ordinary Release assemblies have already been built and validated against the current source, `scripts/package.ps1 -DuckovPath $env:DUCKOV_PATH` can refresh a preview/metadata-only package before deployment. Packaging copies both tracked mod files into `artifacts/package/UltimateDuckovStatistics`; deployment copies and hashes all six permitted files into `<Duckov>/Duckov_Data/Mods/UltimateDuckovStatistics`. `preview.png` ends up beside `info.ini` and the two DLLs. Package verification rejects a missing preview and unapproved extra files; ZIP inventory checks use the same six-file contract. The published five-file GitHub v1.0.0 ZIP is not regenerated for this preparation.

Launch Duckov through Steam and open Mods, then the upload control on the local UDS entry. The first upload reads the local content folder and creates a Workshop item. On the installed Duckov 2.3.30 baseline, upload explicitly sets Public visibility. The uploader reads comma-separated categories, then issues a second tag assignment containing only `Mod`, so verify/correct the resulting listing categories instead of assuming `Quality of Life,Utility` survived. It sets the description from `info.ini` only on first creation.

The game writes the assigned `publishedFileId` into the installed `info.ini`. Record that exact ID in the tracked `mod/info.ini` and commit/push it before packaging or deploying again; otherwise a repository value of zero would overwrite it and a later upload could create a duplicate listing. Keep the repository's version and other intended metadata when transferring the ID, because the native writer can rewrite the installed INI. Record the item URL, visibility, separately installed HarmonyLib dependency `3589088839` and verified uploaded payload. Upload/creation is user-controlled.

Before testing subscription installation, close Duckov and remove the manually installed UDS mod folder so only one UDS copy is loaded. Preserve the separate statistics directory and game saves. Verify activation, dependency loading, Diagnostics version `1.0.0`, access and current-format data continuity from the actual subscribed payload. Record its identity before declaring a supported channel.

Item creation/upload, visibility, preview selection and subscription-install verification remain user-controlled. Once 1.0.0 is explicitly published through the verified Workshop channel, record that supported upgrade baseline and replace the preparation wording in README, INSTALL and this procedure with verified release facts. No development `0.x` build or RC creates a migration promise. Translation help is requested through Workshop comments; the mod and prepared listing have no Ko-fi or Discord links.

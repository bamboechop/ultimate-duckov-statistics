# About and community invitation batch

This batch is being prepared for the eventual `v1.0.0-rc.2`. It does not publish a release or a Workshop item. Its immutable base is `48428b32cf0108d59bd73c1a981a7acdc2078f68`, the merge of [Combat Statistics PR #21](https://github.com/bamboechop/ultimate-duckov-statistics/pull/21).

## Approved public content

Author: **bamboechop**. Fixed runtime destinations are declared together in [`CommunityLinks.cs`](../src/UltimateDuckovStatistics/UI/CommunityLinks.cs):

- Optional support: [Ko-fi](https://ko-fi.com/bamboechop).
- Community and translation contact: [Discord](https://discord.gg/8ngDVJ7jHH).

The author also welcomes translation volunteers through comments on the eventual Steam Workshop page. No Workshop item or item URL currently exists in this batch, so About links only to the two approved destinations above.

Invitation shared by About and the unpublished [Workshop description](WORKSHOP_DESCRIPTION.md): “Want to help translate UDS? Contact me on Discord and mention the language you could help with. Once the Steam Workshop page is available, you can leave a comment there too.”

Optional future promo caption: “Help translate UDS. Tell me your language on Discord or in a Workshop comment once the page is available.” No new promo image or translation infrastructure is needed for this demand test.

## Runtime scope

About uses appended enum identity 9 while Diagnostics retains identity 8. Navigation places About immediately before Diagnostics, leaving Diagnostics last. The earlier tab identities and order are preserved. Tab selection, keyboard cycling, the scrollable measured tab strip, downward focus navigation, visibility, resizing and shell disposal include About. Its static view does not read a profile, invoke adapters or construct statistics projections. The existing panel's generation and outside-raid access rules still apply; About does not bypass them.

The retained content uses the shell's native font/material, rounded surfaces, native button animation and feedback, and shared scrolling. Content height and button labels are measured with TMP and wrap as needed. The installed `ResourceHanRoundedCN-Medium SDF` font in `sharedassets0.assets`, object 174, gives About a 103.71875-pixel advance at the historical 36-pixel reference size and 77.7890625 pixels at the current 27-pixel tab size. Runtime localization measurements, rather than these English reference values, determine the live tab widths. No visual overflow assertion rejects the shell.

Installed `UnityEngine.CoreModule.dll` exposes public static `void UnityEngine.Application.OpenURL(string)`, implemented by the native `OpenURL` function. The contract probe verifies this signature without invoking it. Buttons alone dispatch fixed destinations; construction, selection, focus and ordinary ticks do not launch links. Launch exceptions show a local message while preserving the panel and focus. The void native API cannot prove that a browser or destination page successfully opened. No background request, embedded browser, telemetry, account or runtime dependency is added.

The accepted UI and Combat behavior, native capture, persistence, measurement limits, pre-v1 data separation and supported-channel decisions remain in scope only as unchanged dependencies. Base-distance tracking and background blur are separate work.

## Manual acceptance

Automated validation on 2026-09-10 passed 2,001 core tests, 63 ordinary shell tests and 67 diagnostic shell tests in each of Debug and Release. Native Debug/Release builds and the FrameTimeAnalyzer build passed with zero warnings. The installed contract probe passed, including `Application.OpenURL`. Changed-source formatting/analyzers, `git diff --check` and tracked source binary safety passed. Whole-solution formatting reports only whitespace findings in 13 untouched files, each verified identical to the immutable base; these pre-existing findings were not absorbed into this batch. Local logs and the baseline-file inventory remain in `artifacts/validation/` in the isolated worktree.

The shell regressions execute the production About view, tab shell and panel controller with isolated Unity/scroll/URL boundaries. They cover exact approved destination dispatch, passive construction/focus/ticks, hidden/disposed activation suppression, launcher exceptions and recovery, empty-profile/unavailable-adapter access, all ten tabs, repeated close/reopen, long localized text and keyboard scrolling/focus through resize. The 30-tick idle check verifies no additional objects or TMP measurements. Browser success, audio and native rendered appearance remain manual checks.

Only the user launches Duckov, selects saves and interacts with external sites. No profile or save manipulation is needed.

- At the usual size and a narrower supported window, reach all ten tabs with mouse, arrows and Ctrl+Tab / Ctrl+Shift+Tab. Confirm the earlier order and wrapping; About precedes Diagnostics.
- On About, inspect the description, attribution, invitation and optional support button. Check long localized tab/content/button labels, scrolling, clipping, native spacing and hover/click sounds. Move down from the tab into the scroll viewport, use up/down to scroll, move right into the link buttons and left back to scrolling, and return up to the tab.
- Use an existing empty profile and unavailable measurement states whenever the panel can open. Switch tabs repeatedly, close/reopen and resize; verify content stays available without duplicate controls or broken focus.
- Deliberately activate each approved external link once. Confirm its exact destination, return to Duckov and continue navigating/closing the panel. If launch fails, verify the panel remains usable and retry remains possible.

Automated boundary tests cannot establish native text rendering, audio, browser behavior or manual visual acceptance. Reuse the accepted M18 and Combat evidence for unchanged paths; this batch requires the affected checks above rather than another full gameplay campaign.


## Verified ordinary local test delivery — 2026-09-10

Implementation source `864005b5088668fd52e2635f097f5bf4a3db9dbb` produced `artifacts/rc2-about-community-864005b/UltimateDuckovStatistics-about-community-test.zip` under the original checkout. The ZIP is 640,877 bytes, SHA-256 `17052409c5a52043557ae8b2a28edbc7d4b64e0f96b090f2bce4f3ff490ad454`. The adjacent checksum sidecar and `package-evidence.json` record the archive and all five files. Independent extraction matched the package byte-for-byte and passed exact inventory, ordinary IL and privacy audits. The native DLL is SHA-256 `3d234092ff370c7163880699d973494eeee49f001aef34e75326a50dc151acda`; the unchanged Core DLL is `62db0d1def9d3e91f7391581e617a164b9b5d086dc1104e9c5ec52048ae6c688`.

Version fields remain `1.0.0-rc.1`, matching the existing ordinary-package contract; the source SHA and hashes identify this separate local test build. This is not an rc.2 release or a replacement published rc.1 asset. No tag, release or Workshop upload was created.

Deployment used the independently extracted package with Duckov closed. All five installed file lengths and SHA-256 hashes matched. The preceding mod remains in the isolated worktree at `artifacts/deployment-backups/66e117edf1fa4913b78eccddcddd50c5/UltimateDuckovStatistics`. Fresh before/after inventories verified all 3,116 save, UDS-data and protected original-checkout files unchanged. The original checkout's modified `PLAN.md` and untracked firing diagnostic were not included in this branch. Package and deployment evidence remain beside the ZIP. The later documentation-only delivery record does not change the packaged code or installation guide.

The manual checklist above remains open. Review and CI status are authoritative on the repository's [pull requests](https://github.com/bamboechop/ultimate-duckov-statistics/pulls).

# Diagnostics status width

The native disabled-Harmony repeat confirmed the actionable dependency explanation and corrected Overview World time layout. Its screenshot showed `Error · Harmony not loaded` wrapping despite unused space between the short system title and status. The accordion limited the status to 36% of the row width.

The status now uses space remaining after the native measured title and the existing insets and gap, up to its own preferred width plus padding. Both title and status keep their font size, wrapping and measured row height. Long titles and rows that cannot fit both labels retain a bounded wrapping allocation. The calculation is shared by system and Recent issues headers and contains no Harmony-specific text or reference-resolution widths.

This reversible layout correction adds no tests that duplicate its width arithmetic. Existing complete Debug/Release suites, native builds, changed-file formatting/analyzers, installed contract probing, independent production/native review and reproducible packaging provide delivery evidence. The shell suite does not render this child view; one-line native glyph acceptance remains a user check.

After deployment, briefly open slot 3 with HarmonyLib still disabled and confirm that the status fits on one line at the reported size. Then enable HarmonyLib, fully restart Duckov, and open slot 3 again. Confirm current tracking returns to Working and the recorded one sleep/59 minutes remains, export and close Duckov. Historical capture gaps must stay truthful. This repeats only the changed layout and pending dependency restoration; the already-observed degraded export, data retention, World time layout and quiet retries are preserved separately.

Delivery identities and completed checks are recorded in [the correction evidence](M18_DIAGNOSTICS_WIDTH_CORRECTION.json). M18 native performance and other outstanding qualification gates remain open.

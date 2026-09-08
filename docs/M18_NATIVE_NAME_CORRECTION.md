# Current-language native names

The English-language qualification exposed German recorded item labels in Equipment while Duckov's native menu was English. The accompanying 720x480 Diagnostics screenshot and user report establish usable layout at the smallest available windowed resolution. Original screenshots and hashes are recorded in [UI qualification](M18_UI_QUALIFICATION.json).

UDS captured native display strings when observations were recorded, then reused those historical strings in the UI. Presentation now resolves the exact recorded item, weapon, ammunition, map and enemy IDs against current native metadata. Crafting's numeric item IDs use the same native item registry. Overview highlights, recent run titles, route segments, item tables and equipment tooltips share this lookup. Saved identities, statistics and recorded labels are not rewritten; exports retain the captured labels.

Native localization is read only after Duckov initializes it. Missing metadata, untranslated keys, unsupported identities and ambiguous enemy tokens retain the recorded label. Root slots use the default character item prefab. Nested slot labels require the immediate parent item and exact slot key: the installed baseline uses different labels for the same `Special` slot key on different items. Structured loadout paths provide immediate parents; lifetime groups without that proof retain their recorded slot label. No item is instantiated.

Resolved names are bounded and cached for the panel lifecycle. Every native language event invalidates the cache and marks the open panel for its normal projection refresh; reopening also invalidates the cache. Disposal removes the language subscription. UDS interface translations and layout are outside this correction.

Installed Duckov 2.3.30 contracts were inspected independently, including the 156-entry enemy preset registry and 297 slot collections. The static probe covers the new localization and metadata accessors. Eight new presentation regressions cover the affected tabs and unchanged stored data. Nine new native-boundary/shell cases cover metadata fallback, exact IDs, ambiguous presets, parent-specific slots, cache invalidation, subscription cleanup and an open panel changing language without profile mutation.

Automated validation is separate from native acceptance. Corrected-package English/German switching, deployment and user confirmation remain pending. This correction does not complete M18's remaining recovery/degradation, input, performance or high-history qualification.

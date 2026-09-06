# M17 throwable tracking and Combat presentation

Throwable uses are recorded independently of gun firing, projectile completion and damage. One successfully completed `Skill_Grenade.OnRelease` inside a main-player `SkillBase.ReleaseSkill` in a raid counts as one throw/use. Multiple spawned grenades, repeated explosions and multiple targets do not multiply the count. This includes native grenades, mines and recoverable thrown items using that skill contract; character-only skills without an exact source item are excluded.

## Installed native evidence

Verified against Duckov 2.3.30, Steam build 24013657, by reading the installed assemblies. `CA_Skill.ReleaseSkill` rejects an inactive action, wrong skill type, insufficient stamina/cooldown and unfinished preparation before calling `SkillBase.ReleaseSkill`. The latter sets the source character, calls `OnRelease`, then invokes `OnSkillReleasedEvent`. `Skill_Grenade.OnRelease` creates and launches the configured projectiles, attaches `fromItem.TypeID` to their damage identity, and can create a recoverable pickup. `ItemSetting_Skill.OnSkillReleased` independently reduces the stack or destroys the consumed item where configured. Gun firing and generic Item Use completion callbacks do not prove this path.

The adapter snapshots the source item, generation, run, map and segment before release. A postfix marks successfully completed native grenade release; the outer finalizer publishes at most once, including when a later consumption subscriber throws. It returns the original exception unchanged. An exception before successful release provides no count and disables further tracking. Only matching generation/run/segment completion is accepted. Nested releases have separate observations. Disposal disarms in-flight scopes and uses the existing retryable Harmony cleanup owner.

Exact patch ownership is checked before and after each release. Missing Harmony, an incompatible baseline or conflicting patches publish an unavailable throwable capability. No gameplay action, projectile or inventory mutation is performed by UDS.

## Persistence and availability

The existing `ItemUseRecorded` event and item aggregation, run/segment publication, deferred persistence, deduplication and export paths are reused. `ActivationCount` represents release count. The additive `Throwable` effect tag identifies these records. No serialized member or schema-version change is needed. Observed stack decrease is stored separately as consumption; destroyed items retain the corresponding observed loss, and unproven consumption uses `UnknownAmount`. Recoverable releases can have a proven zero stack decrease without losing their use count. Generic item-use correlation excludes these skill items to prevent double counting.

Lifetime counts are explicitly **recorded** counts and remain Partial: older throws and any tracking gaps cannot be reconstructed. An older combat-only throwable without use records shows Unavailable for throws, never zero. Disabling the adapter preserves recorded values with Partial evidence and displays the current unavailable tracking notice.

## Combat

Weapons & ammunition includes throwable-use records even when no damage was caused. Canonical `duckov:item:N` use records join only `duckov:weapon:N` for that same positive native type ID; foreign, malformed and unknown identities never supply another weapon's counts. Existing historical combat entries can be classified using the exact registered prefab's `ItemSetting_Skill.Skill`, without instantiation or name/icon guessing.

Expanded throwables show Throws / uses, Kills by you and Damage dealt. Headshots and gun ammunition are omitted unless independently evidenced gun activity exists. The detail panel says ammunition is not applicable to throwables and explains the recorded-count coverage. Missing combat attribution stays Unavailable independently of proven release counts. Equipment stays focused on equipped time, slots and attachments. Item Use and its exports receive the same normalized uses and observed consumption.

## Manual qualification

With the game under user control, compare a before/after capture for: a damaging grenade, a grenade hitting nothing, a cancelled preparation, a mine, and a recoverable throwable where available. Each completed action must add one use; cancellation must add none. NPC use and base use must add none. Verify multiple explosion targets do not multiply uses. Inspect Combat, Item Use, run/segment item totals and export; reopen UDS and restart the game to check persistence. Verify historical counts remain unavailable/partial and exact item names/icons remain correct. Check missing/conflicting Harmony through deterministic tests rather than altering live saves. Automated callback tests use native-signature stubs and do not certify gameplay or screenshots.

# Configurable modifier shortcuts

Current source accepts one ordinary keyboard key, optionally combined with any of Ctrl, Alt and Shift. New settings and invalid-binding fallback use `Ctrl+Alt+S`. Valid saved settings, including the earlier F8 default, are preserved because the settings file cannot distinguish an automatically saved default from an explicit choice. Existing users can select the shortcut in Diagnostics to choose the new default themselves. No live settings or Duckov saves are changed during development.

## Input and persistence contract

- `settings.json` retains the existing `PanelHotkey` string and schema. Canonical values include `F8`, `Ctrl+S`, `Alt+Shift+K` and `Ctrl+Alt+Shift+S`; modifier order is Ctrl, Alt, Shift. Reading is case-insensitive, trims individual tokens and rejects duplicate/unknown modifiers, numeric enum values, missing keys and reserved main keys. Invalid values fall back to the default. Save failures keep the previous live binding.
- German labels display `Strg` and `Umschalt`; English displays `Ctrl` and `Shift`. Localization does not change the serialized binding. Diagnostics lays out long chords below the hint when the row would be too narrow.
- Matching requires the exact modifier set and the main key's key-down edge. A held key does not repeatedly toggle UDS. Adding modifiers after the main key was pressed does not activate the shortcut; release and press the main key again.
- Capture waits through modifier-only presses, ignores its opening frame, saves the next valid key with its held modifiers, and returns without toggling the panel. Escape cancels. Reset/restore/result modals retain input priority. Ctrl+Tab and Ctrl+Shift+Tab navigation are unchanged.
- Either Ctrl or Shift side works, and right Alt works in Alt-only/Alt+Shift chords. **Ctrl+RightAlt is reserved**, as Windows can synthesize that pair for AltGr and Unity's legacy input cannot prove which was intended. Use left Alt with either Ctrl for Ctrl+Alt chords. Explicit AltGr and Windows/Command states are also rejected. This does not promise that operating-system-reserved combinations reach the game.
- Native `DigitInputPanel` and `CustomFaceSlider` use `TMP_InputField`. The shortcut dispatcher checks the selected active TMP or ordinary Unity input field (including a selected child), and suppresses shortcuts while that field is focused. It does not scan every scene object or treat every open native view as typing. Arbitrary custom/IMGUI text editors from other mods are outside this check.
- `Application.isFocused` gates keyboard handling. After an observed focus loss, release held keys before input resumes. No global keyboard hook is installed. Existing native pause, cursor/input ownership, profile transitions and raid restrictions still govern activation.

## Validation and native acceptance

The shell tests run the production dispatcher and capture code against the Unity boundary fixture, including all eight modifier sets, exact mismatches, right-side variants, canonical persistence/reload, fresh/invalid settings, legacy F8, held keys, modal cancellation, text-field focus, focus return, pause ownership and raid restrictions. The installed-native probe checks the actual TMP/Unity input-focus properties used here. These are automated boundary/contract checks, not native keyboard or visual acceptance.

Local qualification on September 27, 2026 passed 2,519 main tests per Debug/Release configuration, 176 ordinary shell tests and 178 combined-diagnostic shell tests per configuration. Native Debug/Release builds completed with zero warnings/errors; the installed Duckov 2.3.30 contract probe, changed-source formatting/analyzers and 13-file ordinary package inventory/diagnostics-exclusion audit passed. No runtime or package version was changed, and the package has not been deployed or accepted in gameplay.

After integration and deployment, verify:

1. Existing F8 still works until changed in Diagnostics. Assign Ctrl+Alt+S using left Alt; verify both open and close at base, native pause/camera behavior and Escape.
2. Assign Ctrl+S, Ctrl+Alt+S and Ctrl+Alt+Shift+S in turn. Confirm bare S and extra/missing modifiers do nothing, holding the main key toggles once, and modifier-only capture waits without warning.
3. Cancel capture and a reset/restore confirmation; confirm no setting or profile mutation. Check Ctrl+Tab and Ctrl+Shift+Tab still navigate.
4. Test both Ctrl/Shift sides, right Alt without Ctrl, and German AltGr in a focused input field. Verify AltGr does not open UDS. Switch windows with the shortcut held, release, then press it again.
5. Restart Duckov and verify the chosen chord persists. Check English/German Diagnostics labels, capture text and a long chord at the smallest supported display size.

The first native acceptance for these changes remains pending. No new publication, gameplay acceptance or operating-system shortcut guarantee follows from a successful build.

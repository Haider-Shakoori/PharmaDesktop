# Medicine editor UI Automation integration

The medicine editor in `MedicinesView.xaml` is an **embedded Grid overlay** controlled by `IsEditorOpen`, not a separate WPF window. The overlay now has `AutomationProperties.AutomationId="Medicines_EditorOverlay"` and `AutomationProperties.Name="Medicine editor"`.

## FlaUI runner guidance (local automation project)

1. Attach to the verified Darmaltoon process and authenticated **main window**. Navigate to Medicines.
2. Invoke **Add medicine** once, then discard all pre-invocation UIA element references.
3. Poll a **fresh** main-window subtree (ControlView, then RawView when needed) for `Medicines_EditorOverlay` with a bounded timeout, e.g. 10 seconds and 100–200 ms interval. Do not enumerate top-level dialogs as the primary strategy.
4. Verify the overlay is visible/enabled and contains the expected brand-name input and save button. Search within that **fresh overlay subtree**; use accessible Name + ControlType first and AutomationId where present.
5. For debugging, compare the exact root, UIA view, thread, and search scope used by the successful inspector and the failing transaction runner. Capture exception details and the before/after UIA trees.
6. Perform three **open → type → read back → cancel** cycles before any save. Never invoke the opening button repeatedly during polling; never retry an uncertain save.
7. OBS must verify its Window Capture target after an authorized application restart.

## Important integration caveat

This GitHub branch is based on the repository's current `main`. The local RecordingReady-Accessibility-v2 build contains additional field-level accessibility changes not necessarily present in `main`. **Do not overwrite the local XAML file with this older version**. Cherry-pick or merge the minimal overlay-container attributes into the local version while preserving all local changes, and inspect the diff before building.

This patch improves overlay discoverability; it does **not** by itself prove the FlaUI runner's post-invocation synchronization is fixed. The local FlaUI runner is outside this repository and must be repaired/tested on the Windows machine.

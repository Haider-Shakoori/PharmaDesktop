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

## Additional editor overlays identified in source

The same `IsEditorOpen` + `GlobalBoolToVisibility` embedded-grid pattern exists in four additional views. This branch adds stable UIA container IDs and names:

| View | Overlay AutomationId | Accessible Name |
|---|---|---|
| Medicines | `Medicines_EditorOverlay` | Medicine editor |
| Customers | `Customers_EditorOverlay` | Customer editor |
| Inventory | `Inventory_EditorOverlay` | Inventory editor |
| Purchasing | `Purchasing_EditorOverlay` | Purchase editor |
| Expenses | `Expenses_EditorOverlay` | Expense editor |

All five use **embedded overlays** and should use the same fresh-main-window-subtree wait and control resolution strategy. The overlay's metadata does not replace field-level accessibility or a corrected FlaUI runner.

Other forms are **not the same architecture**: for example `Pos/PaymentWindow.xaml` is a standalone WPF window, and Returns and Daily Closing do not expose this exact `IsEditorOpen` overlay pattern in their XAML. They require their own architecture-aware inspection and tests, not a blind copy of the embedded-overlay fix. Do not mark them PASS until tested.

**Integration:** The local v3 build and uncommitted source can be ahead of GitHub `main`; merge only the missing XAML attributes, preserve existing accessibility IDs and labels, build a versioned executable, and perform three open/type/read/cancel cycles per overlay before authorized saves. Use dynamic process discovery and verify OBS Window Capture after switching. No force termination without specific approval for the verified PID.

# Codex Handoff: Unity Electrical Simulation SPICE Work

## 1. Project Goal And Current Stage

This repository contains a Unity electrical teaching simulation project. The SPICE workstream adds an isolated, local ngspice-backed DC circuit simulator without replacing the existing electrical-control simulator.

The current phase is **T4-A3-A: DC workspace central-canvas visual shell alignment**. The preceding mode entry, mode switching, state retention, host structure, popup menu, and T4-A2 region alignment work have been committed.

The current T4-A3-A implementation adds a control-style visual grid and matching background color below the SPICE wire/component/overlay layers. It does not change SPICE electrical behavior, coordinate conversion, host geometry, or the existing electrical-control workspace.

Do not start AC, transient analysis, save/load, Undo/Redo, new components, DemoSceneBuilder, or branch merging unless a later explicit task authorizes them.

## 2. Project Directory And Unity Version

- Worktree: `E:\Projects\Unity\ElectricalSimulation2D_SpiceT2`
- Unity: `2022.3.57f1c1`
- ngspice package used by the project: `E:\Projects\Unity\ngspice-45.2_64`
- T1 runtime files are kept under `Assets/StreamingAssets/ThirdParty/ngspice/`.

## 3. Current Git State

Verified at handoff creation:

- Branch: `feature/spice-t4a-dc-host-integration`
- HEAD: `610539937bf7c890b981c98b7355889dd1d4960c`
- HEAD subject: `fix(spice): add canvas renderer to DC grid`
- Working tree: clean (`git status --short` had no output).

Recent commits, newest first:

1. `6105399 fix(spice): add canvas renderer to DC grid`
2. `9ec298a fix(spice): align DC workspace presentation`
3. `9ad1e56 fix(spice): align DC host regions with control layout`
4. `570f8b4 refactor(spice): host DC mode in simulation shell`
5. `422f7f1 fix(ui): constrain simulation mode dropdown`
6. `7f25b35 fix(ui): render simulation mode menu above pages`
7. `2e69dc1 fix(spice): prevent host overlay input blocking`
8. `68c026d fix(spice): embed mode switching in simulation page`
9. `6d9f31a feat(spice): bind DC workspace to Demo simulation page`
10. `e319111 feat(spice): add DC simulation host controllers`

Existing worktrees include the main `develop/v2.4-stabilization` worktree, T1, and this T2/T4 worktree. Do not delete existing worktrees.

## 4. Completed Work And Verification Results

### T1: Local ngspice process bridge

- Fixed DC netlist execution through `ngspice_con.exe` was implemented and committed at `db7f72d` on `feature/spice-t1-ngspice-bridge`.
- T1 establishes the local process runner, output capture, fixed-netlist parsing, and StreamingAssets deployment path.

### T2 and T2.1: Structured DC core and topology invariants

- Pure-data `SpiceCircuitModel`, structured components/parameters/wires, graph construction, deterministic node names and SPICE names, DC netlist generation, result mapping, and diagnostics were implemented.
- DC support covers voltage sources, resistors, capacitors, inductors, and ground.
- SI units are the internal representation.
- Ground reachability is validated through a graph of electrical nodes connected by component edges; disconnected closed subnetworks are rejected before ngspice runs.
- Deterministic node naming uses the canonical, ordinal-sorted terminal identity rather than Union-Find root identity.
- T2.1 source commit recorded in earlier work: `ab5a63f89568a4e875afc01574b399831de35285`.

### T3 and T3.1-A: Standalone DC prototype and interaction

- The prototype provides drag/drop creation, movement, rotation, parameter editing, automatic orthogonal wiring, manual orthogonal waypoints, right-click waypoint undo, Esc cancel, diagnostics, generated-netlist display/copy, and DC results.
- SPICE component state is independent from Unity GameObjects and from the existing electrical-control workspace.
- Parameter/topology edits mark results and netlist stale; movement and rotation do not.
- Same-component terminal connections are rejected.
- The prototype uses `SpiceWorkspacePrototypeBootstrap` only for T3 test scenes. It must never be used by `Demo.unity`.

### T3.1-B: Embeddable host structure

- `SpiceWorkspaceController` accepts explicit `SpiceWorkspaceViewBindings`; it no longer owns global Canvas, EventSystem, Camera, or full application-page creation.
- T3 prototype infrastructure is serialized by editor tooling and kept separate from the production Demo host.
- `SpiceWorkspacePrototypeBootstrap` remains limited to the prototype and player-validation scenes.
- Player harness lifecycle was simplified to access `SpiceWorkspacePrototypeBootstrap.Controller` in `Start`, without a `Task.Yield` frame delay.

### T4-A1 / T4-A2: Demo host integration and mode switching

- Production host: `SpiceWorkspaceDemoHost`.
- Mode controller: `SimulationModeController` with `ControlCircuit` and `SpiceDc` modes.
- SPICE is initialized once and preserved in memory while hidden. Mode changes use `SetActive`; they do not change scene, rebuild the workspace, or share business state.
- The Simulation mode menu is a global popup layer and was manually accepted: it closes on outside click/navigation, switches either mode, and remains in bounds at 3840x2160, 1920x1080, and 1366x768.
- Control and SPICE roots are independent. Existing `WorkspaceController`, `WireManager`, control components, control wires, templates, validation, and save data are not reused by SPICE.
- `LeftPaletteCollapseHandle` is hidden in SPICE mode and restored in control mode.

### T4-A3-A: Central workspace visual shell

Committed in two forward-only commits:

- `9ec298a fix(spice): align DC workspace presentation`
  - Adds `SpiceGridVisual` as the first child of `SpiceWorkspaceViewport`.
  - Uses the existing pure-visual `WorkspaceGrid` component.
  - Copies the existing control Workspace background `Image.color` to the SPICE viewport.
  - Keeps the viewport Image raycastable as the SPICE input surface; the grid itself has `raycastTarget = false`.
- `6105399 fix(spice): add canvas renderer to DC grid`
  - Adds the required `CanvasRenderer` to `SpiceGridVisual` in both editor creation and the serialized scene.
  - Prevents the `MissingComponentException` from `RectMask2D` clipping a `MaskableGraphic`.

Verification already completed during the prior session:

- `git diff --check` passed before both commits.
- T3 editor workspace mapping/invalidation validation passed after the visual-shell change.
- The project compiled successfully in Unity batch mode as part of that validation.
- No Unity Editor, `ngspice_con.exe`, or `taskkill.exe` process remained after those batch runs.

## 5. Current Problem Being Handled

There is no uncommitted source or scene change at handoff time.

The last reported runtime error was:

`MissingComponentException: There is no 'CanvasRenderer' attached to the 'SpiceGridVisual' game object.`

It was caused by creating a UGUI `MaskableGraphic` (`WorkspaceGrid`) without its required `CanvasRenderer`. Commit `6105399` contains the forward fix. **Manual Unity Play Mode confirmation after this commit is still required**: open `Demo.unity`, enter Play, switch to Basic Circuit Principles / SPICE mode, and confirm the exception no longer appears.

## 6. Remaining Work

### Immediate manual acceptance for T4-A3-A

1. Open `Assets/Scenes/Demo.unity` and enter Play Mode.
2. Switch to the SPICE DC mode from the Simulate Circuit mode menu.
3. Confirm no `MissingComponentException`, especially no CanvasRenderer or RectMask2D error.
4. Confirm the SPICE central viewport has the light background and grid below wires/components.
5. Confirm the grid does not block drag/drop, blank-click cancellation, terminal click, automatic wiring, or manual waypoint wiring.
6. Check at 3840x2160, 1920x1080, and 1366x768 if available.
7. Confirm control mode remains visually and behaviorally unchanged.

### Later tasks that are explicitly not started

- T4-A3-B: formal toolbar and component-palette visual alignment.
- T4-A3-C: formal SPICE assistant visual alignment.
- Any AC, transient, waveform, save/load, template, Undo/Redo, new component, or merge work.

## 7. Confirmed Technical Decisions And Prohibitions

### Architecture boundaries

- SPICE uses its own `SpiceWorkspaceController.Components` and `.Wires` as the only SPICE workspace facts.
- SPICE does not use `WorkspaceController.Components`, `WireManager.Wires`, `RuntimeStateManager`, `DrawingDto`, existing RuleId values, `LocalInspectorPanel`, or existing control-simulation save data.
- `SpiceWorkspacePrototypeBootstrap` belongs only to T3 prototype/player-validation scenes. It must not be placed in `Demo.unity` or extended with a production mode.
- The production `SpiceWorkspaceDemoHost` receives explicit scene references and initializes the controller once.
- Do not use `FindObjectOfType`, `GameObject.Find`, scene scans, a global singleton, or automatic creation of missing production roots.
- Do not create a second EventSystem or camera in Demo. The only added popup Canvas is `GlobalPopupLayer`, with `overrideSorting = true` and sorting order 100.

### UI and coordinate rules

- Mode selection belongs under the existing top "模拟电路" navigation item; do not reintroduce a page-right mode selector.
- The global NavBar remains visible in both modes.
- SPICE and control modes share visual regions but not controllers or circuit data.
- SPICE component positions, manual wire waypoints, previews, and route rendering use `SpiceWorkspaceViewport` local coordinates.
- Do not change SPICE coordinate conversion, wire routing, component positions, or host geometry merely to accommodate a visual background.
- `SpiceGridVisual` is a visual-only first sibling, behind `SpiceWireLayer`, `SpiceComponentLayer`, and `SpiceOverlayLayer`. It has `WorkspaceGrid`, `CanvasRenderer`, and `raycastTarget = false`.
- The parent `SpiceWorkspaceViewport` Image remains raycastable because it supplies SPICE blank-click/input behavior.

### Explicitly prohibited unless later authorized

- Do not modify `WorkspaceController`, `WireManager`, `RuntimeStateManager`, control analyzer/validation systems, RuleId, save/load, DrawingDto, templates, snapshots, Runtime Catalog, `VisualPrefabRegistry`, `ComponentVisualRuntimeCatalog`, `DemoSceneBuilder`, `ImportUIAssets`, or formal Build Settings.
- Do not run `DemoSceneBuilder`, `BindDemoScene`, or `SpiceT3PrototypeTools.CreateScene` while editing the production Demo scene.
- Do not use `git reset --hard`, `git restore .`, `git checkout .`, `git clean`, automatic stash, or deletion of other worktrees.
- Do not start T4-B, AC, transient simulation, waveform work, saving, importing, templates, or a broad UI refactor.

## 8. Important Files And Code Paths

### SPICE core and infrastructure

- `Assets/Scripts/Spice/Core/`
- `Assets/Scripts/Spice/Topology/`
- `Assets/Scripts/Spice/Netlist/`
- `Assets/Scripts/Spice/Results/`
- `Assets/Scripts/Spice/Infrastructure/NgspiceProcessRunner.cs`

These are stable in the current visual phase and should not be changed for T4-A3-A.

### SPICE workspace and UI

- `Assets/Scripts/Spice/Workspace/SpiceWorkspaceController.cs`
- `Assets/Scripts/Spice/Workspace/SpiceWorkspaceViewBindings.cs`
- `Assets/Scripts/Spice/Workspace/SpiceWorkspaceDemoHost.cs`
- `Assets/Scripts/Spice/Workspace/SimulationModeController.cs`
- `Assets/Scripts/Spice/UI/`
- `Assets/Scripts/Spice/Workspace/SpiceWorkspacePrototypeBootstrap.cs` (prototype-only)

### Demo integration and visual grid

- `Assets/Scenes/Demo.unity`
- `Assets/Editor/SpiceT4/SpiceT4DemoIntegrationTools.cs`
- `Assets/Scripts/UI/WorkspaceGrid.cs`
- `Assets/Scripts/UI/MainUiTheme.cs`
- `Assets/Scripts/UI/PaletteController.cs`

### Tests and prototype tools

- `Assets/Tests/SpiceT3/`
- `Assets/Editor/SpiceT3/SpiceT3PrototypeTools.cs`
- `Assets/Tests/SpiceT3/SpiceT3WorkspacePrototype.unity`
- `Assets/Tests/SpiceT3/SpiceT3PlayerValidation.unity`

## 9. Known Risks And Pitfalls

1. **UGUI graphic requirement:** any object with `WorkspaceGrid` or another `MaskableGraphic` must have `CanvasRenderer`. Under `RectMask2D`, omission causes a `MissingComponentException` during clipping.
2. **Do not disable the viewport Image raycast target:** only the grid is non-interactive. Turning off the viewport Image can break SPICE blank-click cancellation and input handling.
3. **Unity scene-save noise:** Unity can add trailing whitespace throughout `Demo.unity`. Do not commit that noise. The prior controlled approach removed only trailing spaces and normalized line endings after a narrowly scoped Editor save, then reviewed the semantic diff. Never hand-edit scene YAML fields.
4. **Project lock:** Unity batch runs can fail with "Project already open" if a Unity Editor instance or a just-exiting batch instance still owns the project. Verify the editor is closed and wait for it to exit before running a second batch command.
5. **Existing T4 geometry assertion:** `Tools/Spice/T4/Validate Demo DC Workspace Binding` currently reports a pre-existing mismatch because it expects the SPICE Workspace right offset `-344`, while the scene reports `-332`. This was not changed in T4-A3-A because the visual task explicitly prohibited host geometry changes. Do not silently alter geometry under a visual task; resolve it only in a separately authorized geometry task.
6. **T4 static validation and visual grid:** the T4 validation tool now expects `SpiceGridVisual`, its `WorkspaceGrid`, `CanvasRenderer`, first-sibling order, `raycastTarget = false`, matching background color, and a viewport `RectMask2D`. The existing geometry assertion runs earlier and can still stop the validation before these newer checks.
7. **Global popup behavior:** `GlobalPopupLayer` must stay a final child of the main Canvas, have its own `Canvas` with `overrideSorting = true` / order 100, no mask, and no permanent full-screen blocker while its menu is closed.
8. **Manual acceptance is still essential:** the T4-A3-A grid fix has a committed source/scene change, but visual appearance, input behavior, all target resolutions, and control-mode regression need manual confirmation after `6105399`.

## 10. Recommended Next-Step Order

1. Start with the Git checks in section 11. Stop if tracked changes exist.
2. Open `Demo.unity` in Unity 2022.3.57f1c1 and perform the T4-A3-A manual acceptance steps in section 6.
3. If the CanvasRenderer exception is gone and grid/input behavior is accepted, record the result and treat T4-A3-A as closed.
4. Do not start a later visual batch automatically. Wait for explicit approval and scope for T4-A3-B or T4-A3-C.
5. If a task explicitly authorizes resolution of the existing `-332` vs `-344` geometry mismatch, first perform a read-only four-region geometry audit and then make only the approved targeted change.

## 11. Mandatory Git Checks For A New Conversation

Run these in `E:\Projects\Unity\ElectricalSimulation2D_SpiceT2` before making any change:

```powershell
git branch --show-current
git rev-parse HEAD
git status --short
git log -5 --oneline
git worktree list
git diff --check
```

Expected handoff baseline:

```text
feature/spice-t4a-dc-host-integration
610539937bf7c890b981c98b7355889dd1d4960c
<no output from git status --short>
```

If the working tree is not clean, report the exact files and stop. Do not stash, reset, restore, clean, delete, or overwrite anything automatically.

## 12. Long-Term User Requirements Stated In This Conversation

- Work in isolated Git branches/worktrees and make small, independently verifiable commits.
- Preserve existing formal electrical-control functionality and keep SPICE data/logic isolated from it.
- Never modify prohibited business systems merely for convenience.
- Never use SceneManager to switch to a separate SPICE scene for the formal Demo integration; the two modes must remain inside `Demo.unity` / `SimulationPage`.
- Keep the global NavBar visible in both modes.
- Use explicit serialized references for production UI binding; do not search the scene by name or type.
- Treat external process, paths, files, ngspice stdout/stderr, cancellation, and numeric parsing as real defensive boundaries. Do not hide failures with default values or swallowed exceptions.
- Use SI doubles internally for SPICE parameters; do not store free-form unit strings as core data.
- Parameter/topology changes stale results; moving and rotating components do not stale results.
- Do not introduce AC, transient, waveforms, save/load, templates, import/export, Undo/Redo, new components, or a large UI/architecture rewrite without explicit authorization.
- Do not run `DemoSceneBuilder` or rebuild production scene structures unless a task explicitly requests it.
- Before a commit, inspect `git diff --check`, status, stat, and changed file names; do not mix UI importer noise, temporary logs, test builds, templates, or unrelated scene changes into a feature commit.
- Stop for manual acceptance at stated phase boundaries rather than silently continuing into the next phase.

## Handoff Completion Status

This document was created while the repository was clean at commit `6105399`. Creating this file itself makes the working tree dirty until it is reviewed and committed. The only intended uncommitted file after creation is `CODEX_HANDOFF.md`.

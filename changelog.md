# Changelog

Every change made to this project is recorded here, grouped per conversation. Newest conversations go on top.

---

## Conversation 2 — 2026-09-18 (cleanup)

**What happened:** prototype accepted as good enough; codebase slimmed down per user request — `Tools > PCGML Scenario` now shows only the Generator Window, and all mock/offline plumbing is gone.

### Changed

- **Deleted** (files + .meta):
  - [ScenarioPrefabFactory.cs](Assets/Scripts/Scenario/Editor/ScenarioPrefabFactory.cs) — "Create Prefab Library & Catalog" menu + prefab-materialization logic (catalogs are now created manually via the project context menu: right-click → Create → PCGML/Scenario → Object Catalog).
  - [ConstructionCatalogBuilder.cs](Assets/Scripts/Scenario/Editor/ConstructionCatalogBuilder.cs) — "Build Construction Catalog" / "Merge Construction into Scenario Catalog" menus (user fills catalog entries by hand in the Inspector).
  - [ScenarioPackTools.cs](Assets/Scripts/Scenario/Editor/ScenarioPackTools.cs) — "Place Construction Pack Scenery (scale 0.2)" menu.
  - [ScenarioMenu.cs](Assets/Scripts/Scenario/Editor/ScenarioMenu.cs) — M0/M1/M3 menu checks and validator self-tests.
  - [MockLLMClient.cs](Assets/Scripts/Scenario/MockLLMClient.cs) and the whole `Examples/` folder (3 mock JSONs) — mock mode removed everywhere.
- **Mock removal:** [RuntimeScenarioUI.cs](Assets/Scripts/Scenario/RuntimeScenarioUI.cs) dropped the `mockToggle` field and always uses `DeepSeekClient`; [ScenarioGeneratorWindow.cs](Assets/Scripts/Scenario/Editor/ScenarioGeneratorWindow.cs) dropped the "Mock (offline)" toggle, its EditorPrefs key, and the client-selection block (always `DeepSeekClient`). `ILLMClient` stays as the swap point for a future Python middleman.
- **Kept:** `Tools > PCGML Scenario > Generator Window` only, plus its dependencies (PromptBuilder, ScenarioGenerator, ScenarioValidator, ScenarioBuilder, DeepSeekClient, ObjectCatalog + defaults, PrimitiveFactory as the no-prefab fallback).
- **Comment fixes** in PrimitiveFactory.cs, ScenarioValidator.cs, ILLMClient.cs (referenced the deleted tools).
- Grep-verified: no remaining references to mock or the deleted classes/methods.
- **Added (2026-09-18):** mock data-flow documentation in `Assets/mock data/`:
  - `out.json` — mock of the request body POSTed to `api.deepseek.com/chat/completions` (model, condensed system prompt with rules + catalog + example, user message, temperature, max_tokens, `response_format: json_object`, `thinking_mode: non-thinking`).
  - `in.json` — mock of the response (OpenAI envelope; `choices[0].message.content` = the CS-ISR JSON as a string, plus `_readable_content` = the same payload parsed for humans).
  - `prompts.pdf` (+ source `prompts.txt`) — 5 construction-scenario prompts (novice/expert/intermediate/hazard-rich) with expected outcomes and difficulty-detection notes; PDF generated via macOS `cupsfilter`.
- **Added (2026-09-18):** [FreeFlyCamera.cs](Assets/Scripts/FreeFlyCamera.cs) — free-movement camera to attach to the camera GameObject. WASD move (camera-relative), Space/Ctrl up/down, Shift fast, hold RMB + drag to look (cursor locks), mouse wheel adjusts speed. Uses the legacy Input Manager (works because Active Input Handling = "Both"). The speed overlay sits below Player.cs's FPS counter.
- **Building footprint corrected (2026-09-18):** user measured the real building with a temporary measure tool: world size 12.30 × 10.30 m = **6.2 × 5.2 cells → footprint must be 7×6 cells** (the old 4×4 assumption is why cranes landed "inside" the building). Changes: [ScenarioBuilder.cs](Assets/Scripts/Scenario/ScenarioBuilder.cs) now places every object at the **center** of its covered footprint (and the player at cell center) so big models visually occupy exactly the cells they claim; [PromptBuilder.cs](Assets/Scripts/Scenario/PromptBuilder.cs) few-shot example recomputed for a 7×6 building at (2,2) with margins: building covers x2..8/y2..7, mounted crane on the building, materials/trucks on the bottom strip, container on the left strip, route (0,0)→(9,9) via the left/right columns — verified collision-free and reachable. ⚠️ User action: set `footprintX = 7`, `footprintY = 6` on the `building` entry in `Construction.asset` (Inspector). The temporary measure tool was deleted. Note: a 7×6 building on the 10×10 grid leaves only 3–4-cell margins — if sites feel cramped, scale the building prefab down to ~6×5 cells instead of shrinking footprints further.
- **Fence made implicit site scenery (2026-09-18):** the fence prefab is the whole square perimeter (~site size), so grid placement put one corner in the middle of the site. Now [ScenarioBuilder.cs](Assets/Scripts/Scenario/ScenarioBuilder.cs) auto-places it once, centered on the site (9,0,9) — like the floor slab — and the schema rejects `fence` in the LLM's object list (with `ground`/`floor_slab`), prompt rule 9 updated, mock `out.json` doc updated. The fence entry stays in `Construction.asset` (its prefab link drives the auto-placement); its footprint fields are now irrelevant. If the square doesn't hug the 20×20 m slab edges at this scale, adjust the scale inside the fence prefab.
- **Data-flow transparency logs (2026-09-18):** [DeepSeekClient.cs](Assets/Scripts/Scenario/DeepSeekClient.cs) now logs every call: `[Scenario] OUT -> DeepSeek (endpoint, model): <full request body>` before sending, `[Scenario] IN <- DeepSeek (<N> chars): <full response body>` on every 2xx response, and `[Scenario] IN <- DeepSeek HTTP <status>: <excerpt>` for error statuses. Combined with the generator's existing per-attempt logs (`attempt N: received …`, `attempt N validation: X error(s)`, the first 10 issue lines, "sending structured errors back for repair"), a full repair run (up to 3 attempts incl. retries) reads as repeated OUT → IN → validation → repair-prompt-OUT pairs in the Console, with the repair prompt visible inside each OUT body.
- **Added (2026-09-18):** [documentation.md](documentation.md) — the working reference doc: (1) the full Generate flow as an ordered function-call trace (RuntimeScenarioUI → PromptBuilder → DeepSeekClient → ScenarioGenerator → validators → ScenarioBuilder), (2) a per-script reference table, (3) conventions & gotchas (grid/footprint rules, key resolution, known pitfalls). Written so future sessions can pick the project up from this file.

---

## Next steps (live list, 2026-09-17)

All C# code for M0–M4 is written. What's left is editor-side work in Unity, in this order:

### A. Compile + offline milestones (no API key needed)

1. Open the project in Unity 6 (`6000.3.24f1`) via Unity Hub → Add → `/Users/mac/Documents/prop/demo_a`. Let it compile — the CS8361 from Conversation 1 should be gone.
2. **Build the prefab library:** menu `Tools > PCGML Scenario > Create Prefab Library & Catalog` → creates `Assets/Prefabs/Scenario/*.prefab` (primitive placeholders) + `ObjectCatalog.asset` (the type → prefab mapping).
3. **Run validator self-tests:** `Tools > PCGML Scenario > Run Validator Self-Tests` → expect `10 passed, 0 failed` in the console.
4. **M1 (offline end-to-end):** `Tools > PCGML Scenario > M1: Mock End-to-End Test` → console should log `ACCEPTED` (mock JSON → validate → repair loop, no network).
5. **M3 (scene build):** `Tools > PCGML Scenario > M3: Build Example Scene (Mock)` → objects appear in the scene (floor slab, opening with red frame, barrier, stack, worker spawn). Visual check.

### B. M0 — real API smoke test

1. `Tools > PCGML Scenario > Generator Window` → **Settings** tab → paste the API key → **Save Settings** (stored in EditorPrefs, never in a file).
2. **Model name (verified 2026-09-17):** keep `deepseek-v4-flash` — it is the current OpenAI-compatible model id; legacy `deepseek-chat` was retired 2026-07-24. DeepSeek released V4.1 Flash on 2026-09-10 as `deepseek-flash` (v4-flash kept as alias). If the smoke test returns "unknown model", try `deepseek-flash` in the Model field.
   - **Clarified 2026-09-17:** the `ANTHROPIC_BASE_URL=https://api.deepseek.com/anthropic` env used by Claude Code is a *different gateway* (Anthropic Messages protocol) than the Unity client's `https://api.deepseek.com/chat/completions` (OpenAI protocol). The Unity client intentionally uses the OpenAI endpoint: task.md §7 specifies it, and the pipeline depends on its JSON mode (`response_format: {"type":"json_object"}`, task.md D5). The `[1m]` model suffix belongs to the Anthropic gateway only; the OpenAI endpoint takes `deepseek-v4-flash`. The same API key works on both — set it in Generator Window → Settings.
3. **Test Connection (hello world)** in the Settings tab, or menu `Tools > PCGML Scenario > M0: DeepSeek Smoke Test`.
4. Then **Generate** tab → untick *Mock* → **Generate Variants** (target ≤ 10 s per variant; timing is logged).
5. Optional hardening (later): send `"thinking_mode": "non-thinking"` explicitly in `DeepSeekClient` (task.md §6); the API default is already non-thinking for flash.

### C. Editor how-to — GameObjects, 3D models, prefabs

**Putting GameObjects in the scene**
- Hierarchy window → right-click → `3D Object` → Cube / Sphere / Capsule / Plane / Quad — or menu `GameObject > 3D Object`.
- Select it → Inspector: position / rotation / scale.
- Project convention: grid cell `(x, y)` → world position `(x * 2, 0, y * 2)` (2 m cells, floor at y = 0). Objects must sit on the floor: set local position y so the bottom is at 0.

**Importing 3D models**
- Drag a `.fbx` / `.obj` / `.gltf` / `.blend` file into a Project folder (suggest `Assets/Models/`).
- Select the imported file → Inspector → **Model** tab: check **Scale Factor** (FBX from Blender often exports at 0.01 — set to 1 and scale down in the scene instead if needed).
- **Materials** tab → *Extract Materials* if the model lost its textures.
- Drag the model from the Project window into the Hierarchy to place it.

**Making prefabs**
- Drag any GameObject from the **Hierarchy** into a **Project** folder (e.g. `Assets/Prefabs/Scenario/`) → it becomes a `.prefab` (blue cube icon).
- Compound prefabs: put child objects under an empty parent (`GameObject > Create Empty`) first, then drag the parent.
- **Edit a prefab:** double-click it → Prefab Mode (changes apply to every instance), or edit an instance in the scene and click **Overrides → Apply All** in the Inspector.
- **Swapping primitive placeholders for real models later (recommended):** enter Prefab Mode on e.g. `guardrail.prefab`, delete the primitive children, drag your model in as a child, save. The catalog mapping (type → prefab) is untouched, no code change.
- ⚠️ Do **not** re-run *Create Prefab Library & Catalog* after swapping visuals — it overwrites the prefabs with primitives again.
- Keep model origin at floor level and footprint ≤ 2×2 m so cell placement stays correct; `rotation_y` (degrees around Y) is applied at placement.

### D. Housekeeping

- **git:** the project is not a git repo yet. Recommended: `git init` + a Unity `.gitignore` (Library/, Temp/, Logs/, UserSettings/, Assets/Scenarios/ outputs optional) **before** any API-key config exists. The key must never be in a file: EditorPrefs or git-ignored `~/deepseek_config.json` only.
- The API key was pasted into a chat message — consider rotating it in the DeepSeek console if that log is shared with anyone.
- Generated outputs land in `Assets/Scenarios/` as `.asset` + `.json` pairs (provenance) when using **Save** in the generator window.

### E. First run — how to launch the app (2026-09-17)

**How to run:** this slice is editor-tool-driven — nothing auto-spawns at Play. Sequence:

1. Open `Assets/Scenes/SampleScene.unity` (already has Main Camera + Directional Light).
2. `Tools > PCGML Scenario > Create Prefab Library & Catalog` (once) — creates the prefabs + `ObjectCatalog.asset`.
3. `Tools > PCGML Scenario > M3: Build Example Scene (Mock)` — instantiates the scenario into the open scene (floor, opening with red frame, barrier, stack, worker spawn at start cell). Alternative: Generator Window (mock on) → Generate Variants → Build Scene From Last.
4. Press ▶ **Play**.

**What goes in the hierarchy:** almost nothing. The only MonoBehaviour meant for the hierarchy is [Player.cs](Assets/Scripts/Player.cs) — an FPS overlay. Attach it to the Main Camera (or any active GameObject). `ScenarioGenerator`/`ScenarioBuilder`/validators are plain C# classes invoked from the `Tools > PCGML Scenario` menu and the Generator Window; `ObjectCatalog` is a ScriptableObject *asset*, not a hierarchy object. Do **not** try to drag those onto GameObjects.

**Camera framing:** the floor spans world (0,0,0)–(18,0,18), center (9,0,9). Point the Main Camera there, e.g. Position (9, 15, 26), Rotation (25, 180, 0).

**Known gap (found 2026-09-17):** `Packages/manifest.json` has no `com.unity.render-pipelines.universal` (nor inputsystem), although the template's URP assets exist (`Assets/Settings/*RPAsset`) and GraphicsSettings references a URP pipeline asset. Effect: the scene's Global Volume component may log "missing script", and rendering falls back to built-in (PrimitiveFactory's shader fallback covers the materials either way). Fix when convenient: **Window > Package Manager → Packages: Unity Registry → search "Universal RP" → Install** (auto-selects the 6000.3-compatible version); the template settings then light up. No player movement controller exists yet (runtime controller = M5) — Play mode is a visual check for now.
- ✅ 2026-09-18: URP 17.3.0 installed by the user and the pipeline asset is assigned in Graphics Settings.

### F. Play-mode generation UI (2026-09-18)

The editor window path works; for generation inside Play mode, build this small UI (script already written: [RuntimeScenarioUI.cs](Assets/Scripts/Scenario/RuntimeScenarioUI.cs)):

1. **Install/refresh uGUI** — `com.unity.ugui` 2.0.0 was added to the manifest; let Unity resolve it (focus the editor, Package Manager resolves automatically).
2. **Create the Canvas:** right-click in the Hierarchy → `UI > Canvas` (also auto-creates an EventSystem). Use the **Legacy** UI items (the script uses `UnityEngine.UI`, not TMP):
   - Right-click the Canvas → `UI > Legacy > Input Field` → name it **PromptInput**, widen it (e.g. Rect Transform width 600).
   - Right-click the Canvas → `UI > Legacy > Button` → name it **GenerateButton**, set its child Text label to "Generate".
   - Right-click the Canvas → `UI > Legacy > Text` → name it **StatusText**, small font size, left-top aligned, Rect width ~600 height ~200.
   - Right-click the Canvas → `UI > Legacy > Toggle` → name it **MockToggle**, label "Mock (offline)", leave it ON for first tests.
3. **Attach the script:** select the Canvas → Add Component → **RuntimeScenarioUI**, then drag into its slots:
   - Prompt Input ← the PromptInput InputField
   - Generate Button ← the GenerateButton Button
   - Status Text ← the StatusText Text
   - Mock Toggle ← the MockToggle Toggle
   - Catalog ← `Assets/Prefabs/Scenario/ObjectCatalog.asset` (drag from the Project window)
   - Difficulty ← novice (default)
4. **Press ▶ Play** — type a prompt, click Generate. With Mock ON it runs fully offline (cycles the 3 example JSONs, all pass validation). With Mock OFF it calls DeepSeek using the key saved in the generator window's Settings tab (editor Play mode).
5. Each press rebuilds `ScenarioRoot` — a new variant replaces the previous scene. The full validation log also streams to the Console.

**Input handling fix (2026-09-18):** the template set `activeInputHandler: 1` (Input System only) but `com.unity.inputsystem` is not installed, so uGUI's StandaloneInputModule throws `InvalidOperationException` ("You are trying to read Input using the UnityEngine.Input class…") at Play. Fix: **Project Settings → Player → Other Settings → Active Input Handling → "Both"** (editor restarts). Long-term alternative for the M5 player controller: install `com.unity.inputsystem` and use `InputSystemUIInputModule` on the EventSystem.

### G. Real 3D assets — Majadroid pack (2026-09-18)

The CC0 pack `LowPoly-House-Construction-Site-By-Majadroid` was downloaded (itch.io, by Maik Hoffmann; palette by Imphenzia). FBX files are version 7400, `UnitScaleFactor = 100` (cm) → keep Unity import defaults (Scale Factor 1, Convert Units on) to get meters.

**Mapping to the catalog:**
- `guardrail` ← `Fence.fbx`
- `material_stack` ← `Construction-Materials.fbx` (boxes/planks/barrels)
- scenery (manual placement, NOT catalog types): `Building.fbx` = the unfinished building; `Ground/Road/Ramp`, `Containers*`, `Crane-*`, `Trucks` = backdrop
- delete/ignore `_HouseConstructionSite-AllInOneObject_.fbx` (whole pack in one object) and the `.blend` sources (Unity cannot import them without Blender)
- **not in the pack:** pallet, forklift, worker, jersey barrier → those prefabs keep their primitives for now (concrete_barrier's gray block already reads as a barrier)

**Swap procedure:** copy used FBX to `Assets/Models/ConstructionPack/`; per prefab enter Prefab Mode, delete primitive children, drag model in as child, align origin at floor y=0 / centered / facing +Z / footprint ≤ 2×2 m, replace imported colliders with one Box Collider, assign a `.mat` from `Assets/Prefabs/Scenario/Materials/` if colors come in gray; exit Prefab Mode. **Never re-run "Create Prefab Library & Catalog" afterwards** — it overwrites with primitives. The pack's colors use an Imphenzia palette (Blender materials) and may not transfer — tinting with our flat .mat assets matches the project style.
- **Scale confirmed by user (2026-09-18):** `Fence.fbx` at local scale **0.2, 0.2, 0.2** reads correctly against the 20 m floor slab. Use 0.2 as the starting scale for the other pack models too (same scene units), and verify each visually. Watch the fence row length at 0.2 against the 2 m cell — if it spans more than one cell, cut the FBX into a shorter segment or accept the visual overlap for the prototype. User note: the fence model is the *site perimeter fence* — good as scenery around the slab; a shorter segment would suit the per-cell `guardrail` prefab better (optional refinement).
- **Added (2026-09-18):** [ScenarioPackTools.cs](Assets/Scripts/Scenario/Editor/ScenarioPackTools.cs) — menu `Tools > PCGML Scenario > Place Construction Pack Scenery (scale 0.2)` instantiates every FBX from the pack folder (except the AllInOne file) into the open scene at scale 0.2 under a `ConstructionPackScenery` root, spaced in a row at z = −5 (south of the slab) for review; re-running replaces the group.
- **Added (2026-09-18):** the user made prefabs of all 12 pack objects in `Assets/Prefabs/Construction/` and an empty `Construction.asset` catalog. [ConstructionCatalogBuilder.cs](Assets/Scripts/Scenario/Editor/ConstructionCatalogBuilder.cs) — menu `Tools > PCGML Scenario > Build Construction Catalog` — fills it: one `CatalogEntry` per prefab with stable type ids (`building`, `crane_mounted`, `crane_ground`, `fence`, `ground`, `road`, `ramp`, `trucks`, `containers`, `containers_cargo`, `containers_office`, `construction_materials`), display names, and one-line safety roles reflecting the user's visual descriptions (crane_ground as tall as the building; crane_mounted on the building, slightly taller; ground spans the fenced site; road runs through the fence). Note: several are site-scale (building, cranes, ground, road) — placing them via the 2 m grid is mechanically fine but visually overlaps many cells; treating them as scenery (manual placement) or teaching the LLM that they occupy multiple cells is a future refinement. To make the LLM generate with these types, assign `Construction.asset` to the catalog slot in RuntimeScenarioUI (or merge the two catalogs).
- **Populated by CLI (2026-09-18):** the Construction catalog build was executed headlessly while the editor was closed (stale `Temp/UnityLockfile` had to be removed first) — 12 entries verified on disk.
- **Added (2026-09-18):** merge tool in [ConstructionCatalogBuilder.cs](Assets/Scripts/Scenario/Editor/ConstructionCatalogBuilder.cs) — `Tools > PCGML Scenario > Merge Construction into Scenario Catalog` copies the 12 construction entries into `Assets/Prefabs/Scenario/ObjectCatalog.asset` (21 entries total). Rationale: the system prompt's few-shot example uses scenario types (`floor_opening`, `concrete_barrier`, `material_stack`), which would be rejected as unknown with the construction catalog alone — one merged catalog keeps the example valid and lets the LLM compose hazard objects + site objects together. Re-run after "Create Prefab Library & Catalog" (it rebuilds the scenario catalog's 9 defaults).
- **Pivot to construction-only (2026-09-18, user decision):** the scenario catalog/prefabs are abandoned — `Assets/Prefabs/Scenario/` (ObjectCatalog.asset, 8 primitive prefabs, Materials/) was **deleted**. The construction catalog (`Assets/Prefabs/Construction/Construction.asset`, 12 types) is now the pipeline's catalog. Code changes: [PromptBuilder.cs](Assets/Scripts/Scenario/PromptBuilder.cs) now switches the system prompt's few-shot example to `ConstructionExample()` (a valid construction-only site: fence, building, both cranes, road, materials, trucks, containers; struck-by + fall hazards) whenever the active catalog contains `building`, keeping the example consistent with the allowed types. The three mock JSONs in `Assets/Scripts/Scenario/Examples/` were rewritten as construction variants (novice/expert/novice) so offline mock mode validates. NOTE: `Tools > PCGML Scenario > Create Prefab Library & Catalog` still exists and would recreate the primitive scenario prefabs — do not run it. The editor window's catalog loader now falls back to `Construction.asset` (it finds it via `t:ObjectCatalog` search). The merge tool is now moot unless the scenario catalog is ever rebuilt.
- **Footprints (2026-09-18) — fix for visual overlaps:** site-scale models (building) span many 2 m cells but the validators treated every object as 1 cell, so nothing prevented the LLM from overlapping them. Now `CatalogEntry` carries `footprintX/footprintY` (cells, anchored at the object's cell as bottom-left corner, extending +x/+y): building 4×4, cranes 2×2, fence 2×2, road 1×4, trucks 2×1, the rest 1×1; `ground` is now implicit (like floor_slab — "never list it"). [ScenarioValidator.cs](Assets/Scripts/Scenario/ScenarioValidator.cs) expands occupancy by footprint (collisions → `cell_collision` error, out-of-grid footprints → `footprint_out_of_bounds`), keeps route BFS and hazard reachability footprint-aware, and allows `crane_mounted` to share cells with its `building` (the mounted-on exception). The system prompt's catalog table now lists each footprint, hard rules 2/5/9 updated, the few-shot example and all three mock JSONs were recomputed to a footprint-clean layout. ⚠️ Action needed: re-run `Tools > PCGML Scenario > Build Construction Catalog` so the asset entries carry the footprints (existing asset entries default to 1×1).

---

## Conversation 1 — 2026-09-17

**What happened:** Reviewed all `Assets/Scripts/Scenario/` code against `task.md` and fixed the compile errors reported by Unity (two passes).

### Changed

- **Fixed:** [ScenarioGenerator.cs](Assets/Scripts/Scenario/ScenarioGenerator.cs) line 68 — error CS8361: a conditional expression (`attempt == 0 ? InitialTemperature : RepairTemperature`) was used directly inside a string interpolation, where the `:` is parsed as the format-specifier separator. Parenthesized the conditional:
  `{attempt == 0 ? InitialTemperature : RepairTemperature}` → `{(attempt == 0 ? InitialTemperature : RepairTemperature)}`.
- **Fixed:** [ScenarioValidator.cs](Assets/Scripts/Scenario/ScenarioValidator.cs) — `ValidateAll` signature mismatch (CS1501 in ScenarioGenerator.cs:85). The generator called `ValidateAll(data, catalog, result)` but the validator defined only `(data, catalog)`. Added an optional third parameter `ValidationResult result = null` so the generator's 3-arg call and the self-tests' 2-arg calls both compile; passing an existing result also preserves the `json_parse` error in the same audit trail.
- **Fixed:** [ScenarioValidator.cs](Assets/Scripts/Scenario/ScenarioValidator.cs) line 248 — CS1061: `cell` was an unnamed tuple `(source.Cell[0], source.Cell[1])` but `cell.x`/`cell.y` were used below. Named the elements: `(x: source.Cell[0], y: source.Cell[1])`.
- **Fixed:** [ScenarioMenu.cs](Assets/Scripts/Scenario/Editor/ScenarioMenu.cs) and [ScenarioGeneratorWindow.cs](Assets/Scripts/Scenario/Editor/ScenarioGeneratorWindow.cs) — CS0104: `Debug` ambiguous between `System.Diagnostics.Debug` and `UnityEngine.Debug` (both files imported `System.Diagnostics` for `Stopwatch`). Replaced the namespace import with a type alias `using Stopwatch = System.Diagnostics.Stopwatch;` so `Debug.Log` resolves to UnityEngine.
- **Fixed (2026-09-18) — pink prefabs:** root cause proven with a batch-mode diagnostic on a project copy: `PrefabUtility.SaveAsPrefabAsset` does **not** serialize runtime-created materials — every prefab saved with a null material on its MeshRenderer (renders magenta). Two fix patterns were tested: adding materials as prefab sub-assets (`AddObjectToAsset`) **fails**, standalone `.mat` assets **work**. So [ScenarioPrefabFactory.cs](Assets/Scripts/Scenario/Editor/ScenarioPrefabFactory.cs) now materializes each renderer's material as a real `.mat` under `Assets/Prefabs/Scenario/Materials/` before saving the prefab (renderers sharing one cached material share one asset; the folder is cleared on re-run so the menu item stays idempotent).
- **Changed (2026-09-18):** [PrimitiveFactory.cs](Assets/Scripts/Scenario/PrimitiveFactory.cs) `warning_tape` — the procedural stripe texture also cannot survive asset serialization (texture sub-assets were tested and came back null), so the tape is now built from 9 alternating yellow/dark boxes with no texture at all (fits the primitive-placeholder philosophy).
- **Added (2026-09-18):** `com.unity.ugui` 2.0.0 to [Packages/manifest.json](Packages/manifest.json) — prerequisite for Canvas/UI in Play mode (was missing).
- **Added (2026-09-18):** [RuntimeScenarioUI.cs](Assets/Scripts/Scenario/RuntimeScenarioUI.cs) — Play-mode entry point (M5 direction): prompt from a UI InputField → ScenarioGenerator (mock toggle or DeepSeek) → validate + repair loop → ScenarioBuilder builds the accepted scene at runtime. Outside the Editor folder, so it compiles into player builds. Editor Play mode uses the EditorPrefs API key; builds read `~/deepseek_config.json`; the mock's example JSONs are editor-only assets for now.
- **Added (2026-09-18):** [AppManager.cs](Assets/Scripts/AppManager.cs) — runtime config component for the hierarchy (singleton): `apiKey`, `model`, `endpoint`, `maxTokens`, `maxRetries`, `requestTimeoutSeconds`. [DeepSeekClient.cs](Assets/Scripts/Scenario/DeepSeekClient.cs) now reads `AppManager.Instance` first (constructor + `ApiKey` getter) and falls back to EditorPrefs for editor tooling; `request.timeout` now uses the configurable `TimeoutSeconds` field; the missing-key error message points at both places. ⚠️ The key lives in the scene file once pasted into the Inspector — fine for a local prototype, never commit it.
- **Fixed (2026-09-18) — empty LLM response in Play mode:** first real call returned `0 chars` after 17.6s (config verified correct: key set, endpoint/model default). Cause: DeepSeek answered in thinking mode — text in `reasoning_content`, `content` empty. Fix in [DeepSeekClient.cs](Assets/Scripts/Scenario/DeepSeekClient.cs): the request now sends `"thinking_mode": "non-thinking"` explicitly (task.md §6), and `ExtractContent` accepts `reasoning_content` as a fallback and throws with a 500-char raw-response excerpt when neither field is usable.
- **Fixed (2026-09-18) — NRE after successful validation (two real runs: "Novice material transport hazard scan" and "…across unfinished floor" were accepted and built, proving the pipeline works end-to-end with DeepSeek):** the next two runs failed with `Object reference not set…` *after* validation logged 0 errors. Root cause found in the stack trace: Newtonsoft's error-swallowing settings make `DeserializeObject` return `null` (instead of throwing) for top-level parse failures, the generator skipped validation for `parsed == null`, `IsValid` was vacuously true, and `parsed.Meta` threw in the acceptance log. Fixes: [ScenarioData.cs](Assets/Scripts/Scenario/ScenarioData.cs) `FromJson` now throws `FormatException` on a null result; [ScenarioGenerator.cs](Assets/Scripts/Scenario/ScenarioGenerator.cs) explicitly adds a `json_parse` error when `parsed == null`, includes a 200-char raw-response snippet in parse errors (so the repair loop and console show what the LLM actually sent), and the acceptance log is null-safe.
- **Changed (2026-09-18):** difficulty is now parsed from the prompt text instead of an Inspector dropdown in Play mode. [PromptBuilder.cs](Assets/Scripts/Scenario/PromptBuilder.cs) gained `DetectDifficulty(string)` — matches `expert`/`expart` (alias, user's spelling), then `intermediate`, else `novice` (case-insensitive, anywhere in the prompt). [RuntimeScenarioUI.cs](Assets/Scripts/Scenario/RuntimeScenarioUI.cs) dropped the public `difficulty` field; the status line shows the parsed difficulty ("Generating… (difficulty: novice, from prompt)"). Default prompt is now `novice transport hazard` to match the new style. The editor window keeps its own dropdown.
- **Verified (2026-09-18):** end-to-end in batch mode on a project copy — ran the real `CreatePrefabLibrary` and reloaded every prefab: 9 catalog entries, all 45 renderers report `Universal Render Pipeline/Lit` backed by assets, 0 broken renderers, 0 compile errors. Note for the user: the URP *Render Pipeline Converter* finds nothing because there are no built-in .mat files to convert — regenerating via the menu is the correct path.
- **Verified via Unity CLI:** ran the editor in batch mode (`-batchmode -nographics -quit`) against a `/tmp` copy of the project to compile-check without touching the open editor (the Hub launcher at `/Users/mac/.unity/bin/unity` rejects `-batchmode`; use `/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity` directly). Result: **0 compile errors** — script compilation finished (204-node graph, 4.6 s), `LogAssemblyErrors (0ms)` for both runtime and editor assemblies. All C# in the project compiles cleanly.
- **Created:** `changelog.md` (this file) — records everything changed, per conversation, plus the live "Next steps" section.

### Verified (no changes needed)

- Scanned all interpolated strings in `Assets/Scripts/Scenario/` for the same CS8361 pattern — all other ternaries inside interpolations were already parenthesized (DeepSeekClient.cs, ScenarioValidator.cs, ScenarioGenerator.cs line 121, ScenarioGeneratorWindow.cs).
- After the second pass: re-checked every `.x`/`.y` tuple member access in the folder — all are now on named tuples (Dictionary key `(int x, int y)`, the fixed `cell`, the BFS queue element, `InBounds` parameter).
- ⚠️ Correction to the earlier review note "no other compile issues found": the first review missed the two errors above (signature mismatch + unnamed tuple), which Unity's second compile pass caught. They are now fixed — recompile to confirm.
- DeepSeek model name verified via web search on 2026-09-17: `deepseek-v4-flash` is the current OpenAI-compatible model id; legacy `deepseek-chat` was retired 2026-07-24; V4.1 Flash released 2026-09-10 as `deepseek-flash` with v4-flash kept as an alias (see Next steps → B.2).
- Confirmed `com.unity.nuget.newtonsoft-json` 3.2.1 is in [Packages/manifest.json](Packages/manifest.json), so the Newtonsoft usages in ScenarioData/DeepSeekClient/ScenarioGeneratorWindow compile.
- Confirmed `Assets/Scripts/Scenario/Editor/` holds the UnityEditor-only files (correct assembly split).
- Sanity-checked the 3 mock JSONs in `Assets/Scripts/Scenario/Examples/` against the CS-ISR schema — consistent (e.g. mock 2's guarded opening `opening_3` is a distractor with no hazard pointing at it, so the controlled-state contradiction check won't reject it).

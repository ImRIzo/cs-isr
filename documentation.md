# demo_a — PCGML Construction Scenario Generator: Documentation

Last updated: 2026-09-27 · Companion to `task.md` (the design) and `changelog.md` (the history).

This document has three parts:
1. **The Generate flow** — every function call from button click to built scene, in order.
2. **Script reference** — every script and what it does.
3. **Conventions & gotchas** — grid & measurement rules, controls, settings, lessons learned.

---

## 1. The Generate flow (function call by function call)

Both entry points (Play-mode UI and editor window) converge on the same core. This traces the **Play-mode UI** path.

### Click Generate in Play mode

**`RuntimeScenarioUI.OnGenerateClicked()`** (`Assets/Scripts/Scenario/RuntimeScenarioUI.cs`)
1. Guards: not already generating; catalog assigned; prompt non-empty.
2. `PromptBuilder.DetectDifficulty(promptText)` → difficulty from the word *novice/intermediate/expert* (alias `expart`) in the prompt text; default `novice`.
3. `new DeepSeekClient()` → constructor reads `AppManager.Instance` (scene object) for model/endpoint/tokens/retries/timeout; falls back to EditorPrefs (editor tools).
4. `new ScenarioGenerator { Client = <DeepSeekClient>, Catalog = <Construction.asset> }`.
5. `await generator.GenerateAsync(promptText, difficulty, variantIndex: 0, variantCount: 1, onLog: AppendStatus)`.

### Inside `ScenarioGenerator.GenerateAsync()` (`ScenarioGenerator.cs`)

6. Builds the conversation:
   - `PromptBuilder.BuildSystemPrompt(catalog)` — role, 10 hard rules, catalog table **with footprints**, and the few-shot example. **Catalog-aware:** if the catalog contains `building`, the example is `PromptBuilder.ConstructionExample()` (construction site); otherwise the legacy `ExampleScenario()`. The example is serialized from the same POCOs the validators run on (no schema drift).
   - `PromptBuilder.BuildUserPrompt(prompt, difficulty, variantIndex, variantCount)` — `"Difficulty: X.\n\nTraining prompt: <text>\n\nReturn the scenario as a single JSON object."`
7. **Repair loop** `for attempt = 0 … MaxRepairRetries (2)`:
   - `await client.CompleteAsync(messages, temperature)` — **0.7** on attempt 0, **0.2** on repair passes.
   - `PromptBuilder.StripCodeFences(raw)` — removes ``` fences/prose around the outermost `{…}`.
   - `ScenarioData.FromJson(...)` — Newtonsoft, error-tolerant; **throws `FormatException` if the result is null** (a null is never a scenario).
   - `ScenarioValidator.ValidateAll(parsed, catalog, validation)` — all three stages, every pass (re-validating everything is deliberate: a repair that breaks something else must be caught):
     - **`SchemaValidate`** — required fields (title/objective/trade), difficulty enum, object ids/types, catalog membership (`unknown_type`), `floor_slab`/`ground`/`fence` rejected as implicit (`implicit_type`), cells in [0,9]², hazard types in {fall_from_height, struck_by_material}, `source_object_id` resolves, subtlety enum.
     - **`GeometryValidate`** — every object covers its **catalog footprint** (cell = bottom-left anchor, extends +x/+y); footprint out of grid → `footprint_out_of_bounds`; shared cells → `cell_collision` (exception: `crane_mounted` may share with its `building`); start/goal must not be covered; BFS from start over non-covered/non-blocked cells must reach the goal (`route_unreachable`); every hazard source approachable from a free neighbour (`hazard_unreachable`).
     - **`SafetyValidate`** — every hazard has `regulation_ref` + non-empty `required_controls`; controlled-state contradiction (a hazard whose source is already controlled is rejected); subtlety↔difficulty mapping (novice+subtle, expert+non-subtle → **warnings** only).
   - **Valid** → `result.Success = true`, record `Scenario`, `RawJson`, `AttemptsUsed`; log `accepted '…'`; break.
   - **Invalid** → log the first 10 issues; if attempts remain: append the failed candidate as `assistant` and `PromptBuilder.BuildRepairPrompt(validation)` as `user` (same multi-turn session — cheaper), log `sending structured errors back for repair`; else build `BuildRejectionReport` and finish as REJECTED.
8. Returns `GenerationResult` (Success, Scenario, RawJson, Attempts, AttemptsUsed, TotalSeconds, Log, FailureReason).

### Inside `DeepSeekClient.CompleteAsync()` (`DeepSeekClient.cs`)

9. `ApiKey` getter: `AppManager.Instance.apiKey` → EditorPrefs → `~/deepseek_config.json` (player builds). Missing → throw with instructions.
10. Builds the JObject body: `model`, `messages`, `temperature`, `max_tokens`, `response_format: {"type":"json_object"}`, `thinking_mode: "non-thinking"`.
11. Logs the full payload: `[Scenario] OUT -> DeepSeek …`.
12. POST loop (0…`MaxRetries` transport retries, backoff 0.5 s × 2ⁿ on 429/5xx/network):
    - 2xx → logs `[Scenario] IN <- DeepSeek (N chars): …` (full body) → `ExtractContent()`: `choices[0].message.content`; if empty, falls back to `reasoning_content`; if neither usable, throws with a 500-char excerpt.
    - other status → logs `[Scenario] IN <- DeepSeek HTTP <code>: <excerpt>` warning.

### Back in `RuntimeScenarioUI.OnGenerateClicked()`

13. `result.Success` → `new ScenarioBuilder { Catalog = catalog }.BuildIntoScene(result.Scenario)`.

### Inside `ScenarioBuilder.BuildIntoScene()` (`ScenarioBuilder.cs`)

14. Destroys the previous `ScenarioRoot` (regeneration replaces the scene).
15. `BuildFloor()` — instantiates the catalog's **`ground` prefab** once, centered on the site (world 9,−0.046,9) so its collider top sits at y = 0; any Rigidbody on it is pinned kinematic. Fallback: the old 20×20 m gray slab.
16. `BuildSiteFence()` — instantiates the `fence` prefab **once, centered on the site** (world 9,0,9). Site-scale scenery is never a grid object.
17. For each object → `PlaceObject()`:
    - prefab = `Catalog.GetEntry(type).prefab` (fallback: `PrimitiveFactory.Build(type)` if no link);
    - positioned at the **center of its covered footprint**: `CellToWorld(x,y) + (footprintX, 0, footprintY)` meters;
    - rotated by `rotation_y`.
18. `PlacePlayer()` — worker prefab at the start cell's center + `PlayerCameraController` (the Play-mode walk rig, see section 3).
19. Logs `[Scenario] Built '…': N objects, M hazards.` → `RuntimeScenarioUI` shows the ACCEPTED status line ("…Press C to walk the site.").

### Editor window path (for completeness)

`ScenarioGeneratorWindow.GenerateVariants()` runs the same `ScenarioGenerator` per variant (1–5), shows per-result ✓/✗, JSON view (`Pretty`), line-diff between variants (`DiffLines`, LCS), `BuildScene` (same `ScenarioBuilder`), `SaveScenario` (`ScenarioBuilder.SaveToAssets` → `Assets/Scenarios/<name>.json` + `.asset` provenance), and a Settings tab (EditorPrefs key/model/endpoint + hello-world smoke test).

---

## 2. Script reference

### `Assets/Scripts/`

| Script | What it does |
|---|---|
| `AppManager.cs` | Scene-config singleton (put on an empty GameObject). Holds `apiKey`, `model`, `endpoint`, `maxTokens`, `maxRetries`, `requestTimeoutSeconds`. `DeepSeekClient` reads it first. In editor Play mode an empty `apiKey` falls back to the EditorPrefs value. |
| `FreeFlyCamera.cs` | Attached to the Main Camera. WASD camera-relative movement, Space/Ctrl up-down, Shift fast, hold RMB+drag to look (cursor locks), mouse wheel adjusts speed. Uses legacy Input (project is set to "Both"). **All input is suspended while the prompt `InputField` has focus** (`TypingInInputField()` — also used by the player-camera toggle). |
| `PlayerCameraController.cs` | Play-mode walk rig, added to `player_spawn` by `ScenarioBuilder`. **C** toggles between the fly camera and the player view; the fly camera's GameObject is disabled (not moved), so its view is restored exactly. Player view: eye-height child camera (0.45 m), WASD walk (Shift run, 2 m/s) via a `CharacterController` (0.6 m tall, radius 0.12 — sized to the toy-scale worker), mouse look with locked cursor, worker body hidden while active. Cursor unlocks and the view is handed back on exit/teardown. |
| `FallCubeTrigger.cs` | One-shot falling hazard for the ground crane (attach to the GameObject with the trigger BoxCollider). Holds `fall_cube` kinematic until the player's CharacterController enters the zone, then releases it with `ContinuousDynamic` CCD (anti-tunneling through the 0.05 m-thin ground) and a runtime bouncy `PhysicsMaterial` (`Bounce` field, default 0.5; skipped if the cube already has a bouncy material). `releaseBoost` = optional downward nudge. Falls once per build. |

### `Assets/Scripts/Scenario/` (runtime pipeline — also compiles in player builds)

| Script | What it does |
|---|---|
| `ILLMClient.cs` | `ChatMessage` (role/content) and the `ILLMClient` interface: `Task<string> CompleteAsync(List<ChatMessage>, float temperature)`. The swap point for a future Python middleman. |
| `DeepSeekClient.cs` | Direct DeepSeek REST via `UnityWebRequest` (OpenAI chat format). Key resolution: AppManager → EditorPrefs → `~/deepseek_config.json`. Transport retries with backoff; explicit `thinking_mode: non-thinking`; `ExtractContent` with `reasoning_content` fallback; full OUT/IN console logs. |
| `ScenarioData.cs` | CS-ISR POCOs (`ScenarioData`, `ScenarioMeta`, `ScenarioObject`, `Hazard`, `ScenarioTask`) + enums (`Difficulty`, `Subtlety`, `HazardFamily`) + Newtonsoft settings (tolerant read, ignore unknown fields, swallow member errors). `FromJson` throws on a null result. `ScenarioAsset` = provenance ScriptableObject. |
| `ObjectCatalog.cs` | `ObjectCatalog` ScriptableObject (Create menu: *PCGML/Scenario/Object Catalog*). `CatalogEntry`: `type`, `displayName`, `safetyRole` (one-line meaning shown to the LLM), `prefab`, `footprintX/footprintY` (grid cells). `CatalogPromptText()` renders the LLM's catalog table including footprints. `CreateFromDefaults()`/`CatalogDefaults` = legacy in-memory fallback. |
| `PromptBuilder.cs` | Builds everything the LLM reads: `BuildSystemPrompt(catalog)` (rules + catalog table + catalog-aware few-shot example), `BuildUserPrompt`, `BuildRepairPrompt` (structured error list), `DetectDifficulty` (parses difficulty word from prompt), `ConstructionExample()` (valid 7×6-building site), `ExampleScenario()` (legacy), `StripCodeFences`. |
| `ScenarioValidator.cs` | Pure validation, no scene access. `ValidationError/Result`, `ValidateAll` → `SchemaValidate` + `GeometryValidate` + `SafetyValidate`. Footprint-aware occupancy, `cell_collision` with the crane-on-building exception, BFS route, hazard reachability, controlled-state contradiction, subtlety↔difficulty warnings. Constants: `GridSize = 10`, `CellSizeMeters = 2`. |
| `ScenarioGenerator.cs` | Orchestrator: prompt → parse → validate → repair loop (max 2 retries, re-validates everything each pass, same chat session) → `GenerationResult` with timings, raw JSON, per-attempt results, audit/rejection report. |
| `ScenarioBuilder.cs` | CS-ISR → scene: `ground` prefab floor (top at y=0, Rigidbody pinned kinematic; slab fallback), site fence (auto-centered), objects at footprint centers, player at start cell + `PlayerCameraController`. Editor-only `SaveToAssets` writes provenance JSON + asset to `Assets/Scenarios/`. |
| `PrimitiveFactory.cs` | Fallback primitive shapes + cached colored materials (URP Lit with fallbacks) when a catalog entry has no prefab. The `worker` is **toy-scale (~0.6 m total)** to match the 0.2× construction prefabs. Keeps the pipeline runnable with an incomplete catalog. |
| `RuntimeScenarioUI.cs` | Play-mode entry point. UI refs (InputField, Button, Text) + catalog slot. `OnGenerateClicked` = the flow in section 1. Status text shows the last 8 log lines; full trail goes to the Console. Success line hints "Press C to walk the site." |

### `Assets/Scripts/Scenario/Editor/` (editor tools)

| Script | What it does |
|---|---|
| `ScenarioGeneratorWindow.cs` | The only `Tools > PCGML Scenario` menu item. Generate tab: prompt, difficulty, variants, results with JSON/diff viewers, Build Scene From Last, Save. Settings tab: API key/model/endpoint (EditorPrefs) + connection smoke test. |

### Key assets (not code)

| Asset | Role |
|---|---|
| `Assets/Prefabs/Construction/Construction.asset` | **The** catalog: 12 entries from the Majadroid pack. Footprints: building **7×6** (measured), cranes 2×2, road 1×4, trucks 2×1, rest 1×1; `fence`/`ground` are implicit (auto-placed / never listed). The `ground` entry points at `Ground.prefab` (was miswired to `Crane-Mounted.prefab` until 2026-09-27). |
| `Assets/Prefabs/Construction/*.prefab` | The 12 model prefabs (pack FBX at scale 0.2). `Ground.prefab` carries a 20×20 m BoxCollider + kinematic Rigidbody; `Crane-On-Ground.prefab` carries the falling-hazard setup (trigger BoxCollider + `fall_cube` with Rigidbody mass 5). |
| `Assets/Scenes/SampleScene.unity` | The scene: Main Camera (+ FreeFlyCamera), Canvas with PromptInput/GenerateButton/StatusText, RuntimeScenarioUI, AppManager. The player camera rig is added at runtime by the builder — nothing to wire in the scene. |
| `Assets/mock data/` | `out.json` / `in.json` (mock data-flow docs), `prompts.pdf`/`prompts.txt` (5 sample prompts). |

---

## 3. Conventions & gotchas

### Grid & measurement — how the 20×20 m works

- **The constants.** `ScenarioValidator.GridSize = 10` and `CellSizeMeters = 2`. One cell is **2 m × 2 m**; 10 cells per side × 2 m = a **20×20 m** site slab.
- **Cell → world.** Cell `(x, y)` has its bottom-left corner at world `(x·2, 0, y·2)`, so the cell spans `x·2 .. x·2+2` in X and `y·2 .. y·2+2` in Z. The **center** of the cell — where 1×1 objects and the player end up — is `(x·2 + 1, 0, y·2 + 1)`. Cells span world 0..20 in both axes (cell (9,9) covers 18..20).
- **The slab / ground / fence are centered on the site**, at world (9, 0, 9), so they span **−1..19 m** — the last grid column (world 18..20) pokes ~1 m past their edge. This is a known margin quirk (objects in row/column 9 sit at the very rim; a falling crane cube near the rim can land just past the ground's edge).
- **Footprints.** An object's `cell` is the **bottom-left anchor** of its catalog footprint (`footprintX × footprintY` cells); the footprint extends **+x / +y** from there. The builder places the object at the **center of its covered footprint**: `CellToWorld(x, y) + (footprintX, 0, footprintY)` meters (half the footprint, since 1 cell = 2 m → footprintX cells = footprintX meters of offset).
  - Example: building 7×6 at (2,2) → anchor world (4,0,4), center (11,0,10), covering x 4..18, z 4..16 — 14×12 m.
- **Prefab scale — why everything is small.** The Majadroid pack FBX files import with `UnitScaleFactor = 100` (centimeters). The Construction prefabs are all variants at **0.2× scale**, so a real-world ~3 m truck reads as **~0.6 m tall** in the scene — sized to fit the 2 m cells. The primitive `worker` was rebuilt to ~0.6 m total (CharacterController 0.6 m, eye 0.45 m, walk 2 m/s) to match. If a model looks wrong-sized, check its prefab's scale before touching footprint numbers.
- **Implicit types** (never in the LLM's objects; the validator rejects them): `floor_slab`, `ground`, `fence`. `ground` (top at y = 0) and `fence` are auto-placed once at the site center.

The whole grid at a glance (the few-shot building, footprint 7×6 at cell (2,2), shaded ▓):

```
z (m)                                          cell y
 20 ┌───┬───┬───┬───┬───┬───┬───┬───┬───┬───┐   9
 18 ├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤   8
 16 ├───┼───┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼───┤   7
 14 ├───┼───┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼───┤   6
 12 ├───┼───┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼───┤   5
 10 ├───┼───┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼───┤   4
  8 ├───┼───┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼───┤   3
  6 ├───┼───┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼▓▓▓┼───┤   2
  4 ├───┼───┼───┼───┼───┼───┼───┼───┼───┼───┤   1
  2 └───┴───┴───┴───┴───┴───┴───┴───┴───┴───┘   0
    0   2   4   6   8  10  12  14  16  18  20   x (m)
```

- Left axis = world Z (the grid's `y` coordinate), bottom axis = world X (the grid's `x`); right axis = the cell index `y` 0..9 (cell index `x` runs 0..9 left to right). One cell = 2 m × 2 m, so the grid spans world **0..20 m** per side (10 × 2 m).
- The shaded building at cell (2,2): anchor = world (4, 0, 4), center = (11, 0, 10), covering x 4..18 m × z 4..16 m. The slab/ground/fence are centered at (9, 0, 9) instead, spanning −1..19 m — one meter short of the grid's outer edge (the margin quirk above).

### Play-mode controls

| Mode | Input | What it does |
|---|---|---|
| Fly camera | WASD / Space+Ctrl / Shift / RMB-drag / wheel | move (camera-relative) / up-down / fast / look / speed. **All suspended while the prompt InputField has focus.** |
| Player view | **C** | toggle from fly camera to player view (cursor locks) |
| Player view | WASD / Shift / mouse | walk (camera-relative, flat) / run / look |
| Player view | **C** | return to the fly camera (previous view restored; cursor unlocked) |
| Site | walk into the crane's trigger zone | `fall_cube` drops from the crane head (once per build; bounces per its `Bounce` setting) |

**Prompt & model**
- Difficulty is detected from the prompt text (novice/intermediate/expert; `expart` alias), not a dropdown — Play mode has no dropdown anymore (the editor window still has one).
- Model `deepseek-v4-flash` (verified current; `deepseek-chat` retired 2026-07; `deepseek-flash` = V4.1 alias if needed). OpenAI-compatible endpoint only — the app does **not** use the Anthropic-gateway (`api.deepseek.com/anthropic`) that Claude Code's env uses.
- Repair passes: temperature 0.2, same multi-turn session, max 2 retries, re-validate all stages.

**Settings & keys**
- Key resolution order: AppManager (scene) → EditorPrefs (Settings tab) → `~/deepseek_config.json` (builds). Never commit the key.

**Known pitfalls (why things are the way they are)**
- `SaveAsPrefabAsset` drops runtime-created materials → prefabs must reference real `.mat` assets (or primitives). Never regenerate prefabs via code for this project.
- DeepSeek thinking mode returns empty `content` (text in `reasoning_content`) → client sends `thinking_mode: non-thinking` AND falls back to `reasoning_content`.
- Newtonsoft's error-swallowing handler can return `null` for invalid JSON → `FromJson` throws on null so the repair loop sees a real parse error.
- **Repair loop can get prose back instead of JSON:** when the LLM can't satisfy the geometry (e.g. an overfull prompt like "two cranes, 5 trucks, 3 offices" against the 7×6 building's footprint), a repair pass may answer with an *explanation* of the validator error rather than corrected JSON → both `json_parse` errors land and the candidate is REJECTED after 3 attempts (the REJECTED line shows only the last attempt's errors). Mitigations: softer prompts (let the model pick counts), and/or a repair-prompt line like "Do not explain the errors — reply with the corrected JSON only."
- **Thin ground + fast falls = tunneling:** the ground collider is only ~0.05 m thick; a cube dropped from ~6 m hits at ~11 m/s and can skip it between physics steps. The falling cube therefore uses `ContinuousDynamic` collision detection. Any new falling object needs the same.
- **The floor must be kinematic:** `Ground.prefab` has a Rigidbody (mass 1) — it must stay **Is Kinematic** (the builder also pins it), otherwise it falls under gravity at Play start and gets shoved around by heavier falling objects.
- **Unity 6 physics API names:** the material class is `PhysicsMaterial` / `PhysicsMaterialCombine` (the old `PhysicMaterial` spelling does not compile). Runtime bounce materials are assigned via `collider.sharedMaterial`.
- Active Input Handling is "Both" — uGUI uses legacy `Input`; do not switch to Input-System-only without replacing the EventSystem module.
- URP: pipeline assets are assigned in Graphics Settings; primitives use `Shader.Find("Universal Render Pipeline/Lit")` with Standard/Unlit fallbacks.
- `Temp/UnityLockfile` can go stale after a crash → delete it before running batch-mode Unity commands against the project. (A batch-mode run also exits early with no compile if the editor currently holds the lock — the editor's own recompile covers that case.)

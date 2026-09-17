# demo_a — PCGML Construction Scenario Generator: Documentation

Last updated: 2026-09-18 · Companion to `task.md` (the design) and `changelog.md` (the history).

This document has three parts:
1. **The Generate flow** — every function call from button click to built scene, in order.
2. **Script reference** — every script and what it does.
3. **Conventions & gotchas** — grid rules, settings, lessons learned.

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
15. `BuildFloor()` — 20×20 m gray slab at the grid base.
16. `BuildSiteFence()` — instantiates the `fence` prefab **once, centered on the site** (world 9,0,9). Site-scale scenery is never a grid object.
17. For each object → `PlaceObject()`:
    - prefab = `Catalog.GetEntry(type).prefab` (fallback: `PrimitiveFactory.Build(type)` if no link);
    - positioned at the **center of its covered footprint**: `CellToWorld(x,y) + (footprintX, 0, footprintY)` meters;
    - rotated by `rotation_y`.
18. `PlacePlayer()` — worker prefab at the start cell's center.
19. Logs `[Scenario] Built '…': N objects, M hazards.` → `RuntimeScenarioUI` shows the ACCEPTED status line.

### Editor window path (for completeness)

`ScenarioGeneratorWindow.GenerateVariants()` runs the same `ScenarioGenerator` per variant (1–5), shows per-result ✓/✗, JSON view (`Pretty`), line-diff between variants (`DiffLines`, LCS), `BuildScene` (same `ScenarioBuilder`), `SaveScenario` (`ScenarioBuilder.SaveToAssets` → `Assets/Scenarios/<name>.json` + `.asset` provenance), and a Settings tab (EditorPrefs key/model/endpoint + hello-world smoke test).

---

## 2. Script reference

### `Assets/Scripts/`

| Script | What it does |
|---|---|
| `AppManager.cs` | Scene-config singleton (put on an empty GameObject). Holds `apiKey`, `model`, `endpoint`, `maxTokens`, `maxRetries`, `requestTimeoutSeconds`. `DeepSeekClient` reads it first. In editor Play mode an empty `apiKey` falls back to the EditorPrefs value. |
| `Player.cs` | FPS counter overlay (OnGUI), template leftover — decorative. |
| `FreeFlyCamera.cs` | Attached to the Main Camera. WASD camera-relative movement, Space/Ctrl up-down, Shift fast, hold RMB+drag to look (cursor locks), mouse wheel adjusts speed. Uses legacy Input (project is set to "Both"). |

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
| `ScenarioBuilder.cs` | CS-ISR → scene: floor slab, site fence (auto-centered), objects at footprint centers, player at start cell. Editor-only `SaveToAssets` writes provenance JSON + asset to `Assets/Scenarios/`. |
| `PrimitiveFactory.cs` | Fallback primitive shapes + cached colored materials (URP Lit with fallbacks) when a catalog entry has no prefab. Keeps the pipeline runnable with an incomplete catalog. |
| `RuntimeScenarioUI.cs` | Play-mode entry point. UI refs (InputField, Button, Text) + catalog slot. `OnGenerateClicked` = the flow in section 1. Status text shows the last 8 log lines; full trail goes to the Console. |

### `Assets/Scripts/Scenario/Editor/` (editor tools)

| Script | What it does |
|---|---|
| `ScenarioGeneratorWindow.cs` | The only `Tools > PCGML Scenario` menu item. Generate tab: prompt, difficulty, variants, results with JSON/diff viewers, Build Scene From Last, Save. Settings tab: API key/model/endpoint (EditorPrefs) + connection smoke test. |

### Key assets (not code)

| Asset | Role |
|---|---|
| `Assets/Prefabs/Construction/Construction.asset` | **The** catalog: 12 entries from the Majadroid pack. Footprints: building **7×6** (measured), cranes 2×2, road 1×4, trucks 2×1, rest 1×1; `fence`/`ground` are implicit (auto-placed / never listed). |
| `Assets/Prefabs/Construction/*.prefab` | The 12 model prefabs (pack FBX at scale 0.2). |
| `Assets/Scenes/SampleScene.unity` | The scene: Main Camera (+ FreeFlyCamera, Player), Canvas with PromptInput/GenerateButton/StatusText, RuntimeScenarioUI, AppManager. |
| `Assets/mock data/` | `out.json` / `in.json` (mock data-flow docs), `prompts.pdf`/`prompts.txt` (5 sample prompts). |

---

## 3. Conventions & gotchas

**Grid & footprints**
- 10×10 grid of 2 m cells on a 20×20 m slab. World pos of cell (x,y) = (x·2, 0, y·2).
- An object's `cell` is its footprint's **bottom-left anchor**; the footprint extends +x/+y (cell counts, e.g. building 7×6 = 14×12 m).
- `ScenarioBuilder` places objects at the **center** of their footprint; the player spawns at cell center.
- Implicit types (never in the LLM's objects, validator rejects them): `floor_slab`, `ground`, `fence`. `fence` is auto-placed once at the site center.

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
- Active Input Handling is "Both" — uGUI uses legacy `Input`; do not switch to Input-System-only without replacing the EventSystem module.
- URP: pipeline assets are assigned in Graphics Settings; primitives use `Shader.Find("Universal Render Pipeline/Lit")` with Standard/Unlit fallbacks.
- `Temp/UnityLockfile` can go stale after a crash → delete it before running batch-mode Unity commands against the project.

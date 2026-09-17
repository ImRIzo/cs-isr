# Task: Scenario Generation from Prompt (DeepSeek + Unity)

Slice of the PCGML proposal: **prompt → CS-ISR scene graph (JSON, via DeepSeek API) → validated → instantiated as a playable Unity scene**.
The LLM acts as a *grounded semantic planner* — not a free scene creator. It selects from a closed object catalog and expresses relations; deterministic code validates, repairs, and places.

## 1. Goal

- **Input:** natural-language training prompt, e.g. *"Create a novice material-transport task with one subtle fall hazard and one blocked pedestrian route."*
- **Output:** a Unity scene built from a prefab library + a saved CS-ISR JSON carrying hazard/regulation provenance.
- **Prototype targets (from proposal §7):** ≤ 10 s per scenario · ≥ 5 valid, distinct variants per learning goal · one unfinished-building floor · hazard families: **fall** and **struck-by/material-handling**.

## 2. Architecture

```
Editor UI (prompt, difficulty, variants)
   │
   ▼
PromptBuilder ──► DeepSeekClient (POST api.deepseek.com/chat/completions, JSON mode)
   │                     │ CS-ISR JSON (candidate)
   ▼                     ▼
System prompt +     SchemaValidator   ──✗──► repair loop (send errors back to LLM, max 2 retries)
few-shot example        │ ✓
                    GeometryValidator ──✗──► same repair loop
                        │ ✓
                    SafetyValidator   ──✗──► same repair loop
                        │ ✓
                    ScenarioBuilder ──► prefab library → scene instantiation
                                          + save ScenarioData asset / JSON (provenance kept)
```

### Design decisions

- **D1 — LLM plans, deterministic code places.** Free coordinates from LLMs are unreliable. The floor is a **10×10 grid over a 20×20 m slab**; the LLM assigns objects to grid *cells* and expresses relations ("barrier blocks route cell (4,5)"). Cell occupancy makes collision checking near-trivial and lets a BFS check route reachability.
- **D2 — Closed object catalog.** The LLM may only use types listed in `ObjectCatalog` (system prompt). Unknown types → schema error → repair. This matches the proposal's "controllable, relation-first logic".
- **D3 — Call DeepSeek directly from Unity for this slice.** OpenAI-compatible REST needs no SDK; `UnityWebRequest` works in the editor. Wrap it in an `ILLMClient` interface so a Python middleman (needed later for RAG/OSHA retrieval and the PCGML model, proposal Table 2) can be swapped in without touching the pipeline. Also write a `MockLLMClient` (returns canned JSON from a file) so scene-building work proceeds without network/API key.
- **D4 — Editor-time generation first.** An authoring tool (`ScenarioGeneratorWindow`) matches the "interactive authoring ≤ 10 s" target and keeps the API key on the dev machine. Runtime/adaptive generation comes after M4.
- **D5 — Server guarantees JSON syntax, nothing more.** DeepSeek JSON mode returns valid JSON but does *not* enforce our schema. Client-side validation + repair loop is therefore mandatory — this is exactly the proposal's risk control (§10: LLM output is never a source of truth on safety).

## 3. CS-ISR v0 JSON schema

```json
{
  "schema_version": "0.1",
  "scenario": {
    "title": "Novice material transport",
    "learning_objective": "Detect one subtle fall hazard and one blocked pedestrian route while transporting materials",
    "difficulty": "novice",
    "trade": "material_handler",
    "duration_hint_seconds": 180
  },
  "objects": [
    {
      "id": "opening_1",
      "type": "floor_opening",
      "cell": [3, 2],
      "rotation_y": 0,
      "task_role": "fall_hazard_source",
      "config": { "has_cover": false, "has_guardrail": false, "has_warning_tape": false }
    },
    {
      "id": "barrier_1",
      "type": "concrete_barrier",
      "cell": [4, 5],
      "task_role": "route_blocker"
    }
  ],
  "hazards": [
    {
      "id": "h1",
      "type": "fall_from_height",
      "source_object_id": "opening_1",
      "subtlety": "subtle",
      "required_controls": ["guardrail", "fall_arrest"],
      "regulation_ref": "OSHA 1926.501(b)(4)"
    },
    {
      "id": "h2",
      "type": "struck_by_material",
      "source_object_id": "material_stack_1",
      "subtlety": "obvious",
      "required_controls": ["barricade", "safe_zone"],
      "regulation_ref": "OSHA 1926.250(b)"
    }
  ],
  "task": {
    "type": "material_transport",
    "start_cell": [0, 0],
    "goal_cell": [9, 9],
    "blocked_cells": [[4, 5]],
    "intended_route_cells": [[0, 0], [1, 0], [2, 0], [2, 1], [2, 2], [2, 3], [2, 4], [3, 4], [4, 4], [5, 4], [5, 5], [6, 5], [7, 5], [8, 5], [8, 6], [8, 7], [8, 8], [8, 9], [9, 9]]
  }
}
```

Required fields are enforced by `ScenarioValidator` (stage 1 below). Optional fields (config, intended_route) default safely.

## 4. Object catalog v0

| type | prefab (v0 = Unity primitives) | safety role |
|---|---|---|
| `floor_slab` | implicit (whole floor) | scene base |
| `floor_opening` | floor segment w/ hole (or black quad + collider gap) | fall-hazard source |
| `guardrail` | post+rail primitives | control for fall hazards |
| `warning_tape` | striped tape strip | visual control / subtlety knob |
| `concrete_barrier` | scaled cube | route blocker / struck-by source |
| `material_pallet` | pallet + box | task object |
| `material_stack` | stacked boxes | unstable-stack struck-by source |
| `forklift` | primitive compound | moving struck-by source |
| `worker` | capsule + helmet | player / NPC |

Prefab ↔ type mapping lives in one `ScriptableObject` (`ObjectCatalog.asset`). Adding a hazard family later = add prefabs + rows + prompt text, no code change.

## 5. Pipeline stages and validators (proposal §5 → v0)

1. **SchemaValidator** — parse JSON; required fields present; `type` values are catalog members; cells in [0,9]²; hazard `source_object_id` exists; difficulty ∈ {novice, intermediate, expert}. Failure → repair loop. *(proposal target: ≥ 95/100 schema-valid)*
2. **GeometryValidator** — no two objects share a cell (except allowed stacks); grid BFS from `start_cell` to `goal_cell` with `blocked_cells` + occupied cells removed → route exists; hazard source cells reachable. *(target: ≥ 98/100 collision-free, task locations reachable)*
3. **SafetyValidator (v0)** — every hazard carries `regulation_ref` + non-empty `required_controls` + existing source; *controlled-state contradiction check*: a fall hazard whose source opening has `has_guardrail: true` is rejected (a "prohibited state" — the hazard must actually be present for training); `subtlety` matches difficulty mapping (novice → obvious/moderate, expert → subtle). *(target: every accepted hazard has provenance)*
4. **Repair loop** — on failure, append the validator's structured error list to the conversation and ask the LLM to fix only those errors. Max 2 retries, then abort with a logged report of *why* the candidate was rejected (proposal's auditable accept/reject trail).
5. **ScenarioBuilder** — instantiates prefabs per object, sets cell → world position (cell center × 2 m), spawns player at `start_cell`, and serializes the final CS-ISR JSON as an asset for provenance.

All validators are pure C# functions over the parsed data (no Unity scene access) so they are unit-testable and later reusable in a Python server.

## 6. Prompt design

- **System prompt:** role ("construction safety training scenario planner, output JSON only"), the schema as a worked example, the catalog table with one-line safety meaning per type, hard rules (only catalog types · grid 10×10 · hazards must reference real sources · never emit a hazard that is already fully controlled), difficulty → subtlety mapping.
- **Few-shot:** the example scenario from §3 embedded in the system prompt (the LLM's strongest schema hint).
- **User message:** the training prompt + difficulty + variation seed (`"Generate variant #3, differ from previous layouts"`).
- **Call params:** `response_format: {"type": "json_object"}`, non-thinking mode, `temperature 0.7` (variation) / `0.2` (repair passes), `max_tokens` ~4000 (way under DeepSeek's limits).

## 7. Unity implementation notes

- Unity 6.0 (`6000.3.24f1`), URP. No networking packages needed — `UnityWebRequest` is built in.
- JSON parsing: add `com.unity.nuget.newtonsoft-json` (UPM) for tolerant parsing + readable validation errors. (`JsonUtility` works as zero-dependency fallback — our schema is list-based, no dictionaries.)
- DeepSeek call: `POST https://api.deepseek.com/chat/completions`, header `Authorization: Bearer <key>`, OpenAI chat format. Models: `deepseek-v4-flash` for this slice (cheap, fast; `deepseek-chat` is a deprecated alias — verify current name in DeepSeek docs at integration time).
- **API key:** never hardcode. Store in `EditorPrefs` (set from the generator window's Settings tab) or a git-ignored `~/deepseek_config.json`. The proposal's later Python server will own the key instead.
- Async: `await UnityWebRequest.SendWebRequest()` works in the editor (C# 9). Generation runs in the editor, so coroutines/async are fine.

### Files

```
Assets/Scripts/Scenario/
  ScenarioData.cs              # CS-ISR POCOs (scenario, object, hazard, task)
  ObjectCatalog.cs             # ScriptableObject: type → prefab + catalog text
  PromptBuilder.cs             # system + few-shot + user prompt assembly
  ILLMClient.cs                # interface: Prompt -> string JSON
  DeepSeekClient.cs            # UnityWebRequest implementation
  MockLLMClient.cs             # file-backed fake for offline dev
  ScenarioValidator.cs         # schema + geometry + safety → List<ValidationError>
  ScenarioGenerator.cs         # orchestrator: prompt → validate/repair → data
  ScenarioBuilder.cs           # CS-ISR → scene instantiation + asset save
Assets/Scripts/Scenario/Editor/
  ScenarioGeneratorWindow.cs   # prompt box, difficulty, variants, log, settings
Assets/Prefabs/Scenario/       # primitive-based prefabs from catalog table
Assets/Scenarios/              # generated .asset + .json outputs
```

## 8. Milestones

- [ ] **M0 — API smoke test.** `DeepSeekClient` + a menu item sends a hello-world prompt, receives JSON in the console. API key configured via `EditorPrefs`.
- [ ] **M1 — Schema + prompt.** `ScenarioData`, `PromptBuilder`, generator returns a *parsed* CS-ISR from the example prompt. `MockLLMClient` works end-to-end so M1 has no network dependency.
- [ ] **M2 — Validators + repair loop.** Schema/geometry/safety checks pass/fail with error lists; failed candidates are repaired via re-prompt (≤ 2 retries); rejection report logged.
- [ ] **M3 — Scene instantiation.** Prefab library built; `ScenarioBuilder` produces the playable scene (opening without guardrail, blocked route, pallet task). Visual check in editor.
- [ ] **M4 — Editor UX + variants.** Window: generate 1 or 5 variants, show JSON diff between variants, save/load scenario assets, generation time shown (target ≤ 10 s).
- [ ] **M5 (later) — Runtime + RAG.** Generate in builds via server; OSHA rule snippets retrieved (RAG) into the system prompt; trainee-performance adaptation.

## 9. Risks and mitigations

| risk | mitigation |
|---|---|
| LLM emits invalid JSON | DeepSeek JSON mode + repair loop with structured errors |
| LLM invents object types / out-of-bounds cells | closed catalog in prompt + SchemaValidator (D1/D2) |
| LLM "fixes" one error, breaks another | repair loop re-validates *all* stages each pass; max 2 retries then abort |
| LLM generates *safe* scene (guarded openings) | SafetyValidator controlled-state contradiction check (stage 3) |
| No API key / no network during dev | `MockLLMClient` + all validation/instantiation testable offline |
| 429 rate limit / 500s | sequential single calls, simple retry w/ backoff on 429/5xx |
| Latency vs ≤ 10 s target | non-thinking `deepseek-v4-flash`, small max_tokens, log per-stage timing |
| API key exposure in builds | editor-only client for now; server owns the key in M5 |
| Schema drift between prompt example and C# POCOs | single source: generate both the prompt example and the POCO tests from one schema doc (M1 task) |

## 10. Open decisions (answer before M1)

1. DeepSeek API key — do we have one? (M0 needs it; M1+ can run on the mock.)
2. Prefab placeholders: Unity primitives (cubes/capsules, colored) are fine for the prototype? Real 3D assets come later.
3. Generation scope for the demo: editor-only (D4), or do we also need it inside the runtime build?
4. Put the repair-loop re-prompt in the *same* chat session (multi-turn) vs new session with errors inlined — multi-turn is cheaper/faster; confirm DeepSeek context handling for the 2-retry depth.

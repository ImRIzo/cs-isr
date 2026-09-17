using System;
using System.Collections.Generic;
using System.Text;

// Prompt assembly (task.md §6). The few-shot worked example is serialized from
// the same C# POCOs the validators run on — single source of truth (task.md
// §9, "schema drift" row) — so the prompt example and the parser can never
// drift apart.

public static class PromptBuilder
{
    public static string BuildSystemPrompt(ObjectCatalog catalog)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are a construction-safety training scenario planner.");
        sb.AppendLine("You design a hazard-recognition scenario on a single unfinished-building floor: objects placed on a grid, hazards tied to those objects, and a walking task whose route stays clear of hazards.");
        sb.AppendLine("You respond with a single JSON object only — no markdown fences, no prose.");
        sb.AppendLine();
        sb.AppendLine("HARD RULES:");
        sb.AppendLine("1. Use ONLY object types from the catalog below. Unknown types are rejected.");
        sb.AppendLine("2. The floor is a 10x10 grid of 2 m cells on a 20x20 m slab. Cells are [x, y] with 0 <= x,y <= 9. Every object covers its catalog footprint in cells, anchored at its cell (bottom-left corner) and extending +x and +y; the whole footprint must stay inside the grid.");
        sb.AppendLine("3. Every hazard must reference an existing object id in source_object_id.");
        sb.AppendLine("4. Never emit a hazard whose source is already fully controlled: a floor_opening with has_guardrail:true or has_cover:true must NOT be the source of a fall_from_height hazard.");
        sb.AppendLine("5. No two objects may overlap in their covered cells. Exceptions: crane_mounted may sit on the building it is mounted on; material_stack may share with material_stack.");
        sb.AppendLine("6. The route from start_cell to goal_cell must stay clear: no covered or blocked cell may lie on it, and blocked_cells must list every cell that blocks the route.");
        sb.AppendLine("7. Difficulty -> hazard subtlety: novice scenarios use obvious/moderate hazards, expert scenarios use subtle hazards.");
        sb.AppendLine("8. Every hazard needs a regulation_ref and a non-empty required_controls list.");
        sb.AppendLine("9. floor_slab, ground and fence are implicit — they are placed automatically around the whole site and must never be listed in objects.");
        sb.AppendLine("10. Output ONLY the JSON object.");
        sb.AppendLine();
        sb.AppendLine("OBJECT CATALOG:");
        sb.AppendLine(catalog != null && catalog.Entries.Count > 0 ? catalog.CatalogPromptText() : CatalogDefaults.PromptText());
        sb.AppendLine();
        sb.AppendLine("SCHEMA — worked example (follow this structure exactly):");
        // The example must only use types the active catalog allows: with the
        // construction catalog (contains "building") the canonical scenario
        // example would be full of rejected types, so use a site example.
        sb.AppendLine((catalog != null && catalog.Contains("building") ? ConstructionExample() : ExampleScenario()).ToJson());
        return sb.ToString();
    }

    public static string BuildUserPrompt(string trainingPrompt, Difficulty difficulty, int variantIndex, int variantCount)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Difficulty: {difficulty}.");
        if (variantCount > 1)
            sb.AppendLine($"This is variant #{variantIndex + 1} of {variantCount}. Differ from the other variants: different cells, rotations, and — where sensible — different hazard types.");
        sb.AppendLine();
        sb.AppendLine($"Training prompt: {trainingPrompt}");
        sb.AppendLine();
        sb.AppendLine("Return the scenario as a single JSON object.");
        return sb.ToString();
    }

    // Difficulty comes from the prompt text, not a UI dropdown (Play mode):
    // "novice transport hazard", "expert slab inspection", etc. Most specific
    // word wins; nothing matched -> novice. "expart" is accepted as an alias
    // for "expert" (the user's example prompt spells it that way).
    public static Difficulty DetectDifficulty(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return Difficulty.novice;
        if (prompt.IndexOf("expert", StringComparison.OrdinalIgnoreCase) >= 0 ||
            prompt.IndexOf("expart", StringComparison.OrdinalIgnoreCase) >= 0)
            return Difficulty.expert;
        if (prompt.IndexOf("intermediate", StringComparison.OrdinalIgnoreCase) >= 0)
            return Difficulty.intermediate;
        return Difficulty.novice;
    }

    // Structured error feedback for the repair loop (task.md §5.4): the LLM is
    // told exactly which validator rejected which item, and to change nothing else.
    public static string BuildRepairPrompt(ValidationResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Your previous response failed validation. Fix ONLY the listed errors (and warnings where safe), keep everything else unchanged, and return the complete corrected JSON object only.");
        sb.AppendLine();
        sb.AppendLine("Validation issues:");
        sb.Append(result.ToRepairPrompt());
        return sb.ToString();
    }

    // Canonical few-shot scenario — task.md §3, with material_stack_1 added so
    // hazard h2's source_object_id actually resolves (the printed example in
    // the document references an object it never lists).
    public static ScenarioData ExampleScenario()
    {
        return new ScenarioData
        {
            Version = "0.1",
            Meta = new ScenarioMeta
            {
                Title = "Novice material transport",
                LearningObjective = "Detect one subtle fall hazard and one blocked pedestrian route while transporting materials",
                Difficulty = "novice",
                Trade = "material_handler",
                DurationHintSeconds = 180,
            },
            Objects = new List<ScenarioObject>
            {
                new ScenarioObject
                {
                    Id = "opening_1",
                    Type = "floor_opening",
                    Cell = new[] { 3, 2 },
                    RotationY = 0,
                    TaskRole = "fall_hazard_source",
                    Config = new Dictionary<string, object>
                    {
                        { "has_cover", false },
                        { "has_guardrail", false },
                        { "has_warning_tape", false },
                    },
                },
                new ScenarioObject { Id = "barrier_1", Type = "concrete_barrier", Cell = new[] { 4, 5 }, TaskRole = "route_blocker" },
                new ScenarioObject { Id = "material_stack_1", Type = "material_stack", Cell = new[] { 6, 2 }, TaskRole = "unstable_stack_source" },
            },
            Hazards = new List<Hazard>
            {
                new Hazard
                {
                    Id = "h1",
                    Type = "fall_from_height",
                    SourceObjectId = "opening_1",
                    Subtlety = "subtle",
                    RequiredControls = new List<string> { "guardrail", "fall_arrest" },
                    RegulationRef = "OSHA 1926.501(b)(4)",
                },
                new Hazard
                {
                    Id = "h2",
                    Type = "struck_by_material",
                    SourceObjectId = "material_stack_1",
                    Subtlety = "obvious",
                    RequiredControls = new List<string> { "barricade", "safe_zone" },
                    RegulationRef = "OSHA 1926.250(b)",
                },
            },
            Task = new ScenarioTask
            {
                Type = "material_transport",
                StartCell = new[] { 0, 0 },
                GoalCell = new[] { 9, 9 },
                BlockedCells = new List<int[]> { new[] { 4, 5 } },
                IntendedRouteCells = new List<int[]>
                {
                    new[] { 0, 0 }, new[] { 1, 0 }, new[] { 2, 0 }, new[] { 2, 1 }, new[] { 2, 2 }, new[] { 2, 3 }, new[] { 2, 4 },
                    new[] { 3, 4 }, new[] { 4, 4 }, new[] { 5, 4 }, new[] { 5, 5 }, new[] { 6, 5 }, new[] { 7, 5 }, new[] { 8, 5 },
                    new[] { 8, 6 }, new[] { 8, 7 }, new[] { 8, 8 }, new[] { 8, 9 }, new[] { 9, 9 },
                },
            },
        };
    }

    public static string ExampleJson() => ExampleScenario().ToJson();

    // Few-shot for the construction-only catalog (changelog 2026-09-18): a
    // simple site built only from construction types. Layout respects the real
    // catalog footprints (building 7x6 cells = 14x12 m — user-measured
    // 2026-09-18): no overlap except crane_mounted on the building, everything
    // in-bounds, a clear route from (0,0) to (9,9) around the building's
    // margins, all hazard sources approachable, novice-appropriate subtlety.
    static ScenarioData ConstructionExample()
    {
        return new ScenarioData
        {
            Version = "0.1",
            Meta = new ScenarioMeta
            {
                Title = "Simple construction site",
                LearningObjective = "Recognize struck-by hazards around stacked materials and moving trucks while crossing the site",
                Difficulty = "novice",
                Trade = "laborer",
                DurationHintSeconds = 180,
            },
            Objects = new List<ScenarioObject>
            {
                new ScenarioObject { Id = "building_1", Type = "building", Cell = new[] { 2, 2 }, TaskRole = "main_structure" },
                new ScenarioObject { Id = "crane_mounted_1", Type = "crane_mounted", Cell = new[] { 7, 5 }, TaskRole = "overhead_hazard_source" },
                new ScenarioObject { Id = "materials_1", Type = "construction_materials", Cell = new[] { 1, 0 }, TaskRole = "unstable_stack_source" },
                new ScenarioObject { Id = "trucks_1", Type = "trucks", Cell = new[] { 5, 0 }, TaskRole = "moving_struck_by_source" },
                new ScenarioObject { Id = "container_1", Type = "containers", Cell = new[] { 0, 8 }, TaskRole = "material_collection_point" },
            },
            Hazards = new List<Hazard>
            {
                new Hazard
                {
                    Id = "h1",
                    Type = "struck_by_material",
                    SourceObjectId = "materials_1",
                    Subtlety = "obvious",
                    RequiredControls = new List<string> { "barricade", "safe_zone" },
                    RegulationRef = "OSHA 1926.250(b)",
                },
                new Hazard
                {
                    Id = "h2",
                    Type = "struck_by_material",
                    SourceObjectId = "trucks_1",
                    Subtlety = "moderate",
                    RequiredControls = new List<string> { "traffic_plan", "safe_zone" },
                    RegulationRef = "OSHA 1926.602(c)",
                },
                new Hazard
                {
                    Id = "h3",
                    Type = "fall_from_height",
                    SourceObjectId = "building_1",
                    Subtlety = "moderate",
                    RequiredControls = new List<string> { "guardrail", "fall_arrest" },
                    RegulationRef = "OSHA 1926.501(b)(4)",
                },
            },
            Task = new ScenarioTask
            {
                Type = "site_walkthrough",
                StartCell = new[] { 0, 0 },
                GoalCell = new[] { 9, 9 },
                BlockedCells = new List<int[]>(),
            },
        };
    }

    // LLMs sometimes wrap JSON output in ```json fences or prose despite JSON
    // mode; extract the outermost {...} before parsing.
    public static string StripCodeFences(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var text = raw.Trim();
        if (text.StartsWith("```"))
        {
            int end = text.LastIndexOf("```", StringComparison.Ordinal);
            if (end > 3) text = text.Substring(3, end - 3);
            int newline = text.IndexOf('\n');
            if (newline >= 0 && text.Substring(0, newline).Trim().EndsWith("json", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(newline + 1);
        }
        int first = text.IndexOf('{');
        int last = text.LastIndexOf('}');
        if (first >= 0 && last > first) text = text.Substring(first, last - first + 1);
        return text;
    }
}

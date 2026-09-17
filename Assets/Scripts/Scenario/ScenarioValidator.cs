using System;
using System.Collections.Generic;
using System.Linq;

// Pure validation pipeline (task.md §5). All three stages operate on the
// parsed ScenarioData only — no scene access — so they are unit-testable
// and reusable in a Python server.
// The same error list that blocks acceptance is sent back to the LLM by
// ScenarioGenerator's repair loop.

public enum ValidationStage { Schema, Geometry, Safety }

public enum ValidationSeverity { Error, Warning }

[Serializable]
public class ValidationError
{
    public ValidationStage Stage;
    public ValidationSeverity Severity = ValidationSeverity.Error;
    public string Code = "";
    public string Message = "";
    public string Context = "";

    public ValidationError() { }

    public ValidationError(ValidationStage stage, ValidationSeverity severity, string code, string message, string context = "")
    {
        Stage = stage;
        Severity = severity;
        Code = code;
        Message = message;
        Context = context;
    }

    public string ToLine() =>
        $"[{Stage.ToString().ToLowerInvariant()}:{Code}] {Message}{(string.IsNullOrEmpty(Context) ? "" : " (" + Context + ")")}";
}

public class ValidationResult
{
    public List<ValidationError> Issues = new List<ValidationError>();

    public IEnumerable<ValidationError> Errors => Issues.Where(i => i.Severity == ValidationSeverity.Error);
    public IEnumerable<ValidationError> Warnings => Issues.Where(i => i.Severity == ValidationSeverity.Warning);

    // Acceptance is decided on errors only; warnings stay in the audit trail.
    public bool IsValid => !Issues.Any(i => i.Severity == ValidationSeverity.Error);

    public void Error(ValidationStage stage, string code, string message, string context = "") =>
        Issues.Add(new ValidationError(stage, ValidationSeverity.Error, code, message, context));

    public void Warning(ValidationStage stage, string code, string message, string context = "") =>
        Issues.Add(new ValidationError(stage, ValidationSeverity.Warning, code, message, context));

    // Structured error list for the repair loop (task.md §5.4).
    public string ToRepairPrompt()
    {
        var lines = new List<string>();
        foreach (var error in Errors) lines.Add("- " + error.ToLine());
        foreach (var warning in Warnings) lines.Add("- (warning) " + warning.ToLine());
        return string.Join("\n", lines);
    }
}

public static class ScenarioValidator
{
    public const int GridSize = 10;
    public const float CellSizeMeters = 2f;

    public static bool IsValidCell(int[] cell) =>
        cell != null && cell.Length >= 2 && cell[0] >= 0 && cell[0] < GridSize && cell[1] >= 0 && cell[1] < GridSize;

    // The repair loop re-validates ALL stages every pass (task.md §9), so one
    // entry point that runs the full pipeline keeps that guaranteed.
    // An existing result may be passed in (the generator starts one before the
    // parse step so a json_parse error survives into the same audit trail).
    public static ValidationResult ValidateAll(ScenarioData data, ObjectCatalog catalog, ValidationResult result = null)
    {
        if (result == null) result = new ValidationResult();
        SchemaValidate(data, catalog, result);
        GeometryValidate(data, catalog, result);
        SafetyValidate(data, result);
        return result;
    }

    // Stage 1 — schema (task.md §5.1): required fields, catalog membership,
    // cell bounds, hazard sources, controlled vocabularies.
    public static void SchemaValidate(ScenarioData data, ObjectCatalog catalog, ValidationResult result)
    {
        if (data == null)
        {
            result.Error(ValidationStage.Schema, "null_data", "response is not a scenario object");
            return;
        }

        if (string.IsNullOrWhiteSpace(data.Version))
            result.Warning(ValidationStage.Schema, "missing_schema_version", "schema_version missing; assumed 0.1");

        var meta = data.Meta ?? new ScenarioMeta();
        RequireField(result, meta.Title, "scenario.title");
        RequireField(result, meta.LearningObjective, "scenario.learning_objective");
        RequireField(result, meta.Trade, "scenario.trade");
        if (!Enum.TryParse<Difficulty>(meta.Difficulty, true, out _))
            result.Error(ValidationStage.Schema, "unknown_difficulty",
                $"scenario.difficulty '{meta.Difficulty}' is not one of {{novice, intermediate, expert}}");
        if (meta.DurationHintSeconds <= 0)
            result.Warning(ValidationStage.Schema, "missing_duration", "duration_hint_seconds missing or <= 0; default 180 used");

        if (data.Objects == null || data.Objects.Count == 0)
        {
            result.Error(ValidationStage.Schema, "no_objects", "objects list is empty; at least one object required");
        }
        else
        {
            var seenIds = new HashSet<string>();
            foreach (var obj in data.Objects)
            {
                if (obj == null) { result.Error(ValidationStage.Schema, "null_object", "objects contains a null entry"); continue; }

                if (string.IsNullOrWhiteSpace(obj.Id))
                    result.Error(ValidationStage.Schema, "missing_id", "object has no id", "object");
                else if (!seenIds.Add(obj.Id))
                    result.Error(ValidationStage.Schema, "duplicate_id", $"duplicate object id '{obj.Id}'", obj.Id);

                if (string.IsNullOrWhiteSpace(obj.Type))
                    result.Error(ValidationStage.Schema, "missing_type", $"object '{obj.Id}' has no type", obj.Id);
                else if (obj.Type == "floor_slab" || obj.Type == "ground" || obj.Type == "fence")
                    result.Error(ValidationStage.Schema, "implicit_type",
                        $"{obj.Type} is implicit site scenery (auto-placed) and must not be listed in objects", obj.Id);
                else if (catalog != null && !catalog.Contains(obj.Type))
                    result.Error(ValidationStage.Schema, "unknown_type",
                        $"object '{obj.Id}' has type '{obj.Type}' which is not in the catalog; allowed: {string.Join(", ", catalog.TypeNames())}", obj.Id);

                if (!IsValidCell(obj.Cell))
                    result.Error(ValidationStage.Schema, "cell_invalid",
                        $"object '{obj.Id}' cell must be [x, y] with 0 <= x,y <= 9; got '{CellString(obj.Cell)}'", obj.Id);
            }
        }

        if (data.Hazards == null || data.Hazards.Count == 0)
        {
            result.Error(ValidationStage.Schema, "no_hazards", "hazards list is empty; at least one hazard required");
        }
        else
        {
            var seenIds = new HashSet<string>();
            foreach (var hazard in data.Hazards)
            {
                if (hazard == null) { result.Error(ValidationStage.Schema, "null_hazard", "hazards contains a null entry"); continue; }

                if (string.IsNullOrWhiteSpace(hazard.Id))
                    result.Error(ValidationStage.Schema, "missing_id", "hazard has no id", "hazard");
                else if (!seenIds.Add(hazard.Id))
                    result.Error(ValidationStage.Schema, "duplicate_id", $"duplicate hazard id '{hazard.Id}'", hazard.Id);

                if (string.IsNullOrWhiteSpace(hazard.Type))
                    result.Error(ValidationStage.Schema, "missing_type", $"hazard '{hazard.Id}' has no type", hazard.Id);
                else if (!Enum.TryParse<HazardFamily>(hazard.Type, true, out _))
                    result.Error(ValidationStage.Schema, "unknown_hazard_type",
                        $"hazard '{hazard.Id}' type '{hazard.Type}' is not one of {{fall_from_height, struck_by_material}}", hazard.Id);

                if (string.IsNullOrWhiteSpace(hazard.SourceObjectId))
                    result.Error(ValidationStage.Schema, "missing_source", $"hazard '{hazard.Id}' has no source_object_id", hazard.Id);
                else if (data.FindObject(hazard.SourceObjectId) == null)
                    result.Error(ValidationStage.Schema, "source_not_found",
                        $"hazard '{hazard.Id}' source_object_id '{hazard.SourceObjectId}' does not match any object id", hazard.Id);

                if (!Enum.TryParse<Subtlety>(hazard.Subtlety, true, out _))
                    result.Error(ValidationStage.Schema, "unknown_subtlety",
                        $"hazard '{hazard.Id}' subtlety '{hazard.Subtlety}' is not one of {{obvious, moderate, subtle}}", hazard.Id);
            }
        }

        var task = data.Task ?? new ScenarioTask();
        RequireField(result, task.Type, "task.type");
        if (!IsValidCell(task.StartCell))
            result.Error(ValidationStage.Schema, "cell_invalid", $"task.start_cell invalid: '{CellString(task.StartCell)}'");
        if (!IsValidCell(task.GoalCell))
            result.Error(ValidationStage.Schema, "cell_invalid", $"task.goal_cell invalid: '{CellString(task.GoalCell)}'");
        foreach (var cell in task.BlockedCells)
            if (!IsValidCell(cell))
                result.Error(ValidationStage.Schema, "cell_invalid", $"task.blocked_cells contains invalid cell '{CellString(cell)}'");
        if (task.IntendedRouteCells != null)
            foreach (var cell in task.IntendedRouteCells)
                if (!IsValidCell(cell))
                    result.Error(ValidationStage.Schema, "cell_invalid", $"task.intended_route_cells contains invalid cell '{CellString(cell)}'");
    }

    // Stage 2 — geometry (task.md §5.2): cell collisions (with catalog
    // footprints), BFS route from start to goal, hazard source reachability.
    public static void GeometryValidate(ScenarioData data, ObjectCatalog catalog, ValidationResult result)
    {
        if (data == null || data.Objects == null || data.Task == null)
            return; // schema already failed; skip the cascade

        // D1: cell occupancy map makes collision checking near-trivial. Each
        // object covers its catalog footprint in cells, anchored at its cell
        // (bottom-left corner, extending +x and +y).
        var occupied = new Dictionary<(int x, int y), List<string>>();
        foreach (var obj in data.Objects)
        {
            if (obj == null || !IsValidCell(obj.Cell)) continue;
            var footprint = FootprintOf(catalog, obj.Type);
            foreach (var cell in CoveredCells(obj.Cell, footprint))
            {
                if (!InBounds(cell))
                {
                    result.Error(ValidationStage.Schema, "footprint_out_of_bounds",
                        $"object '{obj.Id}' footprint {footprint.w}x{footprint.h} from cell ({obj.Cell[0]},{obj.Cell[1]}) extends outside the grid", obj.Id);
                    break;
                }
                if (!occupied.TryGetValue(cell, out var ids)) { ids = new List<string>(); occupied[cell] = ids; }
                ids.Add(obj.Id);
            }
        }

        foreach (var pair in occupied)
        {
            if (pair.Value.Count <= 1) continue;
            var types = pair.Value.Select(id => data.FindObject(id)?.Type ?? "?").OrderBy(t => t).ToList();
            if (!IsAllowedStack(types))
                result.Error(ValidationStage.Geometry, "cell_collision",
                    $"cell ({pair.Key.x},{pair.Key.y}) is covered by {pair.Value.Count} objects ({string.Join(", ", pair.Value)}) " +
                    $"of types ({string.Join(", ", types)}); sharing is only allowed for crane_mounted on its building (and material_stack+material_stack)",
                    $"{pair.Key.x},{pair.Key.y}");
        }

        var start = data.Task.StartCell;
        var goal = data.Task.GoalCell;
        if (!IsValidCell(start) || !IsValidCell(goal)) return; // schema reports those

        var startKey = (start[0], start[1]);
        var goalKey = (goal[0], goal[1]);
        if (occupied.ContainsKey(startKey))
            result.Error(ValidationStage.Geometry, "start_occupied", $"task.start_cell ({start[0]},{start[1]}) is occupied by an object");
        if (occupied.ContainsKey(goalKey))
            result.Error(ValidationStage.Geometry, "goal_occupied", $"task.goal_cell ({goal[0]},{goal[1]}) is occupied by an object");

        var blocked = new HashSet<(int x, int y)>(
            data.Task.BlockedCells.Where(IsValidCell).Select(c => (c[0], c[1])));
        foreach (var pair in occupied) blocked.Add(pair.Key);

        if (blocked.Contains(startKey))
            result.Error(ValidationStage.Geometry, "start_blocked", "task.start_cell is listed in blocked_cells");
        if (blocked.Contains(goalKey))
            result.Error(ValidationStage.Geometry, "goal_blocked", "task.goal_cell is listed in blocked_cells");

        var reachable = BreadthFirstSearch(startKey, blocked);
        if (!reachable.Contains(goalKey))
            result.Error(ValidationStage.Geometry, "route_unreachable",
                $"no route from start ({start[0]},{start[1]}) to goal ({goal[0]},{goal[1]}) after removing blocked and occupied cells");

        // Every hazard source must be approachable from the start: one of its
        // covered cells if free, otherwise a free 4-neighbour of any covered
        // cell (an occupied source like a building can never be stepped onto).
        foreach (var hazard in data.Hazards)
        {
            if (hazard == null) continue;
            var source = data.FindObject(hazard.SourceObjectId);
            if (source == null || !IsValidCell(source.Cell)) continue; // schema reports those
            var footprint = FootprintOf(catalog, source.Type);
            bool approachable = false;
            foreach (var cell in CoveredCells(source.Cell, footprint))
            {
                if (!blocked.Contains(cell) && reachable.Contains(cell)) { approachable = true; break; }
                foreach (var (dx, dy) in Neighbours)
                {
                    var next = (cell.x + dx, cell.y + dy);
                    if (InBounds(next) && !blocked.Contains(next) && reachable.Contains(next)) { approachable = true; break; }
                }
                if (approachable) break;
            }
            if (!approachable)
                result.Error(ValidationStage.Geometry, "hazard_unreachable",
                    $"hazard '{hazard.Id}' source '{source.Id}' at ({source.Cell[0]},{source.Cell[1]}) is not reachable from start_cell", hazard.Id);
        }

        // The intended route is provenance, not a safety gate -> warnings only.
        var route = data.Task.IntendedRouteCells;
        if (route != null && route.Count > 0)
        {
            if (!IsValidCell(route[0]) || route[0][0] != start[0] || route[0][1] != start[1])
                result.Warning(ValidationStage.Geometry, "invalid_intended_route", "intended_route_cells does not start at start_cell");
            if (!IsValidCell(route[route.Count - 1]) || route[route.Count - 1][0] != goal[0] || route[route.Count - 1][1] != goal[1])
                result.Warning(ValidationStage.Geometry, "invalid_intended_route", "intended_route_cells does not end at goal_cell");
            for (int i = 1; i < route.Count; i++)
            {
                if (!IsValidCell(route[i - 1]) || !IsValidCell(route[i])) continue;
                if (Math.Abs(route[i][0] - route[i - 1][0]) + Math.Abs(route[i][1] - route[i - 1][1]) != 1)
                    result.Warning(ValidationStage.Geometry, "invalid_intended_route", $"intended route jumps at step {i}");
                if (blocked.Contains((route[i][0], route[i][1])))
                    result.Warning(ValidationStage.Geometry, "invalid_intended_route", $"intended route step {i} crosses blocked/occupied cell ({route[i][0]},{route[i][1]})");
            }
        }
    }

    // Stage 3 — safety (task.md §5.3): provenance per hazard, controlled-state
    // contradiction check, subtlety ↔ difficulty mapping.
    public static void SafetyValidate(ScenarioData data, ValidationResult result)
    {
        if (data == null || data.Hazards == null) return;

        foreach (var hazard in data.Hazards)
        {
            if (hazard == null) continue;

            if (string.IsNullOrWhiteSpace(hazard.RegulationRef))
                result.Error(ValidationStage.Safety, "missing_regulation_ref",
                    $"hazard '{hazard.Id}' has no regulation_ref; every accepted hazard needs provenance", hazard.Id);
            if (hazard.RequiredControls == null || hazard.RequiredControls.Count == 0)
                result.Error(ValidationStage.Safety, "empty_required_controls",
                    $"hazard '{hazard.Id}' has empty required_controls", hazard.Id);

            var source = data.FindObject(hazard.SourceObjectId);
            if (source == null) continue; // schema reports the missing source

            // Controlled-state contradiction (prohibited state): a hazard whose
            // source is already fully controlled is not a real training hazard.
            // The LLM is told this in the prompt (hard rule 4); this enforces it
            // (task.md §9 risk: LLM generates a "safe" scene).
            if (string.Equals(hazard.Type, HazardFamily.fall_from_height.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (source.ConfigBool("has_guardrail"))
                    result.Error(ValidationStage.Safety, "hazard_already_controlled",
                        $"fall hazard '{hazard.Id}' source '{source.Id}' has has_guardrail: true — a guarded opening is controlled and must not be a training hazard", hazard.Id);
                if (source.ConfigBool("has_cover"))
                    result.Error(ValidationStage.Safety, "hazard_already_controlled",
                        $"fall hazard '{hazard.Id}' source '{source.Id}' has has_cover: true — a covered opening is controlled and must not be a training hazard", hazard.Id);
            }
            else if (string.Equals(hazard.Type, HazardFamily.struck_by_material.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                if (source.ConfigBool("barricaded"))
                    result.Error(ValidationStage.Safety, "hazard_already_controlled",
                        $"struck-by hazard '{hazard.Id}' source '{source.Id}' is barricaded — a barricaded source is controlled and must not be a training hazard", hazard.Id);
            }
            if (source.ConfigBool("is_controlled") || source.ConfigBool("controlled"))
                result.Error(ValidationStage.Safety, "hazard_already_controlled",
                    $"hazard '{hazard.Id}' source '{source.Id}' is marked controlled — must not be a training hazard", hazard.Id);

            // Subtlety ↔ difficulty mapping (task.md §6: novice → obvious/moderate,
            // expert → subtle). Emitted as a WARNING: task.md's own few-shot
            // example (§3) pairs a novice scenario with a subtle fall hazard, so
            // a hard error here would reject the document's canonical example.
            var difficulty = data.Meta != null ? data.Meta.Difficulty : "";
            if (difficulty == Difficulty.novice.ToString() && hazard.Subtlety == Subtlety.subtle.ToString())
                result.Warning(ValidationStage.Safety, "subtlety_difficulty_mismatch",
                    $"hazard '{hazard.Id}' is subtle in a novice scenario (mapping: novice → obvious/moderate)", hazard.Id);
            else if (difficulty == Difficulty.expert.ToString() && hazard.Subtlety != Subtlety.subtle.ToString())
                result.Warning(ValidationStage.Safety, "subtlety_difficulty_mismatch",
                    $"hazard '{hazard.Id}' is '{hazard.Subtlety}' in an expert scenario (mapping: expert → subtle)", hazard.Id);
        }
    }

    // Helpers

    static void RequireField(ValidationResult result, string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            result.Error(ValidationStage.Schema, "missing_field", $"{fieldName} is required");
    }

    static string CellString(int[] cell) => cell == null ? "null" : $"[{string.Join(",", cell)}]";

    static readonly (int dx, int dy)[] Neighbours = { (1, 0), (-1, 0), (0, 1), (0, -1) };

    static bool InBounds((int x, int y) c) => c.x >= 0 && c.x < GridSize && c.y >= 0 && c.y < GridSize;

    // Allowed same-cell combinations (task.md §5.2 "except allowed stacks" +
    // construction catalog): the mounted crane sits ON its building, so those
    // footprints may overlap; legacy scenario exceptions kept for the old
    // catalog (guardrail/tape on an opening, stacks on stacks).
    static bool IsAllowedStack(List<string> types)
    {
        if (types.All(t => t == "material_stack")) return true;
        if (types.Contains("floor_opening") && types.All(t => t == "floor_opening" || t == "guardrail" || t == "warning_tape")) return true;
        if (types.Contains("crane_mounted") && types.All(t => t == "crane_mounted" || t == "building")) return true;
        return false;
    }

    // Catalog footprint for a type (1x1 when unknown or no catalog).
    static (int w, int h) FootprintOf(ObjectCatalog catalog, string type)
    {
        var entry = catalog != null ? catalog.GetEntry(type) : null;
        if (entry == null) return (1, 1);
        return (Math.Max(1, entry.footprintX), Math.Max(1, entry.footprintY));
    }

    // All cells covered by an object's footprint, anchored at its cell.
    static IEnumerable<(int x, int y)> CoveredCells(int[] cell, (int w, int h) footprint)
    {
        for (int dx = 0; dx < footprint.w; dx++)
            for (int dy = 0; dy < footprint.h; dy++)
                yield return (cell[0] + dx, cell[1] + dy);
    }

    static HashSet<(int x, int y)> BreadthFirstSearch((int x, int y) start, HashSet<(int x, int y)> blocked)
    {
        var visited = new HashSet<(int x, int y)> { start };
        var queue = new Queue<(int x, int y)>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var (dx, dy) in Neighbours)
            {
                var next = (cell.x + dx, cell.y + dy);
                if (!InBounds(next) || blocked.Contains(next) || visited.Contains(next)) continue;
                visited.Add(next);
                queue.Enqueue(next);
            }
        }
        return visited;
    }
}

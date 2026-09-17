using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

// CS-ISR v0 plain data classes (task.md §3). [Serializable] so they can be
// shown in inspectors and embedded in the generated ScenarioAsset. Unity's
// serializer ignores the Newtonsoft attributes and uses the actual field names;
// the raw JSON is always kept verbatim alongside as provenance.

public enum Difficulty { novice, intermediate, expert }

public enum Subtlety { obvious, moderate, subtle }

public enum HazardFamily { fall_from_height, struck_by_material }

[Serializable]
public class ScenarioData
{
    public const string SchemaVersion = "0.1";

    // Tolerant reader (task.md D5): DeepSeek JSON mode only guarantees syntax,
    // never our schema. Unknown fields are ignored and minor parse problems
    // are swallowed here, to be reported as structured errors by
    // ScenarioValidator so the repair loop can act on them.
    public static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        MissingMemberHandling = MissingMemberHandling.Ignore,
        Error = (sender, args) => args.ErrorContext.Handled = true,
    };

    [JsonProperty("schema_version")] public string Version = SchemaVersion;
    [JsonProperty("scenario")] public ScenarioMeta Meta = new ScenarioMeta();
    [JsonProperty("objects")] public List<ScenarioObject> Objects = new List<ScenarioObject>();
    [JsonProperty("hazards")] public List<Hazard> Hazards = new List<Hazard>();
    [JsonProperty("task")] public ScenarioTask Task = new ScenarioTask();

    public ScenarioObject FindObject(string id) => Objects.FirstOrDefault(o => o != null && o.Id == id);
    public Hazard FindHazard(string id) => Hazards.FirstOrDefault(h => h != null && h.Id == id);

    public string ToJson() => JsonConvert.SerializeObject(this, JsonSettings);

    // The error-swallowing settings above make DeserializeObject return null
    // (instead of throwing) for top-level parse failures — literal "null",
    // empty input, or invalid JSON. A null is never a scenario: report it as
    // a parse failure so the repair loop can act on it.
    public static ScenarioData FromJson(string json)
    {
        var data = JsonConvert.DeserializeObject<ScenarioData>(json, JsonSettings);
        if (data == null)
            throw new FormatException("JSON did not deserialize to a scenario object (got null)");
        return data;
    }
}

[Serializable]
public class ScenarioMeta
{
    [JsonProperty("title")] public string Title = "";
    [JsonProperty("learning_objective")] public string LearningObjective = "";
    // Stored as string (not enum): an invalid value must survive parsing so
    // ScenarioValidator can report it as a precise schema error instead of a
    // generic parse exception.
    [JsonProperty("difficulty")] public string Difficulty = "novice";
    [JsonProperty("trade")] public string Trade = "";
    [JsonProperty("duration_hint_seconds")] public int DurationHintSeconds = 180;
}

[Serializable]
public class ScenarioObject
{
    [JsonProperty("id")] public string Id = "";
    [JsonProperty("type")] public string Type = "";
    [JsonProperty("cell")] public int[] Cell = null; // [x, y]; null if missing -> schema error
    [JsonProperty("rotation_y")] public float RotationY = 0f;
    [JsonProperty("task_role")] public string TaskRole = "";
    [JsonProperty("config")] public Dictionary<string, object> Config = new Dictionary<string, object>();

    public int CellX => Cell != null && Cell.Length > 0 ? Cell[0] : -1;
    public int CellY => Cell != null && Cell.Length > 1 ? Cell[1] : -1;

    public bool ConfigBool(string key, bool fallback = false)
    {
        return Config != null && Config.TryGetValue(key, out object value) && value is bool b ? b : fallback;
    }
}

[Serializable]
public class Hazard
{
    [JsonProperty("id")] public string Id = "";
    [JsonProperty("type")] public string Type = "";
    [JsonProperty("source_object_id")] public string SourceObjectId = "";
    // String, same reason as ScenarioMeta.Difficulty.
    [JsonProperty("subtlety")] public string Subtlety = "moderate";
    [JsonProperty("required_controls")] public List<string> RequiredControls = new List<string>();
    [JsonProperty("regulation_ref")] public string RegulationRef = "";
}

[Serializable]
public class ScenarioTask
{
    [JsonProperty("type")] public string Type = "";
    [JsonProperty("start_cell")] public int[] StartCell = null;
    [JsonProperty("goal_cell")] public int[] GoalCell = null;
    [JsonProperty("blocked_cells")] public List<int[]> BlockedCells = new List<int[]>();
    [JsonProperty("intended_route_cells")] public List<int[]> IntendedRouteCells = null; // optional
}

// Provenance asset saved next to the .json output (task.md §5.5): the verbatim
// accepted CS-ISR JSON, so no field (including extra config keys Unity's
// serializer cannot store) is ever lost.
public class ScenarioAsset : ScriptableObject
{
    [TextArea(5, 40)] public string json = "";
    public ScenarioData data;
}

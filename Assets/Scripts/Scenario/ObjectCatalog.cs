using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Closed object catalog (task.md §4 / D2): the single source for
//  1. which types the LLM may emit (CatalogPromptText -> system prompt),
//  2. which prefab instantiates each type (used by ScenarioBuilder),
//  3. the one-line safety meaning shown in the prompt table.
// Adding a hazard family later = add a row here + a prefab, no code change.

[CreateAssetMenu(menuName = "PCGML/Scenario/Object Catalog", fileName = "ObjectCatalog")]
public class ObjectCatalog : ScriptableObject
{
    public const string DefaultAssetPath = "Assets/Prefabs/Scenario/ObjectCatalog.asset";

    public List<CatalogEntry> Entries = new List<CatalogEntry>();

    [Serializable]
    public class CatalogEntry
    {
        public string type = "";
        public string displayName = "";
        [TextArea] public string safetyRole = "";
        public GameObject prefab;
        // Grid footprint in cells, anchored at the object's cell (bottom-left
        // corner), extending +x and +y. Site-scale models (building, cranes,
        // road) cover several cells; 1x1 = the classic single-cell object.
        public int footprintX = 1;
        public int footprintY = 1;
    }

    public bool Contains(string type) => GetEntry(type) != null;

    public CatalogEntry GetEntry(string type)
    {
        for (int i = 0; i < Entries.Count; i++)
            if (Entries[i] != null && Entries[i].type == type)
                return Entries[i];
        return null;
    }

    public List<string> TypeNames()
    {
        var names = new List<string>();
        foreach (var entry in Entries)
            if (entry != null && !string.IsNullOrEmpty(entry.type))
                names.Add(entry.type);
        return names;
    }

    // The catalog table embedded in the system prompt (one safety line per type,
    // including the grid footprint the LLM must respect when choosing cells).
    public string CatalogPromptText() => string.Join("\n", Entries
        .Where(e => e != null)
        .Select(e => $"- {e.type} (footprint {Math.Max(1, e.footprintX)}x{Math.Max(1, e.footprintY)} cells): {e.safetyRole}"));

    // In-memory catalog (no prefab links) so validation, prompts and self-tests
    // run before the prefab library has been generated.
    public static ObjectCatalog CreateFromDefaults()
    {
        var catalog = ScriptableObject.CreateInstance<ObjectCatalog>();
        catalog.Entries = new List<CatalogEntry>();
        foreach (var def in CatalogDefaults.Entries)
            catalog.Entries.Add(new CatalogEntry
            {
                type = def.type,
                displayName = def.displayName,
                safetyRole = def.safetyRole,
            });
        return catalog;
    }
}

// v0 catalog table (task.md §4), shared by the prompt text and the prefab factory.
public static class CatalogDefaults
{
    public static readonly (string type, string displayName, string safetyRole)[] Entries =
    {
        ("floor_slab", "Floor Slab", "the whole 20x20 m floor slab; implicit scene base — never list it in objects"),
        ("floor_opening", "Floor Opening", "hole in the floor slab; a fall-hazard source when config has_cover/has_guardrail are false"),
        ("guardrail", "Guardrail", "post-and-rail barrier; a control that removes a fall hazard at an opening"),
        ("warning_tape", "Warning Tape", "striped visual cue; makes a hazard more visible but does NOT remove it"),
        ("concrete_barrier", "Concrete Barrier", "heavy block; blocks routes and is a struck-by source if struck or moved"),
        ("material_pallet", "Material Pallet", "palletized load; the object being transported in material-transport tasks"),
        ("material_stack", "Material Stack", "stacked boxes; unstable stacks are struck-by-material hazard sources"),
        ("forklift", "Forklift", "moving equipment; a moving struck-by hazard source near workers"),
        ("worker", "Worker", "person on site; player or NPC representation"),
    };

    public static string PromptText() => string.Join("\n",
        Entries.Select(e => $"- {e.type}: {e.safetyRole}"));
}

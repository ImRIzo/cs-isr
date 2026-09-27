using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

// CS-ISR → playable scene (task.md §5.5): prefab per object, cell → world
// position (cell center × 2 m), player spawn at start_cell, and — in the
// editor — the final JSON saved as provenance assets under Assets/Scenarios/.

public class ScenarioBuilder
{
    public ObjectCatalog Catalog;
    public string RootName = "ScenarioRoot";

    public static Vector3 CellToWorld(int x, int y) =>
        new Vector3(x * ScenarioValidator.CellSizeMeters, 0f, y * ScenarioValidator.CellSizeMeters);

    public GameObject BuildIntoScene(ScenarioData data, bool clearExisting = true)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        if (clearExisting)
        {
            var previous = GameObject.Find(RootName);
            if (previous != null)
            {
#if UNITY_EDITOR
                UnityEngine.Object.DestroyImmediate(previous);
#else
                UnityEngine.Object.Destroy(previous);
#endif
            }
        }

        var root = new GameObject(RootName);
        BuildFloor(root.transform);
        BuildSiteFence(root.transform);
        if (data.Objects != null)
            foreach (var obj in data.Objects)
            {
                if (obj == null || obj.Cell == null) continue; // the validators would have rejected these
                PlaceObject(root.transform, obj);
            }
        PlacePlayer(root.transform, data);

        Debug.Log($"[Scenario] Built '{data.Meta?.Title}': {data.Objects?.Count} objects, {data.Hazards?.Count} hazards.");
        return root;
    }

    // The perimeter fence is site-scale — the square fence wraps the whole
    // site (user note 2026-09-18), so it is NOT a grid object: placed once,
    // centered on the site, and never listed in the LLM's objects.
    void BuildSiteFence(Transform parent)
    {
        var prefab = Catalog?.GetEntry("fence")?.prefab;
        if (prefab == null) return;
        var fence = (GameObject)UnityEngine.Object.Instantiate(prefab, parent);
        fence.name = "site_fence";
        float center = (ScenarioValidator.GridSize - 1) * ScenarioValidator.CellSizeMeters * 0.5f; // 9
        fence.transform.localPosition = new Vector3(center, 0f, center);
    }

    // The site ground (task.md §4: implicit, never listed by the LLM): the
    // catalog's "ground" prefab — it carries a real collider (user note
    // 2026-09-27) — with the primitive slab as fallback.
    void BuildFloor(Transform parent)
    {
        float center = (ScenarioValidator.GridSize - 1) * ScenarioValidator.CellSizeMeters * 0.5f; // 9

        var groundPrefab = Catalog?.GetEntry("ground")?.prefab;
        if (groundPrefab != null)
        {
            var ground = (GameObject)UnityEngine.Object.Instantiate(groundPrefab, parent);
            ground.name = "ground";
            // Centered on the site like the fence. The Ground prefab's collider
            // (100x0.255x100 source units, center y 0.104, at 0.2 scale) tops
            // out at ~0.046 m — lower the prefab so the top stays at y=0, the
            // floor level the player and validator assume.
            ground.transform.localPosition = new Vector3(center, -0.046f, center);
            // The Ground prefab carries a Rigidbody (user note 2026-09-27) —
            // pin it kinematic so the floor never falls or gets shoved around.
            var groundBody = ground.GetComponent<Rigidbody>();
            if (groundBody != null) groundBody.isKinematic = true;
            return;
        }

        var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slab.name = "floor_slab";
        slab.transform.SetParent(parent, false);
        slab.transform.localPosition = new Vector3(center, -0.1f, center);
        slab.transform.localScale = new Vector3(
            ScenarioValidator.GridSize * ScenarioValidator.CellSizeMeters, 0.2f,
            ScenarioValidator.GridSize * ScenarioValidator.CellSizeMeters);
        slab.GetComponent<Renderer>().sharedMaterial = PrimitiveFactory.GetMaterial(new Color(0.55f, 0.58f, 0.6f));
    }

    void PlaceObject(Transform parent, ScenarioObject obj)
    {
        var entry = Catalog?.GetEntry(obj.Type);
        var prefab = entry?.prefab;
        GameObject instance = prefab != null
            ? (GameObject)UnityEngine.Object.Instantiate(prefab, parent)
            : PrimitiveFactory.Build(obj.Type); // fallback: entry has no prefab link
        instance.name = obj.Id;
        instance.transform.SetParent(parent, false);
        // Place at the CENTER of the covered footprint (not the anchor corner)
        // so a big model visually occupies exactly the cells it claims
        // (offset = footprint cells * 2 m / 2 = footprint in meters).
        float w = entry != null ? Mathf.Max(1, entry.footprintX) : 1f;
        float h = entry != null ? Mathf.Max(1, entry.footprintY) : 1f;
        instance.transform.localPosition = CellToWorld(obj.Cell[0], obj.Cell[1]) + new Vector3(w, 0f, h);
        instance.transform.localRotation = Quaternion.Euler(0f, obj.RotationY, 0f);
    }

    void PlacePlayer(Transform parent, ScenarioData data)
    {
        var start = data.Task?.StartCell;
        if (start == null || start.Length < 2) return;
        var prefab = Catalog?.GetEntry("worker")?.prefab;
        var player = prefab != null
            ? (GameObject)UnityEngine.Object.Instantiate(prefab, parent)
            : PrimitiveFactory.Build("worker");
        player.name = "player_spawn";
        player.transform.SetParent(parent, false);
        // Cell center, same convention as the object footprints.
        player.transform.localPosition = CellToWorld(start[0], start[1]) + new Vector3(1f, 0f, 1f);
        // Walk mode: in Play mode this adds the player camera (toggle with C).
        player.AddComponent<PlayerCameraController>();
    }

#if UNITY_EDITOR
    // Provenance save (task.md §5.5 / §7 file tree): the verbatim accepted
    // CS-ISR JSON as a .json file plus a ScenarioAsset for the editor.
    public void SaveToAssets(ScenarioData data, string rawJson, string baseName)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scenarios"))
            AssetDatabase.CreateFolder("Assets", "Scenarios");

        string safe = SanitizeFileName(baseName);
        string json = string.IsNullOrWhiteSpace(rawJson) ? data.ToJson() : rawJson;
        string jsonPath = $"Assets/Scenarios/{safe}.json";
        File.WriteAllText(jsonPath, json);

        var asset = ScriptableObject.CreateInstance<ScenarioAsset>();
        asset.json = json;
        asset.data = data;
        AssetDatabase.CreateAsset(asset, $"Assets/Scenarios/{safe}.asset");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[Scenario] Saved provenance: {jsonPath} + {safe}.asset");
    }

    static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "scenario" : name;
    }
#endif
}

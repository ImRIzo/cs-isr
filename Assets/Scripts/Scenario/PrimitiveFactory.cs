using System.Collections.Generic;
using UnityEngine;

// Primitive-based placeholder prefabs (task.md §4: "prefab (v0 = Unity
// primitives)"). Used by ScenarioBuilder's runtime fallback when a catalog
// entry has no prefab link. All shapes are built in local space so a cell
// rotation (rotation_y) applies cleanly at placement time.

public static class PrimitiveFactory
{
    static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();

    static readonly Color Gray = new Color(0.58f, 0.6f, 0.62f);
    static readonly Color Yellow = new Color(1f, 0.85f, 0.1f);
    static readonly Color DarkRed = new Color(0.4f, 0.06f, 0.04f);
    static readonly Color Wood = new Color(0.55f, 0.35f, 0.15f);
    static readonly Color Cardboard = new Color(0.75f, 0.6f, 0.4f);
    static readonly Color Orange = new Color(1f, 0.55f, 0.1f);
    static readonly Color Dark = new Color(0.08f, 0.08f, 0.08f);
    static readonly Color Black = new Color(0.05f, 0.05f, 0.05f);

    public static Material GetMaterial(Color color)
    {
        if (Materials.TryGetValue(color, out var existing)) return existing;
        var shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard")
                     ?? Shader.Find("Unlit/Color");
        var material = new Material(shader);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        else if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        Materials[color] = material;
        return material;
    }

    public static GameObject Build(string type)
    {
        var root = new GameObject(type);
        var t = root.transform;
        switch (type)
        {
            case "floor_opening": BuildFloorOpening(t); break;
            case "guardrail": BuildGuardrail(t); break;
            case "warning_tape": BuildWarningTape(t); break;
            case "concrete_barrier":
                AddBox(t, "block", new Vector3(0f, 0.45f, 0f), new Vector3(1.6f, 0.9f, 0.35f), Gray);
                break;
            case "material_pallet": BuildPallet(t); break;
            case "material_stack": BuildStack(t); break;
            case "forklift": BuildForklift(t); break;
            case "worker": BuildWorker(t); break;
            case "floor_slab":
                AddBox(t, "slab", new Vector3(0f, -0.1f, 0f), new Vector3(20f, 0.2f, 20f), Gray);
                break;
            default: // unknown type: visible magenta marker so nothing is invisible
                AddBox(t, "unknown_" + type, new Vector3(0f, 0.5f, 0f), Vector3.one, Color.magenta);
                break;
        }
        return root;
    }

    static GameObject AddBox(Transform parent, string name, Vector3 center, Vector3 size, Color color)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = center;
        cube.transform.localScale = size;
        cube.GetComponent<Renderer>().sharedMaterial = GetMaterial(color);
        return cube;
    }

    static void RemoveColliders(GameObject go)
    {
        foreach (var collider in go.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(collider);
    }

    // Black quad slightly above the floor + thin red edge frame; the missing
    // collider is the "collider gap" (task.md §4: black quad + collider gap).
    static void BuildFloorOpening(Transform t)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "hole";
        quad.transform.SetParent(t, false);
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        quad.transform.localPosition = new Vector3(0f, 0.015f, 0f);
        quad.transform.localScale = new Vector3(1.7f, 1.7f, 1f);
        quad.GetComponent<Renderer>().sharedMaterial = GetMaterial(Black);
        RemoveColliders(quad);

        const float e = 0.8f; // frame edge offset from cell center
        AddBox(t, "frame_x1", new Vector3(0f, 0.02f, -e), new Vector3(1.7f, 0.04f, 0.06f), DarkRed);
        AddBox(t, "frame_x2", new Vector3(0f, 0.02f, e), new Vector3(1.7f, 0.04f, 0.06f), DarkRed);
        AddBox(t, "frame_z1", new Vector3(-e, 0.02f, 0f), new Vector3(0.06f, 0.04f, 1.7f), DarkRed);
        AddBox(t, "frame_z2", new Vector3(e, 0.02f, 0f), new Vector3(0.06f, 0.04f, 1.7f), DarkRed);
    }

    static void BuildGuardrail(Transform t)
    {
        for (float px = -0.7f; px <= 0.7f; px += 1.4f)
            for (float pz = -0.7f; pz <= 0.7f; pz += 1.4f)
                AddBox(t, "post", new Vector3(px, 0.55f, pz), new Vector3(0.08f, 1.1f, 0.08f), Yellow);
        for (float ry = 0.5f; ry <= 1.05f; ry += 0.55f)
        {
            AddBox(t, "rail_x1", new Vector3(0f, ry, -0.7f), new Vector3(1.5f, 0.06f, 0.06f), Yellow);
            AddBox(t, "rail_x2", new Vector3(0f, ry, 0.7f), new Vector3(1.5f, 0.06f, 0.06f), Yellow);
            AddBox(t, "rail_z1", new Vector3(-0.7f, ry, 0f), new Vector3(0.06f, 0.06f, 1.5f), Yellow);
            AddBox(t, "rail_z2", new Vector3(0.7f, ry, 0f), new Vector3(0.06f, 0.06f, 1.5f), Yellow);
        }
    }

    // Striped tape strip lying on the floor, built from alternating segments.
    // No texture asset (runtime textures do not survive prefab serialization —
    // verified 2026-09-18); visual cue only — no colliders, and (per the
    // safety validator) it does NOT remove a hazard.
    static void BuildWarningTape(Transform t)
    {
        const int segments = 9;
        const float length = 1.8f;
        float segWidth = length / segments;
        for (int i = 0; i < segments; i++)
        {
            var seg = AddBox(t, $"stripe_{i}",
                new Vector3(-length * 0.5f + segWidth * (i + 0.5f), 0.02f, 0f),
                new Vector3(segWidth, 0.015f, 0.15f), i % 2 == 0 ? Yellow : Dark);
            RemoveColliders(seg);
        }
    }

    static void BuildPallet(Transform t)
    {
        AddBox(t, "pallet_base", new Vector3(0f, 0.06f, 0f), new Vector3(1.2f, 0.12f, 1.0f), Wood);
        AddBox(t, "load", new Vector3(0f, 0.4f, 0f), new Vector3(0.7f, 0.55f, 0.7f), Cardboard);
    }

    static void BuildStack(Transform t)
    {
        // Three boxes with growing rotation — the "unstable stack" look.
        float[] heights = { 0.25f, 0.75f, 1.25f };
        float[] yaws = { 0f, 8f, 15f };
        for (int i = 0; i < 3; i++)
        {
            var box = AddBox(t, $"box_{i + 1}", new Vector3(0f, heights[i], 0f), new Vector3(0.85f, 0.5f, 0.85f),
                Color.Lerp(Cardboard, Wood, i * 0.25f));
            box.transform.localRotation = Quaternion.Euler(0f, yaws[i], 0f);
        }
    }

    static void BuildForklift(Transform t)
    {
        AddBox(t, "body", new Vector3(0f, 0.45f, -0.1f), new Vector3(0.9f, 0.6f, 1.2f), Orange);
        AddBox(t, "cab", new Vector3(0f, 0.95f, -0.4f), new Vector3(0.7f, 0.45f, 0.6f), Orange);
        AddBox(t, "mast_left", new Vector3(-0.28f, 1.0f, 0.55f), new Vector3(0.08f, 1.7f, 0.08f), Dark);
        AddBox(t, "mast_right", new Vector3(0.28f, 1.0f, 0.55f), new Vector3(0.08f, 1.7f, 0.08f), Dark);
        AddBox(t, "fork_left", new Vector3(-0.22f, 0.15f, 0.75f), new Vector3(0.07f, 0.06f, 0.9f), Black);
        AddBox(t, "fork_right", new Vector3(0.22f, 0.15f, 0.75f), new Vector3(0.07f, 0.06f, 0.9f), Black);
        AddBox(t, "wheel_fl", new Vector3(-0.45f, 0.16f, -0.55f), new Vector3(0.32f, 0.28f, 0.2f), Black);
        AddBox(t, "wheel_fr", new Vector3(0.45f, 0.16f, -0.55f), new Vector3(0.32f, 0.28f, 0.2f), Black);
        AddBox(t, "wheel_bl", new Vector3(-0.45f, 0.16f, 0.45f), new Vector3(0.32f, 0.28f, 0.2f), Black);
        AddBox(t, "wheel_br", new Vector3(0.45f, 0.16f, 0.45f), new Vector3(0.32f, 0.28f, 0.2f), Black);
    }

    static void BuildWorker(Transform t)
    {
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "body";
        body.transform.SetParent(t, false);
        body.transform.localPosition = new Vector3(0f, 0.95f, 0f);
        body.transform.localScale = new Vector3(0.55f, 0.9f, 0.55f);
        body.GetComponent<Renderer>().sharedMaterial = GetMaterial(Orange);

        var helmet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        helmet.name = "helmet";
        helmet.transform.SetParent(t, false);
        helmet.transform.localPosition = new Vector3(0f, 1.8f, 0f);
        helmet.transform.localScale = Vector3.one * 0.22f;
        helmet.GetComponent<Renderer>().sharedMaterial = GetMaterial(Yellow);
    }
}

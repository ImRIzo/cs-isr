using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch; // alias: System.Diagnostics.Debug would clash with UnityEngine.Debug

// M4 editor authoring UX (task.md §7): prompt box, difficulty, variants,
// generation log with per-stage timings, JSON diff between variants,
// save/load of scenario assets, API-key settings (EditorPrefs).

public class ScenarioGeneratorWindow : EditorWindow
{
    const string DefaultPrompt = "Create a novice material-transport task with one subtle fall hazard and one blocked pedestrian route.";

    string prompt = DefaultPrompt;
    int difficultyIndex = 0;
    int variantCount = 1;

    string statusLine = "";
    readonly List<string> log = new List<string>();
    readonly List<GenerationResult> results = new List<GenerationResult>();
    int diffA = -1, diffB = -1;
    int jsonIndex = -1;
    bool generating = false;

    ObjectCatalog catalog;

    Vector2 logScroll;
    Vector2 resultsScroll;
    Vector2 detailScroll;

    static readonly string[] DifficultyNames = { "novice", "intermediate", "expert" };
    static readonly string[] TabNames = { "Generate", "Settings" };
    int activeTab = 0;

    [MenuItem("Tools/PCGML Scenario/Generator Window")]
    public static void Open() => GetWindow<ScenarioGeneratorWindow>("Scenario Generator");

    void OnEnable()
    {
        catalog = LoadCatalog();
        prompt = EditorPrefs.GetString("ScenarioGenerator.Prompt", DefaultPrompt);
        difficultyIndex = Mathf.Clamp(EditorPrefs.GetInt("ScenarioGenerator.Difficulty", 0), 0, 2);
        variantCount = Mathf.Clamp(EditorPrefs.GetInt("ScenarioGenerator.Variants", 1), 1, 5);
    }

    ObjectCatalog LoadCatalog()
    {
        var loaded = AssetDatabase.LoadAssetAtPath<ObjectCatalog>(ObjectCatalog.DefaultAssetPath);
        if (loaded == null)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:ObjectCatalog"))
            {
                var candidate = AssetDatabase.LoadAssetAtPath<ObjectCatalog>(AssetDatabase.GUIDToAssetPath(guid));
                if (candidate != null) { loaded = candidate; break; }
            }
        }
        if (loaded == null)
        {
            Debug.LogWarning("[Scenario] ObjectCatalog.asset not found — using in-memory defaults (no prefab links). Run Tools > PCGML Scenario > Create Prefab Library & Catalog.");
            loaded = ObjectCatalog.CreateFromDefaults();
        }
        return loaded;
    }

    void OnGUI()
    {
        activeTab = GUILayout.Toolbar(activeTab, TabNames);
        EditorGUILayout.Space();
        switch (activeTab)
        {
            case 0: DrawGenerateTab(); break;
            case 1: DrawSettingsTab(); break;
        }
    }

    void DrawGenerateTab()
    {
        EditorGUILayout.LabelField("Training prompt (natural language):", EditorStyles.boldLabel);
        prompt = EditorGUILayout.TextArea(prompt, GUILayout.Height(48));

        EditorGUILayout.BeginHorizontal();
        difficultyIndex = EditorGUILayout.Popup("Difficulty", difficultyIndex, DifficultyNames);
        variantCount = EditorGUILayout.IntSlider("Variants", variantCount, 1, 5);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = !generating;
        if (GUILayout.Button("Generate Variants", GUILayout.Height(28))) GenerateVariants();
        if (GUILayout.Button("Build Scene From Last", GUILayout.Height(28)) && results.Count > 0)
            BuildScene(results.Count - 1);
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(statusLine))
            EditorGUILayout.HelpBox(statusLine, results.All(r => r.Success) ? MessageType.Info : MessageType.Warning);

        DrawResults();
        DrawDetail();

        EditorGUILayout.LabelField("Log:", EditorStyles.boldLabel);
        logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.Height(160));
        foreach (var line in log)
            EditorGUILayout.LabelField(line, EditorStyles.label);
        EditorGUILayout.EndScrollView();
        if (GUILayout.Button("Clear Log")) log.Clear();
    }

    void DrawResults()
    {
        if (results.Count == 0) return;
        EditorGUILayout.LabelField("Results:", EditorStyles.boldLabel);
        resultsScroll = EditorGUILayout.BeginScrollView(resultsScroll, GUILayout.Height(90));
        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(result.Success
                ? $"V{i + 1} ✓ {result.Scenario.Meta.Title} — {result.TotalSeconds:0.0}s ({result.AttemptsUsed} attempt(s))"
                : $"V{i + 1} ✗ REJECTED — {result.TotalSeconds:0.0}s", GUILayout.MinWidth(240));
            if (GUILayout.Button("Build")) BuildScene(i);
            if (result.Success && GUILayout.Button("Save")) SaveScenario(i);
            if (result.Success && GUILayout.Button("JSON")) jsonIndex = (jsonIndex == i) ? -1 : i;
            if (GUILayout.Button(IsDiffSelected(i) ? "Diff: on" : "Diff"))
                ToggleDiff(i);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    void DrawDetail()
    {
        bool showDiff = diffA >= 0 && diffB >= 0 && diffA < results.Count && diffB < results.Count;
        bool showJson = !showDiff && jsonIndex >= 0 && jsonIndex < results.Count && results[jsonIndex].Success;

        if (showJson)
        {
            EditorGUILayout.LabelField($"Variant {jsonIndex + 1} JSON:", EditorStyles.boldLabel);
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll, GUILayout.Height(200));
            EditorGUILayout.TextArea(Pretty(results[jsonIndex].RawJson), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
        else if (showDiff)
        {
            EditorGUILayout.LabelField($"Diff V{diffA + 1} → V{diffB + 1} (green added, red removed):", EditorStyles.boldLabel);
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll, GUILayout.Height(200));
            foreach (var (kind, line) in DiffLines(Pretty(results[diffA].RawJson), Pretty(results[diffB].RawJson)))
            {
                var style = new GUIStyle(EditorStyles.label);
                style.normal.textColor = kind == "added" ? new Color(0.2f, 0.85f, 0.3f)
                    : kind == "removed" ? new Color(1f, 0.35f, 0.3f)
                    : new Color(0.7f, 0.7f, 0.7f);
                EditorGUILayout.LabelField(line, style);
            }
            EditorGUILayout.EndScrollView();
        }
    }

    bool IsDiffSelected(int i) => diffA == i || diffB == i;

    void ToggleDiff(int i)
    {
        if (diffA == i) { diffA = -1; return; }
        if (diffB == i) { diffB = -1; return; }
        if (diffA == -1) diffA = i;
        else if (diffB == -1) diffB = i;
        else { diffA = i; diffB = -1; }
    }

    // Generation (target ≤ 10 s per scenario — task.md §1; timing is logged).

    async void GenerateVariants()
    {
        if (generating) return;
        generating = true;

        EditorPrefs.SetString("ScenarioGenerator.Prompt", prompt);
        EditorPrefs.SetInt("ScenarioGenerator.Difficulty", difficultyIndex);
        EditorPrefs.SetInt("ScenarioGenerator.Variants", variantCount);

        results.Clear();
        diffA = diffB = -1;
        jsonIndex = -1;
        AddLog($"=== generating {variantCount} variant(s), difficulty={DifficultyNames[difficultyIndex]} ===");

        var generator = new ScenarioGenerator { Client = new DeepSeekClient(), Catalog = catalog };
        var stopwatch = Stopwatch.StartNew();
        int accepted = 0;
        for (int i = 0; i < variantCount; i++)
        {
            AddLog($"--- variant {i + 1}/{variantCount} ---");
            var result = await generator.GenerateAsync(prompt, (Difficulty)difficultyIndex, i, variantCount, AddLog);
            results.Add(result);
            if (result.Success) accepted++;
            Repaint();
        }
        stopwatch.Stop();
        statusLine = $"{accepted}/{variantCount} accepted in {stopwatch.Elapsed.TotalSeconds:0.0}s total (target ≤ 10 s per scenario)";
        AddLog(statusLine);
        generating = false;
        Repaint();
    }

    void BuildScene(int index)
    {
        var result = results[index];
        if (!result.Success) return;
        new ScenarioBuilder { Catalog = catalog }.BuildIntoScene(result.Scenario);
        statusLine = $"Built '{result.Scenario.Meta.Title}' into the open scene.";
    }

    void SaveScenario(int index)
    {
        var result = results[index];
        if (!result.Success) return;
        new ScenarioBuilder { Catalog = catalog }.SaveToAssets(result.Scenario, result.RawJson, $"v{index + 1}_{result.Scenario.Meta.Title}");
        statusLine = "Saved under Assets/Scenarios/.";
    }

    void AddLog(string line)
    {
        log.Add(line);
        if (log.Count > 500) log.RemoveAt(0);
        Repaint();
    }

    static string Pretty(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try { return Newtonsoft.Json.Linq.JToken.Parse(json).ToString(Newtonsoft.Json.Formatting.Indented); }
        catch { return json; }
    }

    // Line-based LCS diff between two JSON texts ("added"/"removed"/"context").
    static List<(string kind, string line)> DiffLines(string a, string b)
    {
        var al = a.Replace("\r\n", "\n").Split('\n');
        var bl = b.Replace("\r\n", "\n").Split('\n');
        int n = al.Length, m = bl.Length;
        var dp = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                dp[i, j] = al[i] == bl[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
        var lines = new List<(string, string)>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (al[x] == bl[y]) { lines.Add(("context", " " + al[x])); x++; y++; }
            else if (dp[x + 1, y] >= dp[x, y + 1]) { lines.Add(("removed", "-" + al[x])); x++; }
            else { lines.Add(("added", "+" + bl[y])); y++; }
        }
        while (x < n) { lines.Add(("removed", "-" + al[x])); x++; }
        while (y < m) { lines.Add(("added", "+" + bl[y])); y++; }
        return lines;
    }

    void DrawSettingsTab()
    {
        EditorGUILayout.HelpBox("The API key stays on this machine (EditorPrefs). Never hardcode it — task.md §7.", MessageType.Info);

        EditorGUILayout.LabelField("DeepSeek API key:", EditorStyles.boldLabel);
        var key = EditorGUILayout.PasswordField(DeepSeekClient.ApiKey, GUILayout.Width(420));
        var model = EditorGUILayout.TextField("Model", EditorPrefs.GetString(DeepSeekClient.ModelPref, "deepseek-v4-flash"));
        var endpoint = EditorGUILayout.TextField("Endpoint", EditorPrefs.GetString(DeepSeekClient.EndpointPref, "https://api.deepseek.com/chat/completions"));

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save Settings", GUILayout.Height(26)))
        {
            DeepSeekClient.ApiKey = key;
            EditorPrefs.SetString(DeepSeekClient.ModelPref, model);
            EditorPrefs.SetString(DeepSeekClient.EndpointPref, endpoint);
            AddLog("settings saved to EditorPrefs");
        }
        if (GUILayout.Button("Test Connection (hello world)", GUILayout.Height(26))) SmokeTest();
        EditorGUILayout.EndHorizontal();
    }

    async void SmokeTest()
    {
        AddLog("smoke test: sending hello-world prompt to DeepSeek…");
        try
        {
            var client = new DeepSeekClient();
            var stopwatch = Stopwatch.StartNew();
            string response = await client.CompleteAsync(new List<ChatMessage>
            {
                new ChatMessage("system", "You are a JSON echo service. Reply with a single JSON object only."),
                new ChatMessage("user", "Return the JSON object {\"hello\": \"world\"} and nothing else."),
            }, 0.2f);
            AddLog($"smoke test OK in {stopwatch.Elapsed.TotalSeconds:0.0}s: {response.Trim()}");
        }
        catch (Exception ex)
        {
            AddLog("smoke test FAILED: " + ex.Message);
        }
    }
}

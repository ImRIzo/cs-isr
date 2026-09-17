using System;
using UnityEngine;
using UnityEngine.UI;

// Runtime (Play mode) entry point for the CS-ISR pipeline — the M5 direction.
// Mirrors the editor window flow: prompt from the UI → ScenarioGenerator
// (DeepSeek) → validate + repair loop → ScenarioBuilder instantiates the
// accepted scene. Lives outside the Editor folder so it also compiles into
// player builds (the real client reads ~/deepseek_config.json there).

public class RuntimeScenarioUI : MonoBehaviour
{
    [Header("UI references")]
    public InputField promptInput;
    public Button generateButton;
    public Text statusText;

    [Header("Pipeline")]
    public ObjectCatalog catalog;

    // Difficulty is read from the prompt text itself (PromptBuilder.DetectDifficulty).
    const string DefaultPrompt = "novice transport hazard";

    bool generating = false;

    void OnEnable()
    {
        if (generateButton != null) generateButton.onClick.AddListener(OnGenerateClicked);
    }

    void OnDisable()
    {
        if (generateButton != null) generateButton.onClick.RemoveListener(OnGenerateClicked);
    }

    void Start()
    {
        if (promptInput != null && string.IsNullOrEmpty(promptInput.text))
            promptInput.text = DefaultPrompt;
    }

    async void OnGenerateClicked()
    {
        if (generating) return;
        if (catalog == null)
        {
            SetStatus("No catalog assigned — drag Assets/Prefabs/Scenario/ObjectCatalog.asset into the Catalog slot.");
            return;
        }
        if (promptInput == null || string.IsNullOrWhiteSpace(promptInput.text))
        {
            SetStatus("Type a training prompt first.");
            return;
        }

        generating = true;
        if (generateButton != null) generateButton.interactable = false;
        var difficulty = PromptBuilder.DetectDifficulty(promptInput.text);
        SetStatus($"Generating… (difficulty: {difficulty}, from prompt)");

        try
        {
            // Editor Play mode: EditorPrefs key from the generator window's
            // Settings tab; player builds: ~/deepseek_config.json.
            var generator = new ScenarioGenerator { Client = new DeepSeekClient(), Catalog = catalog };
            var result = await generator.GenerateAsync(promptInput.text, difficulty, 0, 1, AppendStatus);

            if (result.Success)
            {
                new ScenarioBuilder { Catalog = catalog }.BuildIntoScene(result.Scenario);
                SetStatus($"ACCEPTED '{result.Scenario.Meta.Title}' — scene built in {result.TotalSeconds:0.0}s.");
            }
            else
            {
                SetStatus("REJECTED: " + result.FailureReason);
            }
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message);
        }
        finally
        {
            generating = false;
            if (generateButton != null) generateButton.interactable = true;
        }
    }

    void SetStatus(string line)
    {
        if (statusText != null) statusText.text = line;
        Debug.Log("[Scenario] " + line);
    }

    // Keeps the on-screen log readable — the full trail is in the console.
    void AppendStatus(string line)
    {
        if (statusText != null)
        {
            var lines = (statusText.text + "\n" + line).Split('\n');
            var tail = new string[Mathf.Min(lines.Length, 8)];
            Array.Copy(lines, lines.Length - tail.Length, tail, 0, tail.Length);
            statusText.text = string.Join("\n", tail);
        }
        Debug.Log("[Scenario] " + line);
    }
}

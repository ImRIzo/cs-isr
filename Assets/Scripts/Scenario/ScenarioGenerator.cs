using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

// Orchestrator (task.md §2 / §5.4): prompt → parse → validate → repair loop.
// The LLM is the grounded semantic planner; this class is the deterministic
// gate. Every rejected candidate leaves a structured trail in the log
// (task.md §10: auditable accept/reject trail).

public class GenerationResult
{
    public bool Success = false;
    public ScenarioData Scenario;
    public string RawJson = ""; // last candidate as returned (provenance)
    public List<ValidationResult> Attempts = new List<ValidationResult>();
    public int AttemptsUsed = 0;
    public double TotalSeconds = 0;
    public List<string> Log = new List<string>();
    public string FailureReason = "";
}

public class ScenarioGenerator
{
    public ILLMClient Client;
    public ObjectCatalog Catalog;
    public int MaxRepairRetries = 2; // task.md §5.4

    public const float InitialTemperature = 0.7f; // variation (task.md §6)
    public const float RepairTemperature = 0.2f;  // repair passes

    public async Task<GenerationResult> GenerateAsync(
        string trainingPrompt, Difficulty difficulty, int variantIndex = 0, int variantCount = 1,
        Action<string> onLog = null)
    {
        var result = new GenerationResult();
        var stopwatch = Stopwatch.StartNew();
        void Log(string line)
        {
            line = $"[{stopwatch.Elapsed.TotalSeconds:0.0}s] {line}";
            result.Log.Add(line);
            onLog?.Invoke(line);
        }

        Log($"generating variant {variantIndex + 1}/{variantCount} (difficulty: {difficulty}, client: {Client?.GetType().Name}, max {MaxRepairRetries} repair retries)");

        var messages = new List<ChatMessage>
        {
            new ChatMessage("system", PromptBuilder.BuildSystemPrompt(Catalog)),
            new ChatMessage("user", PromptBuilder.BuildUserPrompt(trainingPrompt, difficulty, variantIndex, variantCount)),
        };

        for (int attempt = 0; attempt <= MaxRepairRetries; attempt++)
        {
            string raw;
            var callStopwatch = Stopwatch.StartNew();
            try
            {
                raw = await Client.CompleteAsync(messages, attempt == 0 ? InitialTemperature : RepairTemperature);
            }
            catch (Exception ex)
            {
                Log($"client call failed: {ex.Message} — aborting");
                result.FailureReason = "client error: " + ex.Message;
                break;
            }
            Log($"attempt {attempt + 1}: received {(raw == null ? 0 : raw.Length)} chars in {callStopwatch.Elapsed.TotalSeconds:0.0}s (temperature {(attempt == 0 ? InitialTemperature : RepairTemperature)})");
            result.RawJson = raw;

            var validation = new ValidationResult();
            ScenarioData parsed = null;
            try
            {
                parsed = ScenarioData.FromJson(PromptBuilder.StripCodeFences(raw));
            }
            catch (Exception ex)
            {
                validation.Error(ValidationStage.Schema, "json_parse",
                    "response is not parseable JSON: " + ex.Message + " — raw: " + Snippet(raw));
            }
            if (parsed == null) // never vacuous-accept an empty parse (was an NRE at 'accepted' log)
                validation.Error(ValidationStage.Schema, "json_parse", "response did not produce a scenario object");
            result.Attempts.Add(validation);

            var validationStopwatch = Stopwatch.StartNew();
            if (parsed != null)
                ScenarioValidator.ValidateAll(parsed, Catalog, validation);
            Log($"attempt {attempt + 1} validation: {validation.Errors.Count()} error(s), {validation.Warnings.Count()} warning(s) in {validationStopwatch.Elapsed.TotalMilliseconds:0.0} ms");

            if (validation.IsValid)
            {
                result.Success = true;
                result.Scenario = parsed;
                result.AttemptsUsed = attempt + 1;
                Log($"accepted '{parsed?.Meta?.Title}' after {attempt + 1} attempt(s)");
                break;
            }

            foreach (var issue in validation.Issues.Take(10))
                Log($"  {issue.ToLine()}");
            if (validation.Issues.Count > 10)
                Log($"  … {validation.Issues.Count - 10} more");

            if (attempt < MaxRepairRetries)
            {
                // Multi-turn repair: append the failed candidate and the
                // structured error list to the SAME session (task.md §10,
                // open decision 4 — cheaper than a fresh session).
                messages.Add(new ChatMessage("assistant", raw));
                messages.Add(new ChatMessage("user", PromptBuilder.BuildRepairPrompt(validation)));
                Log("sending structured errors back for repair");
            }
            else
            {
                result.FailureReason = BuildRejectionReport(validation, attempt + 1);
                Log("rejected after max retries — see rejection report");
                Log(result.FailureReason);
            }
        }

        stopwatch.Stop();
        result.TotalSeconds = stopwatch.Elapsed.TotalSeconds;
        Log($"variant {variantIndex + 1} finished in {result.TotalSeconds:0.0}s: {(result.Success ? "ACCEPTED" : "REJECTED")}");
        return result;
    }

    static string Snippet(string raw, int max = 200) =>
        string.IsNullOrEmpty(raw) ? "null" : raw.Length <= max ? raw : raw.Substring(0, max) + "…";

    // The audit trail required by task.md §5.4: why the candidate was rejected.
    static string BuildRejectionReport(ValidationResult validation, int attempts)
    {
        return $"candidate rejected after {attempts} attempt(s): " +
               $"{validation.Errors.Count()} error(s), {validation.Warnings.Count()} warning(s) — " +
               string.Join("; ", validation.Errors.Select(e => e.ToLine()));
    }
}

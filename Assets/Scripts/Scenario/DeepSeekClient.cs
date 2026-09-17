using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

// Direct DeepSeek call (task.md §7 / D3): OpenAI-compatible REST through
// UnityWebRequest, no SDK. Sequential calls with a small backoff retry on
// 429/5xx and network failures (task.md §9). The API key is never hardcoded:
// EditorPrefs inside the editor (set from the generator window's Settings tab),
// ~/deepseek_config.json outside it.

public class DeepSeekClient : ILLMClient
{
    public const string ApiKeyPref = "DeepSeek.ApiKey";
    public const string ModelPref = "DeepSeek.Model";
    public const string EndpointPref = "DeepSeek.Endpoint";

    public string Model = "deepseek-v4-flash"; // task.md §7: verify the current model name in DeepSeek docs
    public string Endpoint = "https://api.deepseek.com/chat/completions";
    public int MaxTokens = 4000;
    public int MaxRetries = 2; // transport retries on 429/5xx/network errors only
    public int TimeoutSeconds = 90;

    public DeepSeekClient()
    {
        // Runtime config wins (AppManager component in the scene); the
        // editor-only Settings tab (EditorPrefs) is the fallback for tooling.
        var app = AppManager.Instance;
        if (app != null)
        {
            if (!string.IsNullOrEmpty(app.model)) Model = app.model;
            if (!string.IsNullOrEmpty(app.endpoint)) Endpoint = app.endpoint;
            if (app.maxTokens > 0) MaxTokens = app.maxTokens;
            if (app.maxRetries >= 0) MaxRetries = app.maxRetries;
            if (app.requestTimeoutSeconds > 0) TimeoutSeconds = app.requestTimeoutSeconds;
            return;
        }
#if UNITY_EDITOR
        if (UnityEditor.EditorPrefs.HasKey(ModelPref)) Model = UnityEditor.EditorPrefs.GetString(ModelPref);
        if (UnityEditor.EditorPrefs.HasKey(EndpointPref)) Endpoint = UnityEditor.EditorPrefs.GetString(EndpointPref);
#endif
    }

    public static string ApiKey
    {
        get
        {
            var app = AppManager.Instance;
            if (app != null && !string.IsNullOrEmpty(app.apiKey)) return app.apiKey;
#if UNITY_EDITOR
            return UnityEditor.EditorPrefs.GetString(ApiKeyPref, "");
#else
            // Player builds: git-ignored config file in the user's home dir.
            try
            {
                string path = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "deepseek_config.json");
                if (System.IO.File.Exists(path))
                {
                    var config = JObject.Parse(System.IO.File.ReadAllText(path));
                    return (string)config["api_key"] ?? "";
                }
            }
            catch { /* fall through to "key not set" */ }
            return "";
#endif
        }
        set
        {
#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetString(ApiKeyPref, value);
#endif
        }
    }

    public async Task<string> CompleteAsync(List<ChatMessage> messages, float temperature)
    {
        if (string.IsNullOrEmpty(ApiKey))
            throw new InvalidOperationException(
                "DeepSeek API key not set — paste it into the AppManager component in the scene " +
                "(or Tools > PCGML Scenario > Generator Window > Settings for editor tooling).");

        var body = new JObject
        {
            ["model"] = Model,
            ["messages"] = new JArray(messages.Select(m => new JObject { ["role"] = m.role, ["content"] = m.content })),
            ["temperature"] = temperature,
            ["max_tokens"] = MaxTokens,
            ["response_format"] = new JObject { ["type"] = "json_object" },
            // task.md §6: deterministic JSON planning, not reasoning — thinking
            // mode would put the answer in reasoning_content and leave content
            // empty (exactly the 0-chars symptom seen on 2026-09-18).
            ["thinking_mode"] = "non-thinking",
        };

        // Full transparency: log the exact payload leaving the app.
        Debug.Log($"[Scenario] OUT -> DeepSeek ({Endpoint}, model {Model}): {body.ToString(Newtonsoft.Json.Formatting.None)}");

        Exception lastError = null;
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            bool retryableFailure = false;
            try
            {
                using var request = new UnityWebRequest(Endpoint, UnityWebRequest.kHttpVerbPOST);
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None)));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + ApiKey);
                request.timeout = TimeoutSeconds;

                await request.SendWebRequest();

                long status = request.responseCode;
                if (status >= 200 && status < 300)
                {
                    // And the exact payload arriving back (one log per attempt,
                    // so repair-loop passes show as repeated OUT/IN pairs).
                    Debug.Log($"[Scenario] IN <- DeepSeek ({request.downloadHandler.text.Length} chars): {request.downloadHandler.text}");
                    return ExtractContent(request.downloadHandler.text);
                }

                // Error responses are part of the trail too.
                Debug.LogWarning($"[Scenario] IN <- DeepSeek HTTP {status}: {Trim(request.downloadHandler.text, 500)}");

                // 0 = network-level failure (timeout/DNS), also retryable.
                bool retryable = status == 0 || status == 429 || status >= 500;
                if (retryable && attempt < MaxRetries)
                {
                    retryableFailure = true;
                    lastError = new InvalidOperationException(
                        $"DeepSeek HTTP {(status == 0 ? "network error" : status.ToString())}; retrying ({attempt + 1}/{MaxRetries})");
                }
                else
                {
                    lastError = new InvalidOperationException(
                        $"DeepSeek HTTP {(status == 0 ? "network error" : status.ToString())}: {Trim(request.downloadHandler.text, 500)}");
                }
            }
            catch (InvalidOperationException) { throw; } // our own errors already carry the message
            catch (Exception ex)
            {
                retryableFailure = attempt < MaxRetries;
                lastError = new InvalidOperationException($"DeepSeek request failed: {ex.Message}");
            }

            if (!retryableFailure) break;
            await Task.Delay(500 * (1 << attempt)); // 0.5 s, 1 s backoff
        }

        throw lastError ?? new InvalidOperationException("DeepSeek request failed");
    }

    static string ExtractContent(string responseText)
    {
        try
        {
            var root = JObject.Parse(responseText);
            var message = root["choices"]?[0]?["message"];
            var content = message?["content"];
            // If the server ignores thinking_mode and answers in reasoning
            // mode, the text lands in reasoning_content with content empty —
            // accept that shape instead of failing.
            if (content == null || (content.Type == JTokenType.String && ((string)content).Length == 0))
            {
                var reasoning = message?["reasoning_content"];
                if (reasoning != null && reasoning.Type == JTokenType.String && ((string)reasoning).Length > 0)
                    return (string)reasoning;
                throw new InvalidOperationException(
                    $"response has no usable choices[0].message.content: {Trim(responseText, 500)}");
            }
            return (string)content;
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            throw new InvalidOperationException($"unparseable DeepSeek response: {ex.Message}");
        }
    }

    static string Trim(string text, int max) =>
        string.IsNullOrEmpty(text) || text.Length <= max ? text : text.Substring(0, max) + "…";
}

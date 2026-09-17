using UnityEngine;

// Central runtime configuration for the scenario pipeline — endpoint, model,
// API key. The user-facing alternative to EditorPrefs for Play mode and
// builds: put this component on an empty GameObject in the scene and paste
// the key into the Inspector (the key then lives in the scene file — fine for
// a local prototype, but never commit it to version control).
public class AppManager : MonoBehaviour
{
    public static AppManager Instance { get; private set; }

    [Header("DeepSeek API")]
    public string apiKey = "";
    public string model = "deepseek-v4-flash";
    public string endpoint = "https://api.deepseek.com/chat/completions";
    public int maxTokens = 4000;
    public int maxRetries = 2;
    public int requestTimeoutSeconds = 90;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
#if UNITY_EDITOR
        // Editor Play-mode fallback: a key saved via Tools > PCGML Scenario >
        // Generator Window > Settings (EditorPrefs) fills an empty field.
        if (string.IsNullOrEmpty(apiKey))
            apiKey = UnityEditor.EditorPrefs.GetString(DeepSeekClient.ApiKeyPref, "");
#endif
    }
}

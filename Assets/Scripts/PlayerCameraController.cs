using UnityEngine;
using UnityEngine.EventSystems;

// First-person walk mode for the generated scenario. ScenarioBuilder puts
// this on the player_spawn worker; in Play mode press C to switch from the
// free-fly camera to the player's own camera and back.
//
//   C        toggle between the fly camera and the player view
//   WASD     walk (Shift to run)
//   Mouse    look around (cursor locked while in player view)
//
// The fly camera GameObject is disabled while walking (never moved), so
// pressing C again restores its previous view exactly. The camera rig is
// built in Awake so the editor window's edit-mode scene builds stay clean.

public class PlayerCameraController : MonoBehaviour
{
    public float walkSpeed = 2f;
    public float runMultiplier = 2f;
    public float lookSensitivity = 2.5f;
    public float eyeHeight = 0.45f;

    FreeFlyCamera flyCamera;
    GameObject playerCamera;
    CharacterController body;
    Renderer[] bodyRenderers;

    float yaw;
    float pitch;
    bool active;

    void Awake()
    {
        flyCamera = Object.FindFirstObjectByType<FreeFlyCamera>();
        if (flyCamera == null)
        {
            enabled = false; // no fly camera to toggle to (e.g. a scene without one)
            return;
        }

        // The worker primitive's capsule/sphere colliders are replaced by a
        // single CharacterController. The body is hidden while in player view
        // (the camera sits inside it).
        foreach (var c in GetComponentsInChildren<Collider>())
            c.enabled = false;
        bodyRenderers = GetComponentsInChildren<Renderer>();

        // Sized to the toy-scale worker (~0.6 m tall, matching the 0.2x
        // construction prefabs) — keep in sync with PrimitiveFactory.BuildWorker.
        body = gameObject.AddComponent<CharacterController>();
        body.height = 0.6f;
        body.radius = 0.12f;
        body.center = new Vector3(0f, 0.3f, 0f);

        // Eye-height camera as a child so it rides along with the player.
        playerCamera = new GameObject("player_camera");
        playerCamera.transform.SetParent(transform, false);
        playerCamera.transform.localPosition = new Vector3(0f, eyeHeight, 0f);
        var cam = playerCamera.AddComponent<Camera>();
        cam.fieldOfView = 70f;
        cam.nearClipPlane = 0.05f;
        playerCamera.AddComponent<AudioListener>();
        playerCamera.SetActive(false);
    }

    void Update()
    {
        if (playerCamera == null) return;

        // C toggles — unless the user is typing in the prompt field.
        if (Input.GetKeyDown(KeyCode.C) && !FreeFlyCamera.TypingInInputField())
            SetActive(!active);

        if (!active) return;

        // Look: the mouse orbits the camera on the player.
        yaw += Input.GetAxis("Mouse X") * lookSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * lookSensitivity;
        pitch = Mathf.Clamp(pitch, -89f, 89f);
        playerCamera.transform.localRotation = Quaternion.Euler(pitch, yaw, 0f);

        // Walk: camera-relative, flat on the ground plane.
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");
        var forward = playerCamera.transform.forward; forward.y = 0f; forward.Normalize();
        var right = playerCamera.transform.right; right.y = 0f; right.Normalize();
        float speed = walkSpeed * (Input.GetKey(KeyCode.LeftShift) ? runMultiplier : 1f);
        body.Move((forward * z + right * x).normalized * speed * Time.deltaTime);
    }

    void SetActive(bool value)
    {
        active = value;
        if (flyCamera != null) flyCamera.gameObject.SetActive(!value);
        playerCamera.SetActive(value);
        SetBodyVisible(!value);

        if (value)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            // Release a focused prompt field so WASD walks, not types.
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void SetBodyVisible(bool visible)
    {
        if (bodyRenderers == null) return;
        foreach (var r in bodyRenderers)
            if (r != null) r.enabled = visible;
    }

    void OnDisable()
    {
        // Safety net: if the player is torn down mid-walk (e.g. a scenario
        // rebuild), hand the view back to the fly camera.
        if (active && flyCamera != null)
        {
            flyCamera.gameObject.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        active = false;
    }

    void OnGUI()
    {
        if (!active) return;
        GUI.Label(new Rect(10, 45, 400, 24), "Player view — WASD walk, mouse look, C to return to fly camera");
    }
}

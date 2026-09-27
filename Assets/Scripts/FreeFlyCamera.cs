using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Free-fly camera: attach to the camera GameObject and press Play.
// Uses the legacy Input Manager (the project's Active Input Handling is
// "Both", so UnityEngine.Input works).
//
//   WASD            move (relative to where the camera faces)
//   Space / Ctrl    up / down
//   Shift           hold to move faster
//   Right mouse     hold + drag to look around (cursor locks while held)
//   Mouse wheel     change the movement speed
//
// All input is suspended while the prompt field has focus, so typing a
// prompt never steers the camera.

public class FreeFlyCamera : MonoBehaviour
{
    public float moveSpeed = 12f;
    public float fastMultiplier = 3f;
    public float lookSensitivity = 2.5f;
    public float scrollSpeedStep = 2f;
    public float minSpeed = 1f;
    public float maxSpeed = 80f;

    float yaw;
    float pitch;

    void Start()
    {
        // Preserve the camera's current framing as the starting view.
        var euler = transform.eulerAngles;
        yaw = euler.y;
        pitch = euler.x;
    }

    void Update()
    {
        // Typing in the prompt field must not steer the camera.
        if (TypingInInputField()) return;

        // Look: hold the right mouse button and drag.
        if (Input.GetMouseButton(1))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            yaw += Input.GetAxis("Mouse X") * lookSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * lookSensitivity;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Move in the direction the camera faces, flat on the ground plane.
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");
        float up = (Input.GetKey(KeyCode.Space) ? 1f : 0f)
                 - (Input.GetKey(KeyCode.LeftControl) ? 1f : 0f);
        float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? fastMultiplier : 1f);

        Vector3 direction = transform.right * x + transform.forward * z + Vector3.up * up;
        if (direction.sqrMagnitude > 0.001f)
            transform.position += direction.normalized * speed * Time.deltaTime;

        // Mouse wheel adjusts the base speed.
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
            moveSpeed = Mathf.Clamp(moveSpeed + scroll * scrollSpeedStep, minSpeed, maxSpeed);
    }

    // True while a UI input field has keyboard focus (the prompt field) —
    // camera controls (and the player-camera toggle) stay out of the way
    // while the user is typing.
    public static bool TypingInInputField()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return selected != null && selected.GetComponent<InputField>() != null;
    }
}

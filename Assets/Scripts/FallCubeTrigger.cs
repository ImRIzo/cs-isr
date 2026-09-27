using UnityEngine;

// One-shot falling-hazard trigger for the ground crane prefab: the cube
// (fall_cube, Rigidbody mass 5) waits on the crane head and drops when the
// player walks into the trigger zone.
//
// ATTACH TO: the GameObject that holds the trigger BoxCollider.
// The cube is auto-wired by name if the Fall Cube field is left empty, and
// held with isKinematic until the trigger fires — so it no longer falls the
// moment Play starts.

public class FallCubeTrigger : MonoBehaviour
{
    [Tooltip("The falling cube's Rigidbody. Auto-found by name 'fall_cube' if left empty.")]
    public Rigidbody fallCube;

    [Tooltip("Extra downward velocity applied at release (m/s). 0 = pure gravity.")]
    public float releaseBoost = 0f;

    [Tooltip("Bounce energy kept on impact (0 = dead drop, 1 = perfect bounce).")]
    public float bounce = 0.5f;

    bool fallen;

    void Start()
    {
        if (fallCube == null) fallCube = FindFallCube();
        if (fallCube == null)
        {
            Debug.LogWarning($"[FallCubeTrigger] No Rigidbody found on 'fall_cube' — trigger disabled.", this);
            enabled = false;
            return;
        }
        // Hold the cube on the crane head until the player walks in.
        fallCube.isKinematic = true;
        // The cube drops ~6 m and arrives fast enough to tunnel through the
        // 0.05 m-thin ground collider between physics steps — continuous
        // dynamic detection sweeps it against static and kinematic bodies
        // (the ground prefab now carries a Rigidbody).
        fallCube.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        // Bounce on landing: default physics materials have bounciness 0, so
        // the cube currently stops dead. Unity combines two colliders'
        // bounciness as the MAX — a bouncy cube is enough. A user-assigned
        // bouncy material on the cube is left alone.
        var col = fallCube.GetComponent<Collider>();
        if (col != null && (col.sharedMaterial == null || col.sharedMaterial.bounciness <= 0f))
        {
            col.sharedMaterial = new PhysicsMaterial("fall_cube_bounce")
            {
                bounciness = bounce,
                bounceCombine = PhysicsMaterialCombine.Maximum,
            };
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (fallen) return;
        // The player is the worker with PlayerCameraController — its
        // CharacterController is the collider that enters the zone.
        if (other.GetComponentInParent<PlayerCameraController>() == null) return;
        Release();
    }

    void Release()
    {
        fallen = true;
        fallCube.isKinematic = false;
        if (releaseBoost > 0f)
            fallCube.linearVelocity = Vector3.down * releaseBoost;
        Debug.Log($"[FallCubeTrigger] Player entered the zone — released '{fallCube.name}'.");
    }

    Rigidbody FindFallCube()
    {
        foreach (var t in transform.root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "fall_cube") continue;
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null) return rb;
        }
        return null;
    }

    void Reset()
    {
        // Editor convenience: auto-wire the cube when the component is added.
        fallCube = FindFallCube();
    }
}

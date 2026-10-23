using UnityEngine;

/// <summary>Keeps a world-space label (e.g. arena name tag) facing the main camera.</summary>
public class FaceCameraBillboard : MonoBehaviour
{
    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position, Vector3.up);
    }
}

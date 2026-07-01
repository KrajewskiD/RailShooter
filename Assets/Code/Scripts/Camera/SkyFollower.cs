using UnityEngine;

public class SkyFollower : MonoBehaviour
{
    private Transform cameraTransform;

    void Start()
    {
        BindCamera();
    }

    void LateUpdate()
    {
        if (cameraTransform == null)
            BindCamera();

        if (cameraTransform != null)
        {
            transform.position = cameraTransform.position;
        }
    }

    private void BindCamera()
    {
        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
            return;
        }

        Camera fallback = FindFirstObjectByType<Camera>();
        if (fallback != null)
            cameraTransform = fallback.transform;
    }
}

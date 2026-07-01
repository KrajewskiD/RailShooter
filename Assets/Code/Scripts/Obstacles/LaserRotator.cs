using UnityEngine;
public class LaserRotator : MonoBehaviour
{
    [Header("Settings (Set via Initialize)")]
    public RotationAxis rotationAxis;
    public float speed;

    private bool isInitialized = false;

    public void Initialize(RotationAxis axis, float rotSpeed)
    {
        rotationAxis = axis;
        speed = rotSpeed;
        isInitialized = true;
    }

    void Update()
    {
        if (!isInitialized) return;

        float rotationAmount = speed * Time.deltaTime;

        switch (rotationAxis)
        {
            case RotationAxis.LocalX:
                transform.Rotate(Vector3.right * rotationAmount, Space.World);
                break;
            case RotationAxis.LocalY:
                transform.Rotate(Vector3.up * rotationAmount, Space.World);
                break;
            case RotationAxis.LocalZ:
                transform.Rotate(Vector3.forward * rotationAmount, Space.World);
                break;
        }
    }
}

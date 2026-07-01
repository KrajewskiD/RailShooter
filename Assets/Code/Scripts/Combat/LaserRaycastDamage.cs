using UnityEngine;

public class LaserRaycastDamage : MonoBehaviour
{
    [Header("Raycast Settings")]
    [Tooltip("Width of the beam used for collision detection.")]
    public float thicknessMultiplier = 1.0f;
    public float damageAmount = 50;
    public LayerMask hitMask;

    private readonly RaycastHit[] _hitResults = new RaycastHit[10];

    private void Update()
    {
        if (Time.frameCount % 2 == 0)
        {
            CheckLaserCollision();
        }
    }

    private void CheckLaserCollision()
    {
        float currentThickness = (transform.lossyScale.x * 0.5f) * thicknessMultiplier;
        float laserLength = transform.lossyScale.y;

        Vector3 direction = transform.up;
        Vector3 origin = transform.position - (direction * (laserLength / 2f));

        int hitCount = Physics.SphereCastNonAlloc(origin, currentThickness, direction, _hitResults, laserLength, hitMask);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _hitResults[i];
            if (hit.collider.gameObject != gameObject)
            {
                DealDamage(hit.collider.gameObject, hit.point);
            }
        }
    }

    private void DealDamage(GameObject target, Vector3 hitPoint)
    {
        if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.InGame)
            return;

        EntityHealth health = target.GetComponentInParent<EntityHealth>();
        if (health != null)
        {
            health.ApplyDamage(damageAmount * Time.deltaTime * 10f);
        }

        DestructibleVoxelChunk chunk = target.GetComponentInParent<DestructibleVoxelChunk>();
        if (chunk != null)
        {
            float carveRadius = (transform.lossyScale.x * 0.5f) * thicknessMultiplier * 1.2f;
            chunk.Carve(hitPoint, carveRadius);
        }
    }

    private void OnDrawGizmos()
    {
        float currentThickness = (transform.lossyScale.x * 0.5f) * thicknessMultiplier;
        float laserLength = transform.lossyScale.y;

        Gizmos.color = Color.red;
        Vector3 bottom = transform.position - (transform.up * (laserLength / 2f));
        Vector3 top = transform.position + (transform.up * (laserLength / 2f));

        Gizmos.DrawWireSphere(bottom, currentThickness);
        Gizmos.DrawWireSphere(top, currentThickness);
        Gizmos.DrawLine(bottom, top);
    }
}

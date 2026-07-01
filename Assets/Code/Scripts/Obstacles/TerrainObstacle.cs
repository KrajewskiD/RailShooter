using UnityEngine;

public class TerrainObstacle : MonoBehaviour
{
    [Header("Terrain Damage")]
    [Tooltip("Damage applied when something hits this wall.")]
    public bool isFatal = false;
    public float damageAmount = 50f;

    private void Start()
    {
        Collider[] childColliders = GetComponentsInChildren<Collider>();
        foreach (Collider col in childColliders)
        {
            CollisionReporter reporter = col.gameObject.GetComponent<CollisionReporter>();
            if (reporter == null)
            {
                reporter = col.gameObject.AddComponent<CollisionReporter>();
            }
            reporter.parentObstacle = this;
        }
    }

    public void RefreshColliders()
    {
        Collider[] childColliders = GetComponentsInChildren<Collider>();
        foreach (Collider col in childColliders)
        {
            CollisionReporter reporter = col.gameObject.GetComponent<CollisionReporter>();
            if (reporter == null)
                reporter = col.gameObject.AddComponent<CollisionReporter>();

            reporter.parentObstacle = this;
        }
    }

    private void OnCollisionEnter(Collision collision) => DealDamage(collision.gameObject);

    private void DealDamage(GameObject target)
    {
        if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.InGame)
            return;

        EntityHealth entityHealth = target.GetComponent<EntityHealth>()
                                  ?? target.GetComponentInParent<EntityHealth>();

        if (entityHealth == null) return;

        entityHealth.ApplyDamage(damageAmount);
    }

    public class CollisionReporter : MonoBehaviour
    {
        [HideInInspector] public TerrainObstacle parentObstacle;

        private void OnCollisionEnter(Collision collision)
        {
            if (parentObstacle != null)
            {
                parentObstacle.DealDamage(collision.gameObject);
            }
        }
    }
}

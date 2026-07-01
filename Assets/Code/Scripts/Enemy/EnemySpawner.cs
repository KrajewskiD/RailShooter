using System.Collections.Generic;
using UnityEngine;

public class EnemySpawner : MonoBehaviour
{
    [Header("Prefabs")]
    [Tooltip("Pool of enemy prefabs to randomize from.")]
    [SerializeField] private List<GameObject> enemyPrefabs;

    [Header("Target")]
    [Tooltip("Center of spawn ring. Falls back to FindGameObjectWithTag(\"Player\") if empty.")]
    [SerializeField] private Transform target;

    [Header("Pacing")]
    [Tooltip("Seconds between spawns.")]
    [Min(0.1f)] [SerializeField] private float spawnInterval = 3f;

    [Tooltip("Max alive enemies at once.")]
    [Min(1)] [SerializeField] private int maxAlive = 10;

    [Tooltip("Initial delay before first spawn.")]
    [SerializeField] private float initialDelay = 2f;

    [Header("Spawn Ring Around Target")]
    [Tooltip("Min radius (m).")]
    [SerializeField] private float minRange = 30f;

    [Tooltip("Max radius (m).")]
    [SerializeField] private float maxRange = 60f;

    [Tooltip("Lower height offset relative to target.")]
    [SerializeField] private float minHeightOffset = -10f;

    [Tooltip("Upper height offset relative to target.")]
    [SerializeField] private float maxHeightOffset = 30f;

    [Header("Player Weapon Range")]
    [SerializeField] private bool clampToPlayerWeaponRange = true;
    [SerializeField] private bool includeSpecialWeaponRange = false;
    [Min(0f)] [SerializeField] private float weaponRangePadding = 5f;
    [Min(1f)] [SerializeField] private float fallbackPlayerWeaponRange = PlayerWeaponRangeResolver.DefaultRange;

    private float _spawnTimer;
    private readonly List<GameObject> _alive = new List<GameObject>();

    private void Start()
    {
        target = PlayerReferenceResolver.ResolvePlayerTransform(target);
        _spawnTimer = initialDelay;
    }

    private void Update()
    {
        if (GameStateManager.Instance != null &&
            GameStateManager.Instance.CurrentState != GameState.InGame) return;

        if (target == null || enemyPrefabs == null || enemyPrefabs.Count == 0) return;

        _alive.RemoveAll(e => e == null);

        if (_alive.Count >= maxAlive) return;

        _spawnTimer -= Time.deltaTime;
        if (_spawnTimer <= 0f)
        {
            Spawn();
            _spawnTimer = spawnInterval;
        }
    }

    private void Spawn()
    {
        var prefab = enemyPrefabs[Random.Range(0, enemyPrefabs.Count)];
        if (prefab == null) return;

        if (!TryGetRandomOffset(out Vector3 offset)) return;

        Vector3 pos = target.position + offset;
        Vector3 lookDirection = target.position - pos;
        if (lookDirection.sqrMagnitude < 0.0001f) lookDirection = -target.forward;
        Quaternion rot = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);

        var go = Instantiate(prefab, pos, rot);
        _alive.Add(go);
    }

    private bool TryGetRandomOffset(out Vector3 offset)
    {
        offset = Vector3.zero;

        float spawnMin = Mathf.Max(0f, minRange);
        float spawnMax = Mathf.Max(spawnMin, maxRange);
        float allowedDistance = spawnMax;

        if (clampToPlayerWeaponRange)
        {
            float weaponRange = PlayerWeaponRangeResolver.Resolve(target, includeSpecialWeaponRange, fallbackPlayerWeaponRange);
            allowedDistance = Mathf.Max(0f, weaponRange - weaponRangePadding);
            spawnMax = Mathf.Min(spawnMax, allowedDistance);
            if (spawnMax <= 0.01f) return false;
            spawnMin = Mathf.Min(spawnMin, spawnMax);
        }

        float allowedSqr = allowedDistance * allowedDistance;
        const int MaxAttempts = 8;

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            Vector2 direction = Random.insideUnitCircle;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.right;
            direction.Normalize();

            Vector2 ring = direction * Random.Range(spawnMin, spawnMax);
            float y = Random.Range(minHeightOffset, maxHeightOffset);
            Vector3 candidate = new Vector3(ring.x, y, ring.y);

            if (!clampToPlayerWeaponRange || candidate.sqrMagnitude <= allowedSqr)
            {
                offset = candidate;
                return true;
            }
        }

        Vector2 fallbackDirection = Random.insideUnitCircle;
        if (fallbackDirection.sqrMagnitude < 0.0001f) fallbackDirection = Vector2.right;
        fallbackDirection.Normalize();

        float fallbackY = Mathf.Clamp(Random.Range(minHeightOffset, maxHeightOffset), -allowedDistance, allowedDistance);
        float horizontalMax = Mathf.Sqrt(Mathf.Max(0f, allowedSqr - fallbackY * fallbackY));
        float horizontalDistance = Mathf.Clamp(Random.Range(spawnMin, spawnMax), 0f, horizontalMax);
        Vector2 fallbackRing = fallbackDirection * horizontalDistance;

        offset = new Vector3(fallbackRing.x, fallbackY, fallbackRing.y);
        return offset.sqrMagnitude <= allowedSqr + 0.001f;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 center = target != null ? target.position : transform.position;
        Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.4f);
        Gizmos.DrawWireSphere(center, minRange);
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(center, maxRange);
        if (clampToPlayerWeaponRange)
        {
            Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.45f);
            float weaponRange = PlayerWeaponRangeResolver.Resolve(target, includeSpecialWeaponRange, fallbackPlayerWeaponRange);
            Gizmos.DrawWireSphere(center, Mathf.Max(0f, weaponRange - weaponRangePadding));
        }
    }
#endif
}

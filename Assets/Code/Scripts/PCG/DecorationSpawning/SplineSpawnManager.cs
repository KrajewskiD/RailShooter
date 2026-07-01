using System.Collections.Generic;
using UnityEngine;

public class SplineSpawnManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SplineGenerator targetGenerator;

    [Header("Entries")]
    [Tooltip("Każdy entry to (prefab pool + condition + placement) przetwarzany per spawned segment.")]
    [SerializeField] private List<SplineSpawnData> entries = new List<SplineSpawnData>();

    [Header("Enemy Limits")]
    [Tooltip("Maximum active EnemyAI instances spawned by spline entries. 0 means unlimited.")]
    [Min(0)] [SerializeField] private int maxActiveEnemies = 8;

    [Header("Enemy Weapon Range")]
    [SerializeField] private bool spawnEnemiesOnlyInsidePlayerWeaponRange = true;
    [SerializeField] private bool includeSpecialWeaponRange = false;
    [Min(0f)] [SerializeField] private float enemyWeaponRangePadding = 5f;
    [Min(0f)] [SerializeField] private float enemyMinForwardDistance = 5f;
    [Min(1f)] [SerializeField] private float fallbackPlayerWeaponRange = PlayerWeaponRangeResolver.DefaultRange;

    private static readonly List<Renderer> _tempRenderers = new List<Renderer>(16);
    private Transform _segmentsRoot;
    private Transform _playerTransform;

    private void Update()
    {

        if (targetGenerator == null || targetGenerator.player == null) return;


        if (_segmentsRoot == null)
        {
            _segmentsRoot = targetGenerator.transform.Find("SplineSegments");
        }

        if (_segmentsRoot != null && targetGenerator.TryGetPlayerArc(out float playerArc))
        {

            float destroyDistance = targetGenerator.extendTrigger;


            for (int i = _segmentsRoot.childCount - 1; i >= 0; i--)
            {
                Transform segmentTx = _segmentsRoot.GetChild(i);
                if (!segmentTx.TryGetComponent(out SplineSegment segment)) continue;

                if (playerArc - segment.EndArc > destroyDistance)
                {


                    Destroy(segmentTx.gameObject);
                }
            }
        }
    }

    private void OnEnable()
    {
        if (targetGenerator != null)
            targetGenerator.OnSegmentSpawned += HandleSegmentSpawned;
    }

    private void OnDisable()
    {
        if (targetGenerator != null)
            targetGenerator.OnSegmentSpawned -= HandleSegmentSpawned;
    }

    private void HandleSegmentSpawned(SplineSegment segment, int index, int seed)
    {
        if (segment == null) return;
        if (entries == null || entries.Count == 0) return;
        SplineSpawnContext ctx = BuildContext(segment, index, seed);
        StartCoroutine(ProcessEntriesOverTime(ctx));
    }

    private System.Collections.IEnumerator ProcessEntriesOverTime(SplineSpawnContext ctx)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (ctx.Segment == null) yield break;

            var entry = entries[i];
            if (entry == null) continue;

            bool enemyEntry = spawnEnemiesOnlyInsidePlayerWeaponRange && EntryContainsEnemyPrefab(entry);
            if (enemyEntry)
            {
                StartCoroutine(ProcessEntryRoutine(entry, CloneContextForEntry(ctx, i)));
                continue;
            }

            yield return StartCoroutine(ProcessEntryRoutine(entry, ctx));
        }
    }

    private SplineSpawnContext BuildContext(SplineSegment segment, int index, int seed)
    {
        return new SplineSpawnContext
        {
            Segment        = segment,
            Generator      = targetGenerator,
            SegmentIndex   = index,
            Seed           = seed,
            Rng            = new System.Random(seed + index * 7919),
            SegmentLength  = segment.SegmentArcLength,
            SegmentStartArc = segment.StartArc,
            SegmentEndArc   = segment.EndArc
        };
    }

    private System.Collections.IEnumerator ProcessEntryRoutine(SplineSpawnData entry, SplineSpawnContext ctx)
    {
        if (ctx.Segment == null) yield break;
        if (entry.placement == null) yield break;
        if (entry.prefabPool == null || entry.prefabPool.Length == 0) yield break;
        if (entry.condition != null && !entry.condition.Check(ctx)) yield break;

        Transform container = GetOrCreateContainer(ctx.Segment, entry.containerName);
        if (container == null) yield break;

        for (int i = 0; i < entry.countPerSegment; i++)
        {
            if (ctx.Segment == null || container == null) yield break;

            ctx.CurrentSpawnIndex = i;
            ctx.TotalSpawnCount = entry.countPerSegment;

            if (!entry.placement.TryGetPlacement(ctx, out Vector3 pos, out Quaternion rot)) continue;

            int idx = ctx.Rng.Next(entry.prefabPool.Length);
            SpawnablePrefab spawnData = entry.prefabPool[idx];
            if (spawnData == null || spawnData.prefab == null) continue;

            bool isEnemy = IsEnemyPrefab(spawnData.prefab);
            if (isEnemy && spawnEnemiesOnlyInsidePlayerWeaponRange)
            {
                bool expired = false;
                while (ctx.Segment != null &&
                       container != null &&
                       !IsEnemySpawnWindowOpen(ctx, pos, out expired))
                {
                    if (expired) break;
                    yield return null;
                }

                if (ctx.Segment == null || container == null || expired) continue;
            }
            else if (isEnemy && !EnemyAI.CanSpawnMore(maxActiveEnemies))
            {
                continue;
            }

            GameObject spawnObj = Instantiate(spawnData.prefab, pos, rot, container);

            spawnObj.transform.localScale = spawnData.scale.sqrMagnitude > 0.0001f ? spawnData.scale : Vector3.one;

            ConfigureSpawnedSplineEnemy(spawnObj, ctx);

            if (spawnData.settings != null)
                spawnData.settings.ApplySetup(spawnObj);

            if (i % 5 == 0) yield return null;
        }
    }

    private SplineSpawnContext CloneContextForEntry(SplineSpawnContext source, int entryIndex)
    {
        return new SplineSpawnContext
        {
            Segment = source.Segment,
            Generator = source.Generator,
            SegmentIndex = source.SegmentIndex,
            Seed = source.Seed,
            Rng = new System.Random(source.Seed + source.SegmentIndex * 7919 + entryIndex * 104729),
            SegmentLength = source.SegmentLength,
            SegmentStartArc = source.SegmentStartArc,
            SegmentEndArc = source.SegmentEndArc
        };
    }

    private bool IsEnemySpawnWindowOpen(SplineSpawnContext ctx, Vector3 worldPos, out bool expired)
    {
        expired = false;

        Transform player = ResolvePlayerTransform();
        float weaponRange = PlayerWeaponRangeResolver.Resolve(player, includeSpecialWeaponRange, fallbackPlayerWeaponRange);
        float allowedRange = Mathf.Max(0f, weaponRange - enemyWeaponRangePadding);
        if (allowedRange <= 0.01f) return false;

        if (targetGenerator != null && targetGenerator.TryGetPlayerArc(out float playerArc))
        {
            float forwardArcDistance = ctx.CurrentPlacementArc - playerArc;
            if (forwardArcDistance < -enemyMinForwardDistance)
            {
                expired = true;
                return false;
            }

            if (forwardArcDistance < enemyMinForwardDistance || forwardArcDistance > allowedRange)
                return false;
        }

        if (player != null)
        {
            float allowedSqr = allowedRange * allowedRange;
            if ((worldPos - player.position).sqrMagnitude > allowedSqr)
                return false;
        }

        return EnemyAI.CanSpawnMore(maxActiveEnemies);
    }

    private Transform ResolvePlayerTransform()
    {
        if (_playerTransform != null) return _playerTransform;

        if (targetGenerator != null && targetGenerator.player != null)
        {
            _playerTransform = targetGenerator.player;
            return _playerTransform;
        }

        _playerTransform = PlayerReferenceResolver.ResolvePlayerTransform();
        return _playerTransform;
    }

    private static bool EntryContainsEnemyPrefab(SplineSpawnData entry)
    {
        if (entry == null || entry.prefabPool == null) return false;

        for (int i = 0; i < entry.prefabPool.Length; i++)
        {
            SpawnablePrefab spawnData = entry.prefabPool[i];
            if (spawnData != null && IsEnemyPrefab(spawnData.prefab))
                return true;
        }

        return false;
    }

    private static bool IsEnemyPrefab(GameObject prefab)
    {
        return prefab != null && prefab.GetComponentInChildren<EnemyAI>(true) != null;
    }

    private static void ConfigureSpawnedSplineEnemy(GameObject spawnObj, SplineSpawnContext ctx)
    {
        if (spawnObj == null || ctx == null || ctx.Generator == null) return;

        EnemyAI[] enemies = spawnObj.GetComponentsInChildren<EnemyAI>(true);
        for (int i = 0; i < enemies.Length; i++)
        {
            if (enemies[i] != null)
                enemies[i].ConfigureSpline(ctx.Generator);
        }
    }

    private Transform GetOrCreateContainer(SplineSegment segment, string name)
    {
        if (segment == null) return null;
        return SpawnContainerUtility.GetOrCreateClearedContainer(segment.transform, name);
    }
}

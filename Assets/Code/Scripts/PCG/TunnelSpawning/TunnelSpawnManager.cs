using System.Collections.Generic;
using UnityEngine;

public class TunnelSpawnManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private TunnelGenerator targetGenerator;

    [Header("Entries")]
    [Tooltip("Each entry is (prefab pool + condition + placement) processed per spawned segment.")]
    [SerializeField] private List<TunnelSpawnData> entries = new List<TunnelSpawnData>();
    [SerializeField] private bool spawningEnabled = true;
    private static readonly List<Renderer> _tempRenderers = new List<Renderer>(16);

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

    private void HandleSegmentSpawned(TunnelSegment segment, int index, int seed)
    {
        if (!spawningEnabled) return;
        if (entries == null || entries.Count == 0) return;
        SpawnContext ctx = BuildContext(segment, index, seed);

        StartCoroutine(ProcessEntriesOverTime(ctx));
    }

    private System.Collections.IEnumerator ProcessEntriesOverTime(SpawnContext ctx)
    {
        for (int i = 0; i < entries.Count; i++)
        {
            if (!spawningEnabled) yield break;

            var entry = entries[i];
            if (entry == null) continue;
            
            yield return StartCoroutine(ProcessEntryRoutine(entry, ctx));
        }
    }

    private SpawnContext BuildContext(TunnelSegment segment, int index, int seed)
    {
        float segLen = targetGenerator != null ? targetGenerator.segmentLength : 0f;
        return new SpawnContext
        {
            Segment       = segment,
            Generator     = targetGenerator,
            SegmentIndex  = index,
            Seed          = seed,
            Rng           = new System.Random(seed + index * 7919),
            SegmentLength = segLen,
            SegmentStartZ = index * segLen,
            SegmentEndZ   = (index + 1) * segLen
        };
    }

    private System.Collections.IEnumerator ProcessEntryRoutine(TunnelSpawnData entry, SpawnContext ctx)
    {
        if (entry.placement == null)
        {
            yield break;
        }

        if (entry.prefabPool == null || entry.prefabPool.Length == 0)
        {
            yield break;
        }

        if (entry.condition != null && !entry.condition.Check(ctx))
        {
            yield break;
        }

        Transform container = GetOrCreateContainer(ctx.Segment, entry.containerName);

        for (int i = 0; i < entry.countPerSegment; i++)
        {
            if (!spawningEnabled) yield break;

            ctx.CurrentSpawnIndex = i;
            ctx.TotalSpawnCount = entry.countPerSegment;

            if (!entry.placement.TryGetPlacement(ctx, out Vector3 pos, out Quaternion rot))
            {
                continue;
            }

            int idx = ctx.Rng.Next(entry.prefabPool.Length);
            SpawnablePrefab spawnData = entry.prefabPool[idx];
            if (spawnData == null)
            {
                continue;
            }

            GameObject spawnObj = Instantiate(spawnData.prefab, pos, rot, container);
            spawnObj.transform.localScale = spawnData.scale;

            if (spawnData.settings != null)
            {
                spawnData.settings.ApplySetup(spawnObj);
            }


            if (ctx.AvailableRadius > 0)
            {
                _tempRenderers.Clear();
                spawnObj.GetComponentsInChildren<Renderer>(true, _tempRenderers);
                if (_tempRenderers.Count > 0)
                {
                    Bounds bounds = _tempRenderers[0].bounds;
                    for (int r = 1; r < _tempRenderers.Count; r++)
                    {
                        bounds.Encapsulate(_tempRenderers[r].bounds);
                    }

                    float objectRadius = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
                    float maxAllowedRadius = ctx.AvailableRadius * 0.9f;

                    if (objectRadius > maxAllowedRadius)
                    {
                        float shrinkFactor = maxAllowedRadius / objectRadius;
                        spawnObj.transform.localScale *= shrinkFactor;                        
                    }
                }
            }
            if (i % 5 == 0) 
            {
                yield return null; 
            }
        }

    }

    private Transform GetOrCreateContainer(TunnelSegment segment, string name)
    {
        return SpawnContainerUtility.GetOrCreateClearedContainer(segment.transform, name);
    }

    public void SetSpawningEnabled(bool enabled, bool clearExistingSpawnedObjects)
    {
        spawningEnabled = enabled;
        if (!spawningEnabled) StopAllCoroutines();
        if (clearExistingSpawnedObjects) ClearSpawnedObjects();
    }

    public void ClearSpawnedObjects()
    {
        if (targetGenerator == null || entries == null) return;

        TunnelSegment[] segments = targetGenerator.GetComponentsInChildren<TunnelSegment>(true);
        foreach (TunnelSegment segment in segments)
        {
            if (segment == null) continue;

            for (int i = 0; i < entries.Count; i++)
            {
                TunnelSpawnData entry = entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.containerName)) continue;

                Transform container = segment.transform.Find(entry.containerName);
                if (container == null) continue;

                SpawnContainerUtility.ClearChildren(container);
            }
        }
    }
}

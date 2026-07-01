using UnityEngine;
using UnityEngine.Pool;
using System.Collections.Generic;

public static class HitEffectPool
{
    private static readonly Dictionary<GameObject, IObjectPool<PooledEffect>> _pools = new Dictionary<GameObject, IObjectPool<PooledEffect>>();

    public static void Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return;

        if (!_pools.TryGetValue(prefab, out var pool))
        {
            pool = new ObjectPool<PooledEffect>(
                createFunc: () => {
                    GameObject go = Object.Instantiate(prefab);
                    PooledEffect effect = go.GetComponent<PooledEffect>();
                    if (effect == null) effect = go.AddComponent<PooledEffect>();
                    
                    effect.Initialize(pool); 
                    return effect;
                },
                actionOnGet: e => e.gameObject.SetActive(true),
                actionOnRelease: e => e.gameObject.SetActive(false),
                actionOnDestroy: e => {
                    if (e is Component comp && comp != null)
                    {
                        Object.Destroy(comp.gameObject);
                    }
                },
                collectionCheck: false,
                defaultCapacity: 50,
                maxSize: 300
            );
            _pools[prefab] = pool;
        }

        PooledEffect instance = pool.Get();
        instance.transform.SetPositionAndRotation(position, rotation);
        instance.Play();
    }
}

public class PooledEffect : MonoBehaviour
{
    private IObjectPool<PooledEffect> _pool;
    private ParticleSystem[] _particles;
    private float _timer;
    private float _maxLife = 2f;
    private bool _isPlaying;

    public void Initialize(IObjectPool<PooledEffect> pool)
    {
        _pool = pool;
        
        _particles = GetComponentsInChildren<ParticleSystem>(true);
        
        float maxDuration = 1f;
        foreach (var ps in _particles)
        {
            float duration = ps.main.duration + ps.main.startLifetime.constantMax;
            if (duration > maxDuration) maxDuration = duration;
        }
        _maxLife = maxDuration;
    }

    public void Play()
    {
        _timer = 0f;
        _isPlaying = true;
        
        foreach (var ps in _particles)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Clear(true);
            ps.Play(true);
        }
    }

    private void Update()
    {
        if (!_isPlaying) return;

        _timer += Time.deltaTime;
        if (_timer >= _maxLife)
        {
            _isPlaying = false;
            if (_pool != null) _pool.Release(this);
            else Destroy(gameObject); 
        }
    }
}

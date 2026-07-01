using UnityEngine;
using UnityEngine.Pool;

public static class OneShotAudioPool
{
    private static ObjectPool<PooledOneShotAudioSource> _pool;

    public static void Play(AudioClip clip, Vector3 position, float spatialBlend = 0.5f, float volume = 1f)
    {
        if (clip == null) return;

        EnsurePool();
        PooledOneShotAudioSource source = _pool.Get();
        source.Play(clip, position, spatialBlend, volume);
    }

    private static void EnsurePool()
    {
        if (_pool != null) return;

        _pool = new ObjectPool<PooledOneShotAudioSource>(
            createFunc: CreateSource,
            actionOnGet: source => source.gameObject.SetActive(true),
            actionOnRelease: source => source.gameObject.SetActive(false),
            actionOnDestroy: source =>
            {
                if (source != null)
                {
                    Object.Destroy(source.gameObject);
                }
            },
            collectionCheck: false,
            defaultCapacity: 8,
            maxSize: 64);
    }

    private static PooledOneShotAudioSource CreateSource()
    {
        var go = new GameObject("[PooledOneShotAudio]");
        Object.DontDestroyOnLoad(go);

        var audioSource = go.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        var pooledSource = go.AddComponent<PooledOneShotAudioSource>();
        pooledSource.Initialize(_pool, audioSource);

        go.SetActive(false);
        return pooledSource;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _pool = null;
    }
}

public sealed class PooledOneShotAudioSource : MonoBehaviour
{
    private IObjectPool<PooledOneShotAudioSource> _pool;
    private AudioSource _source;
    private float _releaseAt;
    private bool _playing;

    public void Initialize(IObjectPool<PooledOneShotAudioSource> pool, AudioSource source)
    {
        _pool = pool;
        _source = source;
    }

    public void Play(AudioClip clip, Vector3 position, float spatialBlend, float volume)
    {
        if (_source == null) _source = GetComponent<AudioSource>();
        if (_source == null) return;

        transform.position = position;
        _source.clip = clip;
        _source.volume = volume;
        _source.spatialBlend = spatialBlend;
        _source.Play();

        _releaseAt = Time.unscaledTime + clip.length + 0.05f;
        _playing = true;
    }

    private void Update()
    {
        if (!_playing) return;
        if (Time.unscaledTime < _releaseAt && _source != null && _source.isPlaying) return;

        _playing = false;
        if (_source != null)
        {
            _source.Stop();
            _source.clip = null;
        }

        if (_pool != null)
        {
            _pool.Release(this);
        }
    }
}

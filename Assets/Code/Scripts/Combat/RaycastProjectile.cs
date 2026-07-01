using UnityEngine;
using UnityEngine.Pool;

public class RaycastProjectile : MonoBehaviour
{
    private const float HitEffectSurfaceOffset = 0.08f;

    private WeaponData _weaponData;
    private float _damage;
    private bool _isReleased;
    private float _lifeTimer;
    private float _maxLifeTime;
    private string _ownerTag = "Untagged";
    private TrailRenderer[] _trails;
    private Vector3 _currentVelocity;

    private Transform _thisTransform;

    public LayerMask hitMask;
    private IObjectPool<RaycastProjectile> _pool;
    public void SetPool(IObjectPool<RaycastProjectile> pool) => _pool = pool;

    private void Awake()
    {
        _thisTransform = base.transform;
        CacheTrails();
    }

    private void OnDisable()
    {
        _isReleased = true;
        SetTrailsEmitting(false, clear: true);
    }

    public void Initialize(in ProjectileInitData data)
    {
        _weaponData = data.Weapon;
        _damage = data.Damage;
        _isReleased = false;
        _lifeTimer = 0f;
        _ownerTag = data.Owner != null ? data.Owner.tag : "Untagged";

        float safeVel = Mathf.Max(0.1f, data.TotalVelocity);
        _maxLifeTime = _weaponData.maxDistance / safeVel;

        SetTrailsEmitting(true, clear: true);

        _currentVelocity = data.Direction * safeVel;
    }

    private void Update()
    {
        if (_isReleased) return;

        float dt = Time.deltaTime;
        _lifeTimer += dt;

        if (_lifeTimer >= _maxLifeTime)
        {
            Release();
            return;
        }

        Vector3 moveStep = _currentVelocity * dt;
        float distance = moveStep.magnitude;

        Vector3 direction = distance > 0.0001f ? moveStep / distance : _currentVelocity.normalized;

        if (Physics.Raycast(_thisTransform.position, direction, out RaycastHit hit, distance, hitMask))
        {
            if (!hit.transform.root.CompareTag(_ownerTag))
            {
                HandleImpact(hit.collider.gameObject, hit);
                return;
            }
        }

        _thisTransform.position += moveStep;
    }

    private void HandleImpact(GameObject hitTarget, RaycastHit hit)
    {
        if (hitTarget.TryGetComponent(out IDamageable damageable) ||
            (damageable = hitTarget.GetComponentInParent<IDamageable>()) != null)
        {
            damageable.ApplyDamage(_damage);
        }

        if (hitTarget.TryGetComponent(out DestructibleVoxelChunk chunk) ||
            (chunk = hitTarget.GetComponentInParent<DestructibleVoxelChunk>()) != null)
        {
            if (chunk.Config != null)
                chunk.Carve(hit.point, _damage * chunk.Config.carveScale);
        }

        if (_weaponData.hitEffect != null)
        {
            Vector3 effectPosition = hit.point + hit.normal * HitEffectSurfaceOffset;
            HitEffectPool.Spawn(_weaponData.hitEffect, effectPosition, Quaternion.LookRotation(hit.normal));
        }

        Release();
    }

    private void Release()
    {
        if (_isReleased && _pool == null) return; 
        _isReleased = true;
        SetTrailsEmitting(false, clear: true);

        if (_pool != null) _pool.Release(this);
        else Destroy(gameObject);
    }

    private void CacheTrails()
    {
        _trails = GetComponentsInChildren<TrailRenderer>(true);
    }

    private void SetTrailsEmitting(bool emitting, bool clear)
    {
        if (_trails == null || _trails.Length == 0)
        {
            CacheTrails();
        }

        for (int i = 0; i < _trails.Length; i++)
        {
            TrailRenderer trail = _trails[i];
            if (trail == null) continue;

            trail.emitting = emitting;
            if (clear) trail.Clear();
        }
    }
}

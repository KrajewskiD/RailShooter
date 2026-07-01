using UnityEngine;
using UnityEngine.Pool;
using System.Collections.Generic;

public class GunEngine : MonoBehaviour
{
    [Header("Weapon Role")]
    public StatCategory weaponCategory = StatCategory.PrimaryWeapon;
    private float[] _v;

    public bool IsLimitedAmmo => _weapon != null && _weapon.isLimitedAmmo;

    [Header("Visuals & Fire Points")]
    [SerializeField] private GameObject muzzleFlash;
    [SerializeField] private Transform[] firePoints;

    [Header("Pool Settings")]
    [SerializeField] private int poolDefaultCapacity = 32;
    [SerializeField] private int poolMaxSize = 100;

    private readonly Dictionary<WeaponData, IObjectPool<RaycastProjectile>> _pools = new Dictionary<WeaponData, IObjectPool<RaycastProjectile>>();
    private StatBinder[] _binders;
    private IObjectPool<GameObject> _muzzleFlashPool;
    private IStatProvider _stats;
    private FirePointProvider _firePointProvider;
    private WeaponData _weapon;
    private Rigidbody _rootRb;
    
    private Transform _thisTransform;
    private Transform _rootTransform;
    private GameObject _rootGo;

    private int _bulletsLeft;
    private float _cooldownTimer;
    private float _reloadTimer;
    private bool _isReloading;
    private int _currentPointIndex;
    private bool _initialized;
    public int BulletsLeft => _bulletsLeft;
    public int MaxMagazine => _initialized ? (int)_v[(int)WeaponStatType.MagazineSize] : 0;
    public bool IsReloading => _isReloading;
    public bool AllowButtonHold => _weapon != null && _weapon.allowButtonHold;
    public bool HasWeapon => _weapon != null;
    public WeaponData CurrentWeapon => _weapon;
    public float ReloadProgress => _isReloading && _reloadDuration > 0f
        ? 1f - Mathf.Clamp01(_reloadTimer / _reloadDuration)
        : 1f;
    private float _reloadDuration;

    private void Awake()
    {
        _thisTransform = transform;
        _rootTransform = transform.root;
        _rootGo = _rootTransform.gameObject;

        System.Array enumValues = System.Enum.GetValues(typeof(WeaponStatType));

        int maxVal = 0;
        for (int i = 0; i < enumValues.Length; i++)
        {
            int val = (int)enumValues.GetValue(i);
            if (val > maxVal) maxVal = val;
        }
        _v = new float[maxVal + 1];

        _binders = new StatBinder[maxVal + 1];
        for (int i = 0; i < enumValues.Length; i++)
        {
            int val = (int)enumValues.GetValue(i);
            _binders[val] = new StatBinder(this, (WeaponStatType)val);
        }

        if (muzzleFlash != null)
        {
            _muzzleFlashPool = new ObjectPool<GameObject>(
                createFunc: () => Instantiate(muzzleFlash),
                actionOnGet: obj => obj.SetActive(true),
                actionOnRelease: obj => obj.SetActive(false),
                actionOnDestroy: Destroy,
                defaultCapacity: 10,
                maxSize: 30
            );
        }
    }

    public void Start()
    {
        TryInitializeFromStats();
    }

    private void TryInitializeFromStats()
    {
        if (_initialized) return;

        _stats = GetComponentInParent<IStatProvider>();

        if (_stats is PlayerStatsManager playerStats)
        {
            if (weaponCategory == StatCategory.PrimaryWeapon)
            {
                if (playerStats.CurrentWeapon == null) return;

                playerStats.OnWeaponChanged -= HandleWeaponChanged;
                playerStats.OnWeaponChanged += HandleWeaponChanged;
                Setup(playerStats, playerStats.CurrentWeapon);
            }
            else
            {
                if (playerStats.CurrentSpecialWeapon == null) return;

                playerStats.OnSpecialWeaponChanged -= HandleWeaponChanged;
                playerStats.OnSpecialWeaponChanged += HandleWeaponChanged;
                Setup(playerStats, playerStats.CurrentSpecialWeapon);
            }
        }
        else if (_stats is EnemyStatProvider enemyStats)
        {
            Setup(enemyStats, enemyStats.GetData().defaultWeapon);
        }
    }

    public void Setup(IStatProvider provider, WeaponData weapon)
    {
        if (_initialized) CleanupBindings();

        _stats = provider;
        _weapon = weapon;
        _rootRb = _rootTransform.GetComponent<Rigidbody>();

        if (_firePointProvider == null)
            _firePointProvider = GetComponentInParent<FirePointProvider>();

        Bind(WeaponStatType.FireRate);
        Bind(WeaponStatType.MagazineSize);
        Bind(WeaponStatType.ReloadSpeed);
        Bind(WeaponStatType.Damage);
        Bind(WeaponStatType.MuzzleVelocity);
        Bind(WeaponStatType.FirePointLevel);

        _bulletsLeft = (int)_v[(int)WeaponStatType.MagazineSize];
        _initialized = true;
    }

    private void Bind(WeaponStatType type)
    {
        Stat s = _stats.GetWeaponStat(type, weaponCategory);
        if (s == null) return;

        _binders[(int)type].BindTo(s);
    }

    void Update()
    {
        if (!_initialized)
        {
            TryInitializeFromStats();
        }

        if (!_initialized) return;
        if (_cooldownTimer <= 0f && !_isReloading) return;

        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;

        if (_isReloading)
        {
            _reloadTimer -= Time.deltaTime;
            if (_reloadTimer <= 0f) FinishReload();
        }
    }

    public void RequestShoot()
    {
        RequestShoot(Vector3.zero);
    }

    public void RequestShoot(Vector3 _)
    {
        if (_weapon == null) return;

        int ammoCost = Mathf.Max(1, _weapon.ammoCostPerShot);

        if (!CanShoot(ammoCost))
        {
            if (_bulletsLeft < ammoCost && !_isReloading && !IsLimitedAmmo) RequestReload();
            return;
        }

        int activeCount = ResolveActiveFirePointCount();
        if (activeCount <= 0)
        {
            return;
        }

        int burst = Mathf.Max(1, _weapon.bulletsPerTap);
        bool allAtOnce = _weapon.firePattern == FirePattern.AllAtOnce;

        if (_currentPointIndex >= activeCount) _currentPointIndex = 0;

        int shotsFired = 0;

        for (int i = 0; i < burst; i++)
        {
            if (allAtOnce)
            {
                for (int j = 0; j < activeCount; j++)
                {
                    Transform p = GetFirePoint(j);
                    if (p != null && FireOne(p)) shotsFired++;
                }
            }
            else
            {
                for (int j = 0; j < activeCount; j++)
                {
                    int idx = (_currentPointIndex + j) % activeCount;
                    Transform p = GetFirePoint(idx);
                    if (p != null && FireOne(p)) shotsFired++;
                }
                _currentPointIndex = (_currentPointIndex + 1) % activeCount;
            }
        }

        if (shotsFired == 0) return;

        _bulletsLeft -= ammoCost;
        _cooldownTimer = 1f / Mathf.Max(0.001f, _v[(int)WeaponStatType.FireRate]);
    }

    private bool FireOne(Transform point)
    {
        if (_weapon == null || _weapon.visualModel == null) return false;

        Vector3 fireDirection = point.forward;
        if (fireDirection.sqrMagnitude < 0.0001f)
        {
            fireDirection = _rootTransform != null ? _rootTransform.forward : Vector3.forward;
        }
        fireDirection.Normalize();

        var pool = GetPool(_weapon);
        RaycastProjectile projectile = pool.Get();

        float muzzleVelocity = _v[(int)WeaponStatType.MuzzleVelocity];

        projectile.transform.SetPositionAndRotation(point.position, Quaternion.LookRotation(fireDirection) * Quaternion.Euler(90f, 0f, 0f));

        ProjectileInitData initData = new ProjectileInitData(
            _weapon,
            fireDirection,
            muzzleVelocity,
            _v[(int)WeaponStatType.Damage],
            _rootGo
        );

        projectile.Initialize(in initData);

        if (_muzzleFlashPool != null)
        {
            GameObject flash = _muzzleFlashPool.Get();
            flash.transform.SetPositionAndRotation(point.position, point.rotation);
        }

        return true;
    }

    private int ResolveActiveFirePointCount()
    {
        if (_firePointProvider != null)
        {
            int providerCount = _firePointProvider.ResolveActiveCount(weaponCategory, _v[(int)WeaponStatType.FirePointLevel]);
            if (providerCount > 0) return providerCount;
        }

        return firePoints != null ? firePoints.Length : 0;
    }

    private Transform GetFirePoint(int index)
    {
        if (_firePointProvider != null)
        {
            Transform providerPoint = _firePointProvider.GetFirePoint(weaponCategory, index);
            if (providerPoint != null) return providerPoint;
        }

        if (firePoints == null || index < 0 || index >= firePoints.Length) return null;
        return firePoints[index];
    }

    private Transform GetReferenceFirePoint()
    {
        if (_firePointProvider != null)
        {
            Transform providerPoint = _firePointProvider.GetReferenceFirePoint(weaponCategory);
            if (providerPoint != null) return providerPoint;
        }

        if (firePoints == null) return null;
        for (int i = 0; i < firePoints.Length; i++)
        {
            if (firePoints[i] != null) return firePoints[i];
        }
        return null;
    }

    private IObjectPool<RaycastProjectile> GetPool(WeaponData data)
    {
        if (_pools.TryGetValue(data, out var existing)) return existing;

        IObjectPool<RaycastProjectile> pool = null;
        pool = new ObjectPool<RaycastProjectile>(
            createFunc: () => {
                GameObject go = Instantiate(data.visualModel);
                RaycastProjectile proj = go.GetComponent<RaycastProjectile>();
                proj.SetPool(pool);
                return proj;
            },
            actionOnGet: p => p.gameObject.SetActive(true),
            actionOnRelease: p => p.gameObject.SetActive(false),
            actionOnDestroy: p => {
                if (p is Component comp && comp != null)
                {
                    Destroy(comp.gameObject);
                }
            },
            collectionCheck: false,
            defaultCapacity: poolDefaultCapacity,
            maxSize: poolMaxSize
        );

        _pools[data] = pool;
        return pool;
    }

    private void HandleWeaponChanged(WeaponData newData)
    {
        if (newData == null) return;
        _weapon = newData;
        if (_initialized) RequestReload();
    }

    public void RequestReload()
    {
        if (_isReloading || !_initialized) return;

        int magSize = (int)_v[(int)WeaponStatType.MagazineSize];
        
        if (_bulletsLeft >= magSize) return;

        _isReloading = true;
        _reloadDuration = _weapon.reloadTime / Mathf.Max(0.001f, _v[(int)WeaponStatType.ReloadSpeed]);
        _reloadTimer = _reloadDuration;
    }

    private void FinishReload()
    {
        _bulletsLeft = (int)_v[(int)WeaponStatType.MagazineSize];
        _isReloading = false;
        _reloadTimer = 0f;
        _reloadDuration = 0f;
    }

    private bool CanShoot(int ammoCost) => _initialized && !_isReloading && _bulletsLeft >= ammoCost && _cooldownTimer <= 0f;

    private void CleanupBindings()
    {
        if (_binders == null) return;
        for (int i = 0; i < _binders.Length; i++)
        {
            if (_binders[i] != null) _binders[i].Unbind();
        }
    }

    private void OnDestroy()
    {
        if (_stats is PlayerStatsManager playerStats)
        {
            if (weaponCategory == StatCategory.PrimaryWeapon)
                playerStats.OnWeaponChanged -= HandleWeaponChanged;
            else
                playerStats.OnSpecialWeaponChanged -= HandleWeaponChanged;
        }
        
        CleanupBindings();
        foreach (var pool in _pools.Values) pool.Clear();
        if (_muzzleFlashPool != null) _muzzleFlashPool.Clear();
    }

    private class StatBinder
    {
        private readonly GunEngine _engine;
        private readonly WeaponStatType _type;
        private readonly int _index;
        private Stat _boundStat;

        public StatBinder(GunEngine engine, WeaponStatType type)
        {
            _engine = engine;
            _type = type;
            _index = (int)type;
        }

        public void BindTo(Stat newStat)
        {
            Unbind(); 

            _boundStat = newStat;
            if (_boundStat != null)
            {
                _boundStat.OnValueChanged += HandleValueChanged;
                HandleValueChanged(); 
            }
        }

        public void Unbind()
        {
            if (_boundStat != null)
            {
                _boundStat.OnValueChanged -= HandleValueChanged;
                _boundStat = null;
            }
        }

        private void HandleValueChanged()
        {
            if (_type == WeaponStatType.MagazineSize)
            {
                int newMag = (int)_boundStat.Value;
                int oldMag = (int)_engine._v[_index];
                int diff = newMag - oldMag;
                if (diff > 0) _engine._bulletsLeft += diff;
                if (_engine._bulletsLeft > newMag) _engine._bulletsLeft = newMag;
            }

            _engine._v[_index] = _boundStat.Value;
        }
    }
}

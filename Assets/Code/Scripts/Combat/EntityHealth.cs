using System;
using UnityEngine;

public class EntityHealth : MonoBehaviour, IDamageable
{
    [Header("Recovery")]
    [Tooltip("How many HP/shield points are restored per second when a pickup heals the player.")]
    [SerializeField] private float recoveryPerSecond = 45f;

    private float _maxHealth;
    private float _currentHealth;
    private bool  _isDead;
    private Stat  _maxHealthStat;
    private Stat  _maxShieldStat;
    private PlayerInvulnerability _invulnerability;
    private float _queuedHealthRestore;
    private float _queuedShieldRestore;

    public event Action<float, float> OnHealthChanged;
    public event Action OnDied;

    public float CurrentHealth => _currentHealth;
    public float MaxHealth => _maxHealth;
    public float HealthRatio => _maxHealth > 0f ? _currentHealth / _maxHealth : 0f;
    public bool  IsDead => _isDead;

    [Header("Shield")]
    [Tooltip("Czy shield startuje pusty (true — gracz musi go zbierać) czy pełny (false — gracz zaczyna z pełną tarczą).")]
    [SerializeField] private bool _startWithEmptyShield = true;

    private float _maxShield;
    private float _currentShield;
    private bool _initialized;

    public float CurrentShield => _currentShield;
    public float MaxShield => _maxShield;
    public float ShieldRatio => _maxShield > 0f ? _currentShield / _maxShield : 0f;
    public bool HasShield => _currentShield > 0f;

    public event Action<float, float> OnShieldChanged;

    public void AddShield(float amount)
    {
        if (_isDead || amount <= 0f) return;
        _queuedShieldRestore += amount;
    }

    private void Start()
    {
        TryInitialize();
    }

    private void OnDestroy()
    {
        if (_maxHealthStat != null)
            _maxHealthStat.OnValueChanged -= HandleMaxHealthChanged;
        if (_maxShieldStat != null)
            _maxShieldStat.OnValueChanged -= HandleMaxShieldChanged;
    }

    private void Update()
    {
        if (!_initialized)
        {
            TryInitialize();
        }

        if (_isDead) return;
        float step = Mathf.Max(0.01f, recoveryPerSecond) * Time.deltaTime;

        if (_queuedHealthRestore > 0f && _currentHealth < _maxHealth)
        {
            float amount = Mathf.Min(step, Mathf.Min(_queuedHealthRestore, _maxHealth - _currentHealth));
            _queuedHealthRestore -= amount;
            _currentHealth += amount;
            OnHealthChanged?.Invoke(_currentHealth, _maxHealth);
        }
        else if (_queuedHealthRestore > 0f && _currentHealth >= _maxHealth)
        {
            _queuedHealthRestore = 0f;
        }

        if (_queuedShieldRestore > 0f && _currentShield < _maxShield)
        {
            float amount = Mathf.Min(step, Mathf.Min(_queuedShieldRestore, _maxShield - _currentShield));
            _queuedShieldRestore -= amount;
            _currentShield += amount;
            OnShieldChanged?.Invoke(_currentShield, _maxShield);
        }
        else if (_queuedShieldRestore > 0f && _currentShield >= _maxShield)
        {
            _queuedShieldRestore = 0f;
        }
    }

    private void TryInitialize()
    {
        if (_initialized) return;

        IStatProvider provider = GetComponentInParent<IStatProvider>();
        if (_invulnerability == null)
        {
            _invulnerability = GetComponentInParent<PlayerInvulnerability>();
        }

        if (provider == null)
        {
            return;
        }

        _maxHealthStat = provider.GetHullStat(HullStatType.MaxHealth);
        if (_maxHealthStat == null)
        {
            return;
        }

        _maxHealthStat.OnValueChanged += HandleMaxHealthChanged;
        Initialize(_maxHealthStat.Value);

        _maxShieldStat = provider.GetHullStat(HullStatType.MaxShield);
        if (_maxShieldStat != null)
        {
            _maxShield = _maxShieldStat.Value;
            _maxShieldStat.OnValueChanged += HandleMaxShieldChanged;
        }

        _currentShield = _startWithEmptyShield ? 0f : _maxShield;
        OnShieldChanged?.Invoke(_currentShield, _maxShield);
        _initialized = true;
    }

    private void HandleMaxShieldChanged()
    {
        _maxShield = Mathf.Max(0f, _maxShieldStat.Value);
        _currentShield = Mathf.Min(_currentShield, _maxShield);
        OnShieldChanged?.Invoke(_currentShield, _maxShield);
    }

    private void Initialize(float startingMax)
    {
        _maxHealth = startingMax;
        _currentHealth = _maxHealth;
        _isDead = false;
        OnHealthChanged?.Invoke(_currentHealth, _maxHealth);
    }

    private void HandleMaxHealthChanged()
    {
        float diff = _maxHealthStat.Value - _maxHealth;
        _maxHealth = _maxHealthStat.Value;
        if (diff > 0f) _queuedHealthRestore += diff;
        else _currentHealth = Mathf.Min(_currentHealth, _maxHealth);
        OnHealthChanged?.Invoke(_currentHealth, _maxHealth);
    }

    public void ApplyDamage(float amount)
    {
        if (_isDead || amount <= 0f) return;
        if (_invulnerability != null && _invulnerability.IsInvulnerable) return;


        if (_currentShield > 0f)
        {
            float absorbed = Mathf.Min(_currentShield, amount);
            _currentShield -= absorbed;
            amount -= absorbed;
            OnShieldChanged?.Invoke(_currentShield, _maxShield);
        }

        if (amount > 0f)
        {
            _currentHealth = Mathf.Max(0f, _currentHealth - amount);
            OnHealthChanged?.Invoke(_currentHealth, _maxHealth);

            if (_currentHealth <= 0f) Die();
        }
    }

    public void Heal(float amount)
    {
        if (_isDead || amount <= 0f) return;
        _queuedHealthRestore += amount;
    }

    private void Die()
    {
        if (_isDead) return;
        _isDead = true;
        OnDied?.Invoke();

        if (gameObject.CompareTag("Player"))
        {
            PlayerProgressManager.Instance?.CommitActiveRunAndClear();
            GameStateManager.Instance.ChangeState(GameState.GameOver);
        }
        else
            Destroy(gameObject);
    }
}

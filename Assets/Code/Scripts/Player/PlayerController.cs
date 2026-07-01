using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;
using Unity.Cinemachine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour, IA_PlayerControls.IGamePlayActions, IFlightVehicle
{
    private PlaneData data;
    [Header("Virtual Stick (Mouse)")]
    public float mouseSensitivity = 3.0f;
    [Range(0, 1)] public float deadzone = 0.01f;
    public float inputSmoothingSpeed = 10f;

    [Header("Rail Shooter Movement (Strafe)")]
    public float strafeSpeed = 45f;
    public float baseSpeed = 40f;
    public float boostMultiplier = 2f;
    [Range(0.1f, 1f)] public float throttle = 0.5f;

    [Header("Visual Model Banking")]
    [Tooltip("Ship graphics only, as a child object.")]
    [SerializeField] private Transform visualModel;
    public float rotationLerpSpeed = 20f;
    public float maxVisualPitch = 20f;
    public float maxVisualYaw = 20f;
    public float maxVisualRoll = 35f;

    [Header("Manual Roll / Yaw / Spin")]
    public float manualRollSpeed = 180f;
    public float spinDuration = 0.5f;
    [SerializeField] private float spinCooldown = 2f;
    [SerializeField] private float spinInvulnerabilityDuration = 0.45f;
    [SerializeField] private float spinBlinkInterval = 0.06f;

    [Header("Aiming")]
    [SerializeField] private float aimConvergenceDistance = 200f;

    private Rigidbody _rb;
    private IA_PlayerControls _controls;
    private GunEngine _primaryGunEngine;
    private GunEngine _specialGunEngine;
    private Camera _mainCamera;
    public Vector3 AimTarget { get; private set; }
    private Vector2 _joystickPos;
    private bool _isBoosting;
    private bool _isFiring;
    private bool _isSpecialFiring;
    private float _currentEnergy;
    [SerializeField] private float energyPickupRestorePerSecond = 45f;
    private float _queuedEnergyRestore;
    private Stat _maxEnergyStat;
    private Stat _energyConsumptionStat;
    private Stat _energyRegenStat;

    private float _manualRollInput;
    private float _currentManualRollAngle;
    private float _spinAngle = 0f;
    private bool _isSpinning = false;
    private float _nextSpinReadyTime;
    private PlayerInvulnerability _invulnerability;
    private Renderer[] _spinRenderers = System.Array.Empty<Renderer>();
    private bool[] _spinRendererDefaults = System.Array.Empty<bool>();
    private bool _spinRenderersVisible = true;
    public float CurrentForwardSpeed { get; private set; }
    private float _tempSpeedMultiplier = 1f;
    private float _tempBoostUntil = -1f;
    private const float MaxTempBoostMultiplier = 1.25f;

    public bool TempBoostActive => Time.time < _tempBoostUntil;
    public float TempBoostMultiplier => TempBoostActive ? _tempSpeedMultiplier : 1f;
    public float CurrentEnergy => _currentEnergy;
    public float MaxEnergy => GetMaxEnergy();
    public GunEngine PrimaryGun => _primaryGunEngine;
    public GunEngine SpecialGun => _specialGunEngine;

    private float GetMaxEnergy() => _maxEnergyStat != null ? _maxEnergyStat.Value : (data != null ? data.maxEnergy : 0f);

    public void RestoreEnergy(float amount)
    {
        if (amount <= 0f) return;
        _queuedEnergyRestore += amount;
    }

    public void ApplyTempSpeedBoost(float multiplier, float duration)
    {
        if (multiplier <= 0f || duration <= 0f) return;
        _tempSpeedMultiplier = Mathf.Clamp(multiplier, 1f, MaxTempBoostMultiplier);
        _tempBoostUntil = Time.time + duration;
    }

    private Quaternion _visualBaseLocalRot;
    private Quaternion _currentTilt = Quaternion.identity;
    private static readonly Vector3 FORWARD_AXIS = Vector3.forward;

    public bool ShowMouseCursor => false;
    public Transform VehicleTransform => transform;
    public Transform PickupTargetTransform => visualModel != null ? visualModel : transform;

    public Vector2 SmoothMousePos
    {
        get
        {
            return new Vector2(
                (Screen.width * 0.5f) + (_joystickPos.x * Screen.width * 0.5f),
                (Screen.height * 0.5f) + (_joystickPos.y * Screen.height * 0.5f)
            );
        }
    }

    void Awake()
    {
        _mainCamera = Camera.main;

        if (GameStateManager.Instance != null && GameStateManager.Instance.ChosenPlaneData != null)
        {
            data = GameStateManager.Instance.ChosenPlaneData;
        }

        LoadVisualModel();
        _invulnerability = GetComponent<PlayerInvulnerability>();
        if (_invulnerability == null) _invulnerability = gameObject.AddComponent<PlayerInvulnerability>();
        CacheSpinRenderers();

        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        _rb.constraints = RigidbodyConstraints.FreezeRotationX |
                          RigidbodyConstraints.FreezeRotationY |
                          RigidbodyConstraints.FreezeRotationZ |
                          RigidbodyConstraints.FreezePositionZ;

        if (visualModel != null) _visualBaseLocalRot = visualModel.localRotation;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        var statsManager = GetComponentInChildren<PlayerStatsManager>();
        if (statsManager != null)
        {
            _maxEnergyStat = statsManager.GetHullStat(HullStatType.MaxEnergy);
            _energyConsumptionStat = statsManager.GetHullStat(HullStatType.EnergyConsuption);
            _energyRegenStat = statsManager.GetHullStat(HullStatType.EnergyRegen);
        }

        _currentEnergy = GetMaxEnergy();
    }

    private void LoadVisualModel()
    {
        PlaneVisualLoader.Load(data, visualModel, this);
        RefreshGunEngines();
    }

    private void RefreshGunEngines()
    {
        _primaryGunEngine = null;
        _specialGunEngine = null;

        foreach (GunEngine engine in GetComponentsInChildren<GunEngine>(true))
        {
            if (engine.weaponCategory == StatCategory.PrimaryWeapon)
            {
                _primaryGunEngine = engine;
            }
            else
            {
                _specialGunEngine = engine;
            }
        }
    }

    private void CacheSpinRenderers()
    {
        _spinRenderers = visualModel != null ? visualModel.GetComponentsInChildren<Renderer>(true) : System.Array.Empty<Renderer>();
        _spinRendererDefaults = new bool[_spinRenderers.Length];

        for (int i = 0; i < _spinRenderers.Length; i++)
            _spinRendererDefaults[i] = _spinRenderers[i] != null && _spinRenderers[i].enabled;

        _spinRenderersVisible = true;
    }

    private void SetSpinRenderersVisible(bool visible)
    {
        if (_spinRenderersVisible == visible) return;
        _spinRenderersVisible = visible;

        for (int i = 0; i < _spinRenderers.Length; i++)
        {
            if (_spinRenderers[i] != null)
                _spinRenderers[i].enabled = visible && (i >= _spinRendererDefaults.Length || _spinRendererDefaults[i]);
        }
    }

    void OnEnable()
    {
        HUD.RegisterVehicle(this);
        PickupManager.GetOrCreate()?.SetPlayer(this);
        if (_controls == null)
        {
            _controls = new IA_PlayerControls();
            _controls.GamePlay.AddCallbacks(this);
        }
        _controls.GamePlay.Enable();
    }

    void OnDisable()
    {
        SetSpinRenderersVisible(true);
        _controls.GamePlay.Disable();
    }

    void Update()
    {
        if (GameStateManager.Instance.CurrentState != GameState.InGame) return;

        UpdateAimTarget();

        if (_isFiring && _primaryGunEngine != null && _primaryGunEngine.AllowButtonHold)
        {
            _primaryGunEngine.RequestShoot(AimTarget);
        }

        if (_isSpecialFiring && _specialGunEngine != null && _specialGunEngine.AllowButtonHold)
        {
            _specialGunEngine.RequestShoot(AimTarget);
        }
    }

    void FixedUpdate()
    {
        if (GameStateManager.Instance.CurrentState != GameState.InGame)
            return;

        float dt = Time.fixedDeltaTime;

        HandleEnergyAndSpeed(dt);

        float inputX = 0f;
        float inputY = 0f;

        if (Mathf.Abs(_joystickPos.x) > deadzone)
            inputX = _joystickPos.x;

        if (Mathf.Abs(_joystickPos.y) > deadzone)
            inputY = _joystickPos.y;

        _rb.linearVelocity = new Vector3(
            inputX * strafeSpeed,
            inputY * strafeSpeed,
            0f
        );
    }

    private void HandleEnergyAndSpeed(float dt)
    {
        if (data == null) return;

        var levelManager = LevelManager.Instance;
        float targetSpeed = levelManager != null ? levelManager.CurrentTargetSpeed : baseSpeed;
        float targetBoostMultiplier = levelManager != null ? levelManager.boostMultiplier : boostMultiplier;
        float maxEnergy = GetMaxEnergy();
        bool canBoost = _isBoosting && _currentEnergy > 0;
        if (canBoost)
        {
            CurrentForwardSpeed = targetSpeed * targetBoostMultiplier;
            float consumption = _energyConsumptionStat != null ? _energyConsumptionStat.Value : data.energyConsuption;
            _currentEnergy -= consumption * dt;
        }
        else
        {
            CurrentForwardSpeed = targetSpeed;
            float regen = _energyRegenStat != null ? _energyRegenStat.Value : data.energyRegen;
            _currentEnergy += regen * dt;
        }
        if (_queuedEnergyRestore > 0f && _currentEnergy < maxEnergy)
        {
            float restoreStep = Mathf.Max(0.01f, energyPickupRestorePerSecond) * dt;
            float restore = Mathf.Min(_queuedEnergyRestore, Mathf.Min(restoreStep, maxEnergy - _currentEnergy));
            _queuedEnergyRestore -= restore;
            _currentEnergy += restore;
        }
        CurrentForwardSpeed *= TempBoostMultiplier;
        _currentEnergy = Mathf.Clamp(_currentEnergy, 0, maxEnergy);
    }

    void LateUpdate()
    {
        if (GameStateManager.Instance.CurrentState != GameState.InGame) return;

        UpdateAimTarget();

        if (visualModel == null) return;

        float h = Mathf.Abs(_joystickPos.x) > deadzone ? _joystickPos.x : 0f;
        float v = Mathf.Abs(_joystickPos.y) > deadzone ? _joystickPos.y : 0f;
        float dynamicRoll = h * maxVisualRoll * (1f - (Mathf.Abs(v) * 0.5f));

        if (_manualRollInput != 0f)
        {
            _currentManualRollAngle = (_currentManualRollAngle + _manualRollInput * manualRollSpeed * Time.deltaTime) % 360f;
        }
        else if (_currentManualRollAngle != 0f)
        {
            float step = manualRollSpeed * 1.2f * Time.deltaTime;
            if (_currentManualRollAngle > 0f)
                _currentManualRollAngle = _currentManualRollAngle > step ? _currentManualRollAngle - step : 0f;
            else
                _currentManualRollAngle = _currentManualRollAngle < -step ? _currentManualRollAngle + step : 0f;
        }

        Quaternion targetTilt = Quaternion.Euler(-v * maxVisualPitch, h * maxVisualYaw, dynamicRoll);
        _currentTilt = Quaternion.Slerp(_currentTilt, targetTilt, Time.deltaTime * rotationLerpSpeed);

        Quaternion rollRotation = Quaternion.AngleAxis(_currentManualRollAngle + _spinAngle, FORWARD_AXIS);
        visualModel.localRotation = _visualBaseLocalRot * rollRotation * _currentTilt;
    }

    private void UpdateAimTarget()
    {
        if (_mainCamera == null) _mainCamera = Camera.main;

        if (_mainCamera != null)
        {
            Ray aimRay = _mainCamera.ScreenPointToRay(SmoothMousePos);
            float distance = Mathf.Max(aimConvergenceDistance, _mainCamera.nearClipPlane + 0.5f);
            AimTarget = aimRay.GetPoint(distance);
            return;
        }

        AimTarget = transform.position + transform.forward * Mathf.Max(aimConvergenceDistance, 1f);
    }

    public void OnAim(InputAction.CallbackContext ctx)
    {
        if (GameStateManager.Instance.CurrentState != GameState.InGame) return;

        Vector2 delta = ctx.ReadValue<Vector2>();
        if (delta.sqrMagnitude < 0.001f) return;

        _joystickPos.x = Mathf.Clamp(_joystickPos.x + (delta.x * mouseSensitivity * 0.001f), -1f, 1f);
        _joystickPos.y = Mathf.Clamp(_joystickPos.y + (delta.y * mouseSensitivity * 0.001f), -1f, 1f);
    }

    public void OnRoll(InputAction.CallbackContext ctx)
    {
        if (GameStateManager.Instance.CurrentState != GameState.InGame) return;
        _manualRollInput = ctx.ReadValue<float>();
    }

    public void OnBoost(InputAction.CallbackContext ctx)
    {
        if (GameStateManager.Instance.CurrentState != GameState.InGame) return;
        _isBoosting = ctx.ReadValueAsButton();
    }

    public void OnSpecial(InputAction.CallbackContext ctx)
    {
        if (GameStateManager.Instance.CurrentState != GameState.InGame) return;
        if (_specialGunEngine == null) return;

        _isSpecialFiring = ctx.ReadValueAsButton();

        if (ctx.performed && !_specialGunEngine.AllowButtonHold)
        {
            UpdateAimTarget();
            _specialGunEngine.RequestShoot(AimTarget);
        }
    }

    public void OnFire(InputAction.CallbackContext ctx)
    {
        if (GameStateManager.Instance.CurrentState != GameState.InGame) return;
        _isFiring = ctx.ReadValueAsButton();

        if (ctx.performed && _primaryGunEngine != null)
        {
            if (!_primaryGunEngine.AllowButtonHold)
            {
                UpdateAimTarget();
                _primaryGunEngine.RequestShoot(AimTarget);
            }
        }
    }

    public void OnReload(InputAction.CallbackContext ctx)
    {
        if (GameStateManager.Instance.CurrentState != GameState.InGame) return;
        if (ctx.performed && _primaryGunEngine != null) _primaryGunEngine.RequestReload();
    }

    public void OnMenu(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;

        if (PauseMenuController.Instance != null && PauseMenuController.Instance.HandleMenuInput())
            return;

        if (GameStateManager.Instance.CurrentState == GameState.InGame)
        {
            GameStateManager.Instance.ChangeState(GameState.Pause);
        }
        else if (GameStateManager.Instance.CurrentState == GameState.Pause)
        {
            GameStateManager.Instance.ChangeState(GameState.InGame);
        }
    }

    public void OnSpin(InputAction.CallbackContext ctx)
    {
        if (ctx.performed &&
            !_isSpinning &&
            Time.time >= _nextSpinReadyTime &&
            GameStateManager.Instance.CurrentState == GameState.InGame)
        {
            float direction = _joystickPos.x >= 0 ? -1f : 1f;
            StartCoroutine(PerformSpin(direction));
        }
    }

    private IEnumerator PerformSpin(float direction)
    {
        _isSpinning = true;
        _nextSpinReadyTime = Time.time + Mathf.Max(0f, spinCooldown);
        _invulnerability?.Grant(spinInvulnerabilityDuration);
        CacheSpinRenderers();

        float elapsed = 0f;
        float blinkElapsed = 0f;
        float startAngle = _spinAngle;

        while (elapsed < spinDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, spinDuration));
            float curve = Mathf.SmoothStep(0, 1, t);
            _spinAngle = startAngle + (curve * 360f * direction);

            if (elapsed <= spinInvulnerabilityDuration)
            {
                blinkElapsed += Time.deltaTime;
                if (blinkElapsed >= Mathf.Max(0.01f, spinBlinkInterval))
                {
                    blinkElapsed = 0f;
                    SetSpinRenderersVisible(!_spinRenderersVisible);
                }
            }
            else
            {
                SetSpinRenderersVisible(true);
            }

            yield return null;
        }

        _spinAngle = 0f;
        SetSpinRenderersVisible(true);
        _isSpinning = false;
    }
}

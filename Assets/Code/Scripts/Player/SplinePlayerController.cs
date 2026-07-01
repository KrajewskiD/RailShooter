using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Splines;
using Unity.Cinemachine;

[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(SplineAnimate))]
public class SplinePlayerController : MonoBehaviour, IA_PlayerControls.IGamePlayActions, IFlightVehicle
{
    [Header("Plane Data")]
    [SerializeField] private PlaneData fallbackPlaneData;

    [Header("Spline & Speed")]
    [SerializeField] private float speedWeight = 1.0f;
    [SerializeField] private float mouseSensitivity = 1.0f;
    public CinemachineCamera virtualCamera;
    public float cameraRollMultiplier = 1.0f; 
    public float cameraRollDamping = 5f;

    [Header("Movement Frame (Viewport)")]
    [SerializeField] private float viewportMargin = 0.05f;
    public float lerpSpeed = 12f;

    [Header("Visual Model & Banking")]
    [SerializeField] private Transform visualModel;
    public float rotationLerpSpeed = 15f;
    public float maxVisualPitch = 20f;
    public float maxVisualYaw = 20f;
    public float maxVisualRoll = 40f;

    [Header("Spline Banking")]
    [Tooltip("Jak silnie samolot ma się przechylać w zakrętach spline'a.")]
    public float splineBankingIntensity = 2.5f;
    [Tooltip("Jak daleko przed siebie (w metrach) ma zaglądać skrypt, by ocenić zakręt.")]
    public float bankingLookAhead = 10f;

    [Header("Special Maneuvers")]
    public float manualRollSpeed = 180f;
    public float spinDuration = 0.5f;
    [SerializeField] private float spinCooldown = 2f;
    [SerializeField] private float spinInvulnerabilityDuration = 0.45f;
    [SerializeField] private float spinBlinkInterval = 0.06f;

    [Header("Aiming Dynamics")]
    public float aimConvergenceDistance = 200f;
    public float aimFreedomRadius = 60f;

    private SplineAnimate _splimate; 
    private IA_PlayerControls _controls;

    [Header("Weapons")]
    private GunEngine _primaryGunEngine;
    private GunEngine _specialGunEngine;
    private PlaneData _data;
    private Camera _mainCamera; 
    private GameStateManager _gameStateManager;
    private Transform _myTransform;
    private Transform _visualParent;
    private Transform _cameraTransform;

    private Vector2 _joystickPos; 
    private bool _isBoosting;
    private bool _isFiring;
    private bool _isSpecialFiring;
    private bool _fireRequestedThisFrame;
    private bool _specialRequestedThisFrame;
    private bool _canStreamFire; 
    private bool _canStreamSpecial;
    private float _manualRollInput;
    private float _currentManualRollAngle;
    private float _spinAngle = 0f;
    private bool _isSpinning = false;
    private float _currentEnergy;
    private float _spinTimer = 0f;
    private float _spinDirection = 0f;
    private float _nextSpinReadyTime;
    private float _spinBlinkTimer;
    private PlayerInvulnerability _invulnerability;
    private Renderer[] _spinRenderers = System.Array.Empty<Renderer>();
    private bool[] _spinRendererDefaults = System.Array.Empty<bool>();
    private bool _spinRenderersVisible = true;
    private bool _isInGame;

    private float _scaledTanHalfFov;
    private float _cachedAspect;
    private int _lastScreenWidth;
    private int _lastScreenHeight;
    private float _appliedDistance; 
    private float _cachedFrameWidth;  
    private float _cachedFrameHeight; 
    private LensSettings _lensCache;
    private float _currentVisualRoll;
    private float maxBankingAngle = 15f;

    private float _lastSentMaxSpeed = -1f;
    private float _precalculatedSensitivity;
    private float _tempSpeedMultiplier = 1f;
    private float _tempBoostUntil = -1f;
    [SerializeField] private float energyPickupRestorePerSecond = 45f;
    private float _queuedEnergyRestore;
    private Stat _maxEnergyStat;
    private Stat _energyConsumptionStat;
    private Stat _energyRegenStat;
    private const float MaxTempBoostMultiplier = 1.25f;

    public bool TempBoostActive => Time.time < _tempBoostUntil;
    public float TempBoostMultiplier => TempBoostActive ? _tempSpeedMultiplier : 1f;
    public float CurrentEnergy => _currentEnergy;
    public float MaxEnergy => GetMaxEnergy();
    public GunEngine PrimaryGun => _primaryGunEngine;
    public GunEngine SpecialGun => _specialGunEngine;

    private float GetMaxEnergy() => _maxEnergyStat != null ? _maxEnergyStat.Value : (_data != null ? _data.maxEnergy : 0f);

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

    private Vector3 _localTarget;
    private Quaternion _localTargetRot = Quaternion.identity;
    private Quaternion _currentTilt = Quaternion.identity;
    private Vector2 _smoothMousePos;

    public float CurrentForwardSpeed { get; private set; }
    public Vector3 AimTarget { get; private set; }
    public bool ShowMouseCursor => false;
    public Transform VehicleTransform => _myTransform != null ? _myTransform : transform;
    public Transform PickupTargetTransform => visualModel != null ? visualModel : VehicleTransform;
    public Vector2 SmoothMousePos => _smoothMousePos;

    public float FrameHalfWidth => _cachedFrameWidth;
    public float FrameHalfHeight => _cachedFrameHeight;

    private Quaternion _visualBaseLocalRot;
    private Vector3 _visualBaseLocalPos; 

    private static readonly Vector3 FORWARD_AXIS = Vector3.forward;

    void Awake()
    {
        _myTransform = transform;
        _mainCamera = Camera.main;
        _gameStateManager = GameStateManager.Instance;
        _cameraTransform = _mainCamera.transform;
        _splimate = GetComponent<SplineAnimate>();

        _precalculatedSensitivity = mouseSensitivity * 0.001f;

        if (_controls == null)
        {
            _controls = new IA_PlayerControls();
            _controls.GamePlay.AddCallbacks(this);
        }

        if (_gameStateManager?.ChosenPlaneData != null)
        {
            _data = _gameStateManager.ChosenPlaneData;
        }
        else if (fallbackPlaneData != null)
        {
            _data = fallbackPlaneData;
        }

        PlaneVisualLoader.Load(_data, visualModel, this);
        RefreshGunEngines();
        _invulnerability = GetComponent<PlayerInvulnerability>();
        if (_invulnerability == null) _invulnerability = gameObject.AddComponent<PlayerInvulnerability>();
        CacheSpinRenderers();

        var statsManager = GetComponentInChildren<PlayerStatsManager>();
        if (statsManager != null)
        {
            _maxEnergyStat = statsManager.GetHullStat(HullStatType.MaxEnergy);
            _energyConsumptionStat = statsManager.GetHullStat(HullStatType.EnergyConsuption);
            _energyRegenStat = statsManager.GetHullStat(HullStatType.EnergyRegen);
        }

        _currentEnergy = GetMaxEnergy();

        if (visualModel != null)
        {
            _visualParent = visualModel.parent; 
            _visualBaseLocalRot = visualModel.localRotation;
            _visualBaseLocalPos = visualModel.localPosition; 
            _localTarget.z = _visualBaseLocalPos.z; 
        }

        if (virtualCamera != null) _lensCache = virtualCamera.Lens;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Start() 
    {
        if (_cameraTransform != null && _visualParent != null)
        {
            float rawDist = Vector3.Distance(_cameraTransform.position, _visualParent.position + (_visualParent.rotation * _visualBaseLocalPos));
            _appliedDistance = Mathf.Max(rawDist, _mainCamera.nearClipPlane + 0.5f);
        }
        else _appliedDistance = 10f;

        RefreshResolutionSettings();
    }

    void OnEnable()
    {
        HUD.RegisterVehicle(this);
        PickupManager.GetOrCreate()?.SetPlayer(this);
        _controls.GamePlay.Enable();
        if (_gameStateManager == null)
        {
            _gameStateManager = GameStateManager.Instance;
        }

        if (_gameStateManager != null)
        {
            _gameStateManager.OnStateChanged -= OnStateChanged;
            _gameStateManager.OnStateChanged += OnStateChanged;
            OnStateChanged(_gameStateManager.CurrentState);
        }
        else
        {
            _isInGame = false;
        }
    }

    void OnDisable()
    {
        SetSpinRenderersVisible(true);
        _controls.GamePlay.Disable();
        if (_gameStateManager != null) _gameStateManager.OnStateChanged -= OnStateChanged;
    }

    private void OnStateChanged(GameState newState) => _isInGame = (newState == GameState.InGame);

    void Update()
    {
        if (!_isInGame) return;
        
        float dt = Time.deltaTime;
        HandleEnergyAndSpeed(dt);
                
        if (_isSpinning) UpdateSpin(dt);
    }

    void LateUpdate()
    {
        if (!_isInGame) return;

        float dt = Time.deltaTime;
        
        if (Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight) RefreshResolutionSettings();

        float moveSmooth = 1f - Mathf.Exp(-lerpSpeed * dt);
        float rotSmooth = 1f - Mathf.Exp(-rotationLerpSpeed * dt);
        float camSmooth = 1f - Mathf.Exp(-cameraRollDamping * dt);

        CalculateMovementDynamic(moveSmooth);
        ApplyVisualRotation(dt, rotSmooth, camSmooth);

        if (visualModel != null) visualModel.SetLocalPositionAndRotation(_localTarget, _localTargetRot);

        UpdateAimTarget();

        if ((_isFiring && _canStreamFire) || _fireRequestedThisFrame)
        {
            if (_primaryGunEngine != null) _primaryGunEngine.RequestShoot(AimTarget);
            _fireRequestedThisFrame = false;
        }

        if ((_isSpecialFiring && _canStreamSpecial) || _specialRequestedThisFrame)
        {
            if (_specialGunEngine != null) _specialGunEngine.RequestShoot(AimTarget);
            _specialRequestedThisFrame = false;
        }
    }

    private void RefreshResolutionSettings()
    {
        _lastScreenWidth = Screen.width;
        _lastScreenHeight = Screen.height;
        _cachedAspect = (float)_lastScreenWidth / _lastScreenHeight;
        
        float fovRad = _mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        _scaledTanHalfFov = Mathf.Tan(fovRad) * (1f - (viewportMargin * 2f));

        _cachedFrameHeight = _appliedDistance * _scaledTanHalfFov;
        _cachedFrameWidth = _cachedFrameHeight * _cachedAspect;
    }

    private void CalculateMovementDynamic(float smoothFactor)
    {
        if (visualModel == null) return;

        Quaternion camRot = _cameraTransform.rotation;
        _visualParent.rotation = camRot;

        _localTarget.x += (_joystickPos.x * _cachedFrameWidth - _localTarget.x) * smoothFactor;
        _localTarget.y += (_joystickPos.y * _cachedFrameHeight - _localTarget.y) * smoothFactor;

        float hW = _lastScreenWidth * 0.5f;
        float hH = _lastScreenHeight * 0.5f;
        float frameScale = 1f - (viewportMargin * 2f);
        _smoothMousePos.x = hW + (_joystickPos.x * hW * frameScale);
        _smoothMousePos.y = hH + (_joystickPos.y * hH * frameScale);
    }

    private void UpdateAimTarget()
    {
        if (_mainCamera != null)
        {
            Ray aimRay = _mainCamera.ScreenPointToRay(_smoothMousePos);
            float distance = Mathf.Max(aimConvergenceDistance, _mainCamera.nearClipPlane + 0.5f);
            AimTarget = aimRay.GetPoint(distance);
            return;
        }

        Transform aimRef = _visualParent != null ? _visualParent : _myTransform;
        Vector3 dynamicOffset = (aimRef.right * _joystickPos.x * aimFreedomRadius) +
                                (aimRef.up * _joystickPos.y * aimFreedomRadius);
        AimTarget = aimRef.position + (aimRef.forward * aimConvergenceDistance) + dynamicOffset;
    }

    private void ApplyVisualRotation(float dt, float rotSmoothFactor, float camSmoothFactor)
    {
        if (visualModel == null) return;

        if (_manualRollInput != 0)
        {
            _currentManualRollAngle = (_currentManualRollAngle + _manualRollInput * manualRollSpeed * dt) % 360f;
        }
        else if (_currentManualRollAngle != 0f)
        {
            float step = manualRollSpeed * 1.2f * dt;
            if (_currentManualRollAngle > 0) 
                _currentManualRollAngle = _currentManualRollAngle > step ? _currentManualRollAngle - step : 0;
            else 
                _currentManualRollAngle = _currentManualRollAngle < -step ? _currentManualRollAngle + step : 0;
        } 


        float targetModelRoll = (_joystickPos.x * maxBankingAngle);
       
        float diff = targetModelRoll - _currentVisualRoll;
        if ((diff > 0.001f ? diff : -diff) > 0.001f)
            _currentVisualRoll += diff * rotSmoothFactor;

        if (virtualCamera != null)
        {
            float targetDutch = (_joystickPos.x * -maxVisualRoll) * cameraRollMultiplier;
            float dDiff = targetDutch - _lensCache.Dutch;
            if ((dDiff > 0.01f ? dDiff : -dDiff) > 0.01f)
            {
                _lensCache.Dutch += dDiff * camSmoothFactor;
                virtualCamera.Lens = _lensCache; 
            }
        }
        
        Quaternion targetTilt = Quaternion.Euler(_joystickPos.y * -maxVisualPitch, _joystickPos.x * maxVisualYaw, _currentVisualRoll);
        _currentTilt = Quaternion.Lerp(_currentTilt, targetTilt, rotSmoothFactor);

        Quaternion rollRot = Quaternion.AngleAxis(_currentManualRollAngle + _spinAngle, FORWARD_AXIS);
        _localTargetRot = _visualBaseLocalRot * rollRot * _currentTilt;
    }

    private void HandleEnergyAndSpeed(float dt)
    {
        LevelManager levelManager = LevelManager.Instance;
        if (levelManager == null || _splimate == null) return;

        bool hasEnergyData = _data != null;
        bool canBoost = hasEnergyData && _isBoosting && _currentEnergy > 0;

        float targetSpeed = levelManager.CurrentTargetSpeed > 0f
            ? levelManager.CurrentTargetSpeed
            : levelManager.baseSpeed;

        CurrentForwardSpeed = canBoost ? targetSpeed * levelManager.boostMultiplier : targetSpeed;

        CurrentForwardSpeed *= TempBoostMultiplier;

        float targetSpeedValue = CurrentForwardSpeed * speedWeight;
        float sDiff = _lastSentMaxSpeed - targetSpeedValue;
        
        if (Mathf.Abs(sDiff) > 0.001f)
        {
            float savedT = _splimate.NormalizedTime;
            _splimate.MaxSpeed = targetSpeedValue;
            _splimate.NormalizedTime = savedT;
            _lastSentMaxSpeed = targetSpeedValue;
        }

        if (!hasEnergyData) return;

        float maxEnergy = GetMaxEnergy();

        if (canBoost)
        {
            float consumption = _energyConsumptionStat != null ? _energyConsumptionStat.Value : _data.energyConsuption;
            _currentEnergy = Mathf.Max(0f, _currentEnergy - consumption * dt);
        }
        else if (_currentEnergy < maxEnergy)
        {
            float regenRate = _energyRegenStat != null ? _energyRegenStat.Value : _data.energyRegen;
            if (regenRate > 0f) _currentEnergy = Mathf.Min(maxEnergy, _currentEnergy + regenRate * dt);
        }

        if (_queuedEnergyRestore > 0f && _currentEnergy < maxEnergy)
        {
            float restoreStep = Mathf.Max(0.01f, energyPickupRestorePerSecond) * dt;
            float restore = Mathf.Min(_queuedEnergyRestore, Mathf.Min(restoreStep, maxEnergy - _currentEnergy));
            _queuedEnergyRestore -= restore;
            _currentEnergy += restore;
        }
    }

    private float GetSplineCurvatureRoll()
    {
        if (PathMath.Instance == null || _splimate == null) return 0f;

        float totalLength = _splimate.Container.CalculateLength();
        float currentDist = _splimate.NormalizedTime * totalLength;
        
        Vector3 currentTan = PathMath.Instance.GetSampleAtDistance(currentDist).tangent;
        
        Vector3 sample1 = PathMath.Instance.GetSampleAtDistance(currentDist + bankingLookAhead * 0.5f).tangent;
        Vector3 sample2 = PathMath.Instance.GetSampleAtDistance(currentDist + bankingLookAhead).tangent;
        Vector3 sample3 = PathMath.Instance.GetSampleAtDistance(currentDist + bankingLookAhead * 1.5f).tangent;
        
        Vector3 averagedFutureTan = (sample1 + sample2 + sample3).normalized;

        float turnAngle = Vector3.SignedAngle(currentTan, averagedFutureTan, Vector3.up);
        return -turnAngle * splineBankingIntensity;
    }

    private void UpdateSpin(float dt)
    {
        _spinTimer += dt;
        float t = Mathf.Clamp01(_spinTimer / Mathf.Max(0.01f, spinDuration));
        
        _spinAngle = (360f * _spinDirection) * (t * t * (3f - 2f * t));

        if (_spinTimer <= spinInvulnerabilityDuration)
        {
            _spinBlinkTimer += dt;
            if (_spinBlinkTimer >= Mathf.Max(0.01f, spinBlinkInterval))
            {
                _spinBlinkTimer = 0f;
                SetSpinRenderersVisible(!_spinRenderersVisible);
            }
        }
        else
        {
            SetSpinRenderersVisible(true);
        }

        if (t >= 1f)
        {
            _isSpinning = false;
            _spinAngle = 0f;
            _spinTimer = 0f;
            _spinBlinkTimer = 0f;
            SetSpinRenderersVisible(true);
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

    public void OnAim(InputAction.CallbackContext ctx)
    {
        if (!_isInGame) return;
        Vector2 delta = ctx.ReadValue<Vector2>();
        _joystickPos.x = Mathf.Clamp(_joystickPos.x + (delta.x * _precalculatedSensitivity), -1f, 1f);
        _joystickPos.y = Mathf.Clamp(_joystickPos.y + (delta.y * _precalculatedSensitivity), -1f, 1f);
    }

    public void OnSpin(InputAction.CallbackContext ctx)
    {
        if (ctx.performed && !_isSpinning && _isInGame && Time.time >= _nextSpinReadyTime)
        {
            _isSpinning = true;
            _spinTimer = 0f;
            _spinBlinkTimer = 0f;
            _spinDirection = _joystickPos.x >= 0 ? -1f : 1f;
            _nextSpinReadyTime = Time.time + Mathf.Max(0f, spinCooldown);
            _invulnerability?.Grant(spinInvulnerabilityDuration);
            CacheSpinRenderers();
        }
    }

    public void OnBoost(InputAction.CallbackContext ctx)
    {
        if (ctx.performed) _isBoosting = true;
        else if (ctx.canceled) _isBoosting = false;
    }

    public void OnFire(InputAction.CallbackContext ctx)
    {
        if (_primaryGunEngine == null) return;

        _isFiring = ctx.ReadValueAsButton();

        if (ctx.performed)
        {
            _canStreamFire = _primaryGunEngine.AllowButtonHold;
            if (!_canStreamFire) _fireRequestedThisFrame = true;
        }
    }
    public void OnSpecial(InputAction.CallbackContext ctx)
    {
        if (_specialGunEngine == null) return;

        _isSpecialFiring = ctx.ReadValueAsButton();

        if (ctx.performed)
        {
            _canStreamSpecial = _specialGunEngine.AllowButtonHold;
            if (!_canStreamSpecial) _specialRequestedThisFrame = true;
        }
    }


    public void OnRoll(InputAction.CallbackContext ctx) => _manualRollInput = ctx.ReadValue<float>();
    public void OnReload(InputAction.CallbackContext ctx) { if(ctx.performed) _primaryGunEngine?.RequestReload(); }
    
    public void OnMenu(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed || _gameStateManager == null) return;

        if (PauseMenuController.Instance != null && PauseMenuController.Instance.HandleMenuInput())
            return;

        if (_gameStateManager.CurrentState == GameState.InGame) _gameStateManager.ChangeState(GameState.Pause);
        else if (_gameStateManager.CurrentState == GameState.Pause) _gameStateManager.ChangeState(GameState.InGame);
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
}

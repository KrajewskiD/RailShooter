using Unity.Cinemachine;
using UnityEngine;

[RequireComponent(typeof(CinemachineCamera))]
public class DynamicFovBySpeed : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Rigidbody from which speed is read. Falls back to virtual camera Follow target's Rigidbody if null.")]
    [SerializeField] private Rigidbody target;

    [Header("FOV Range")]
    [Range(20f, 120f)] public float baseFov = 60f;
    [Range(20f, 150f)] public float maxFov = 90f;

    public ParticleSystem speedLines;
    public float minEmission = 0f;
    public float maxEmission = 50f;

    [Header("Speed Mapping")]
    [Tooltip("Speed (m/s) at which FOV starts to widen.")]
    public float minSpeed = 40f;
    [Tooltip("Speed (m/s) at which FOV reaches maxFov.")]
    public float maxSpeed = 80f;

    [Header("Smoothing")]
    [Tooltip("Higher = faster reaction. Lower = smoother. Frame-rate independent.")]
    [Min(0.1f)] public float responsiveness = 4f;

    private ParticleSystem.MainModule _mainModule;
    private ParticleSystem.EmissionModule _emissionModule;

    private CinemachineCamera _cam;
    private IFlightVehicle _playerController;
    private float _currentFov;

    private void Awake()
    {
        _cam = GetComponent<CinemachineCamera>();
        _currentFov = baseFov;

        if (speedLines != null)
        {
            _mainModule = speedLines.main;
            _emissionModule = speedLines.emission;
        }

        ApplyFov(_currentFov);
    }

    private void OnEnable()
    {
        if (target == null && _cam != null && _cam.Follow != null)
            target = _cam.Follow.GetComponentInParent<Rigidbody>();

        if (target != null)
            _playerController = target.GetComponent<IFlightVehicle>();
    }

    private void LateUpdate()
    {
        if (target == null) return;

        float speed = target.linearVelocity.magnitude;

        if (_playerController != null)
        {
            Vector3 simulatedVelocity = target.linearVelocity;
            simulatedVelocity.z = _playerController.CurrentForwardSpeed;
            speed = simulatedVelocity.magnitude;
        }

        float t = Mathf.InverseLerp(minSpeed, maxSpeed, speed);

        float targetFov = Mathf.Lerp(baseFov, maxFov, t);
        float k = 1f - Mathf.Exp(-responsiveness * Time.deltaTime);
        _currentFov = Mathf.Lerp(_currentFov, targetFov, k);
        ApplyFov(_currentFov);

        if (speedLines != null)
        {
            UpdateVFX(t, k);
        }
    }

    private void UpdateVFX(float t, float k)
    {
        float targetRate = Mathf.Lerp(minEmission, maxEmission, t);
        _emissionModule.rateOverTime = Mathf.Lerp(_emissionModule.rateOverTime.constant, targetRate, k);

        _mainModule.simulationSpeed = Mathf.Lerp(1f, 3f, t);
    }

    private void ApplyFov(float fov)
    {
        var lens = _cam.Lens;
        lens.FieldOfView = fov;
        _cam.Lens = lens;
    }
}

using UnityEngine;
using Unity.Cinemachine;

public class HUD : MonoBehaviour
{
    [Header("UI References")]
    public RectTransform mouseCursorUI;
    public RectTransform planeReticleUI;

    [Header("Gun Battery")]
    [Tooltip("All barrel exit points (Transforms) from the plane hierarchy.")]
    public Transform[] attackPoints;

    [Header("Physics")]
    [Tooltip("Layers the reticle should detect. Exclude 'Projectiles' and 'Player'.")]
    public LayerMask hitLayers;
    public float maxCheckDistance = 1000f;

    [Header("Reticle Smoothing")]
    public Camera _mainCam;
    [Range(1f, 100f)]
    public float reticleSmoothSpeed = 40f;
    public float deadZoneFadeRadius = 250f;
    public CanvasGroup reticleCanvasGroup;
    private float _smoothedDistance = 200f;
    private Canvas _canvas;
    private Camera _uiCamera;
    private FirePointProvider _firePointProvider;
    private static IFlightVehicle _playerVehicle;
    public static void RegisterVehicle(IFlightVehicle vehicle) => _playerVehicle = vehicle;

    void Awake()
    {
        _mainCam = Camera.main;
        _canvas = GetComponentInParent<Canvas>();
        RefreshUiCamera();

        if (planeReticleUI != null && reticleCanvasGroup == null)
        {
            reticleCanvasGroup = planeReticleUI.GetComponent<CanvasGroup>();
        }
    }

    void OnEnable()  => CinemachineCore.CameraUpdatedEvent.AddListener(OnCameraUpdated);
    void OnDisable() => CinemachineCore.CameraUpdatedEvent.RemoveListener(OnCameraUpdated);

    private void OnCameraUpdated(CinemachineBrain brain)
    {
        if (_playerVehicle == null) return;

        if (_mainCam == null) _mainCam = Camera.main;
        if (_mainCam == null) return;

        RefreshUiCamera();

        if (mouseCursorUI != null)
        {
            bool shouldShow = _playerVehicle.ShowMouseCursor;
            
            if (mouseCursorUI.gameObject.activeSelf != shouldShow)
            {
                mouseCursorUI.gameObject.SetActive(shouldShow);
            }

            if (shouldShow) UpdateMouseCursor();
        }

        if (planeReticleUI != null)
        {
            UpdateReticle();
        }
    }

    private void UpdateMouseCursor()
    {
        if (mouseCursorUI == null || _playerVehicle == null) return;

        Vector2 mousePos = _playerVehicle.SmoothMousePos;
        float padding = 20f;
        
        float clampedX = Mathf.Clamp(mousePos.x, padding, Screen.width - padding);
        float clampedY = Mathf.Clamp(mousePos.y, padding, Screen.height - padding);
        SetScreenPosition(mouseCursorUI, new Vector2(clampedX, clampedY), 1f, false);
    }

    private void UpdateReticle()
    {
        if (_playerVehicle == null || _mainCam == null) return;

        ResolveShotLine(out Vector3 rayOrigin, out Vector3 rayDir);

        float targetDistance = maxCheckDistance;
        if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, maxCheckDistance, hitLayers))
        {
            targetDistance = hit.distance;
        }
        
        _smoothedDistance = Mathf.Lerp(_smoothedDistance, targetDistance, Time.deltaTime * 10f);
        
        Vector3 worldTarget = rayOrigin + rayDir * _smoothedDistance;
        Vector3 screenPoint = _mainCam.WorldToScreenPoint(worldTarget);

        if (screenPoint.z > 0)
        {
            if (!planeReticleUI.gameObject.activeSelf) planeReticleUI.gameObject.SetActive(true);

            float clampedX = Mathf.Clamp(screenPoint.x, 40f, Screen.width - 40f);
            float clampedY = Mathf.Clamp(screenPoint.y, 40f, Screen.height - 40f);

            Vector2 finalPos = new Vector2(clampedX, clampedY);

            float t = 1f - Mathf.Exp(-reticleSmoothSpeed * Time.deltaTime);
            SetScreenPosition(planeReticleUI, finalPos, t, true);

            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float distToCenter = Vector2.Distance(finalPos, screenCenter);
            float normalizedFocus = Mathf.Clamp01(distToCenter / deadZoneFadeRadius);
            float targetScale = Mathf.Lerp(0.4f, 1.0f, normalizedFocus);
            planeReticleUI.localScale = Vector3.Lerp(planeReticleUI.localScale, Vector3.one * targetScale, t);
            if (reticleCanvasGroup != null)
            {
                float targetAlpha = Mathf.Lerp(0.3f, 1.0f, normalizedFocus);
                reticleCanvasGroup.alpha = Mathf.Lerp(reticleCanvasGroup.alpha, targetAlpha, t);
            }

        }
        else
        {
            planeReticleUI.gameObject.SetActive(false);
        }
    }

    private void ResolveShotLine(out Vector3 origin, out Vector3 direction)
    {
        Transform referencePoint = ResolveReferenceFirePoint();
        if (referencePoint != null)
        {
            origin = referencePoint.position;
            direction = referencePoint.forward;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
            direction.Normalize();
            return;
        }

        Transform vehicle = _playerVehicle?.VehicleTransform;
        if (vehicle != null)
        {
            origin = vehicle.position;
            direction = vehicle.forward;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
            direction.Normalize();
            return;
        }

        origin = _mainCam.transform.position;
        direction = _playerVehicle.AimTarget - origin;
        if (direction.sqrMagnitude < 0.0001f) direction = _mainCam.transform.forward;
        direction.Normalize();
    }

    private Transform ResolveReferenceFirePoint()
    {
        if (_playerVehicle?.VehicleTransform != null && _firePointProvider == null)
        {
            _firePointProvider = _playerVehicle.VehicleTransform.GetComponent<FirePointProvider>();
            if (_firePointProvider == null)
                _firePointProvider = _playerVehicle.VehicleTransform.GetComponentInChildren<FirePointProvider>(true);
        }

        Transform providerPoint = _firePointProvider != null
            ? _firePointProvider.GetReferenceFirePoint(StatCategory.PrimaryWeapon)
            : null;

        if (providerPoint != null) return providerPoint;

        if (attackPoints == null) return null;
        for (int i = 0; i < attackPoints.Length; i++)
        {
            if (attackPoints[i] != null) return attackPoints[i];
        }

        return null;
    }

    private void RefreshUiCamera()
    {
        if (_canvas == null) return;
        _uiCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : (_canvas.worldCamera != null ? _canvas.worldCamera : _mainCam);
    }

    private void SetScreenPosition(RectTransform target, Vector2 screenPosition, float t, bool smooth)
    {
        if (target == null) return;

        RectTransform parentRect = target.parent as RectTransform;
        if (parentRect == null)
        {
            target.position = screenPosition;
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPosition, _uiCamera, out Vector2 localPosition))
            return;

        target.anchoredPosition = smooth
            ? Vector2.Lerp(target.anchoredPosition, localPosition, t)
            : localPosition;
    }
}

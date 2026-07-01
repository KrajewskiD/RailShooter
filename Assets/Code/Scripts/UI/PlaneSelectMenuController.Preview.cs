using UnityEngine;
using UnityEngine.UIElements;

public partial class PlaneSelectMenuController
{
    private RenderTexture _previewTexture;
    private Transform _previewRig;
    private Transform _previewPivot;
    private Camera _previewCamera;
    private GameObject _previewInstance;
    private PlaneData _previewedPlane;
    private VisualElement _previewInputElement;
    private bool _isPreviewDragging;
    private int _previewPointerId = -1;
    private int _previewLayer;
    private float _previewYaw;
    private Vector2 _lastPreviewPointerPosition;

    private void RenderPreviewModel(PlaneData data)
    {
        EnsurePreviewRig();

        bool hasModel = data != null && data.visualPrefab != null && _previewTexture != null;
        if (_previewRenderImage != null)
        {
            _previewRenderImage.image = _previewTexture;
            _previewRenderImage.scaleMode = ScaleMode.ScaleToFit;
            _previewRenderImage.style.display = hasModel ? DisplayStyle.Flex : DisplayStyle.None;
        }

        if (_previewImageEl != null)
        {
            _previewImageEl.style.backgroundImage = new StyleBackground((Texture2D)null);
        }

        if (!hasModel)
        {
            ClearPreviewModel();
            _previewedPlane = data;
            return;
        }

        if (_previewedPlane == data && _previewInstance != null) return;
        LoadPreviewModel(data);
    }

    private void EnsurePreviewRig()
    {
        EnsurePreviewTexture();

        if (_previewRig != null)
        {
            if (_previewCamera != null) _previewCamera.targetTexture = _previewTexture;
            return;
        }

        _previewLayer = LayerMask.NameToLayer("Planes");
        if (_previewLayer < 0) _previewLayer = LayerMask.NameToLayer("UI");
        if (_previewLayer < 0) _previewLayer = 0;

        var rigGo = new GameObject("Plane Select Preview Rig");
        rigGo.hideFlags = HideFlags.HideAndDontSave;
        _previewRig = rigGo.transform;
        _previewRig.position = new Vector3(10000f, 10000f, 10000f);

        var pivotGo = new GameObject("Preview Pivot");
        pivotGo.hideFlags = HideFlags.HideAndDontSave;
        pivotGo.transform.SetParent(_previewRig, false);
        _previewPivot = pivotGo.transform;

        var cameraGo = new GameObject("Preview Camera");
        cameraGo.hideFlags = HideFlags.HideAndDontSave;
        cameraGo.layer = _previewLayer;
        cameraGo.transform.SetParent(_previewRig, false);
        float cameraDistance = Mathf.Max(1f, previewCameraDistance);
        cameraGo.transform.localPosition = new Vector3(0f, 0.2f, -cameraDistance);
        cameraGo.transform.localRotation = Quaternion.Euler(8f, 0f, 0f);

        _previewCamera = cameraGo.AddComponent<Camera>();
        _previewCamera.clearFlags = CameraClearFlags.SolidColor;
        _previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _previewCamera.orthographic = true;
        _previewCamera.orthographicSize = previewModelSize * 0.75f;
        _previewCamera.nearClipPlane = 0.01f;
        _previewCamera.farClipPlane = 100f;
        _previewCamera.cullingMask = 1 << _previewLayer;
        _previewCamera.allowHDR = false;
        _previewCamera.allowMSAA = true;
        _previewCamera.targetTexture = _previewTexture;

        CreatePreviewLight("Preview Key Light", new Vector3(-3f, 4f, -4f), 2.4f, new Color(1f, 0.95f, 0.84f));
        CreatePreviewLight("Preview Fill Light", new Vector3(4f, 2f, 3f), 1.4f, new Color(0.58f, 0.76f, 1f));
    }

    private void EnsurePreviewTexture()
    {
        int width = Mathf.Max(256, previewTextureSize.x);
        int height = Mathf.Max(256, previewTextureSize.y);
        if (_previewTexture != null && _previewTexture.width == width && _previewTexture.height == height) return;

        if (_previewCamera != null) _previewCamera.targetTexture = null;
        if (_previewRenderImage != null) _previewRenderImage.image = null;

        ReleasePreviewTexture();

        _previewTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            name = "Plane Select Preview",
            antiAliasing = 4,
            useMipMap = false,
            autoGenerateMips = false
        };
        _previewTexture.Create();

        if (_previewCamera != null) _previewCamera.targetTexture = _previewTexture;
        if (_previewRenderImage != null) _previewRenderImage.image = _previewTexture;
    }

    private void CreatePreviewLight(string lightName, Vector3 localPosition, float intensity, Color color)
    {
        var lightGo = new GameObject(lightName);
        lightGo.hideFlags = HideFlags.HideAndDontSave;
        lightGo.layer = _previewLayer;
        lightGo.transform.SetParent(_previewRig, false);
        lightGo.transform.localPosition = localPosition;

        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.intensity = intensity;
        light.color = color;
        light.range = 12f;
        light.shadows = LightShadows.None;
    }

    private void LoadPreviewModel(PlaneData data)
    {
        ClearPreviewModel();
        _previewedPlane = data;

        if (data == null || data.visualPrefab == null || _previewPivot == null) return;

        _previewInstance = Instantiate(data.visualPrefab, _previewPivot);
        _previewInstance.name = $"{data.displayName} Preview";
        _previewInstance.transform.localPosition = Vector3.zero;
        _previewInstance.transform.localRotation = Quaternion.Euler(0f, previewModelYawOffset, 0f);
        _previewInstance.transform.localScale = Vector3.one;

        PreparePreviewObject(_previewInstance);

        if (TryGetPreviewBounds(_previewInstance, out Bounds bounds))
        {
            CenterPreviewInstance(bounds);

            float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float scale = previewModelSize / Mathf.Max(0.01f, maxDimension);
            _previewInstance.transform.localScale = Vector3.one * scale;

            if (TryGetPreviewBounds(_previewInstance, out bounds))
            {
                CenterPreviewInstance(bounds);
                FitPreviewCamera(bounds);
            }
        }

        _previewYaw = previewInitialYaw;
        ApplyPreviewRotation();
        SetPreviewRigActive(IsOpen());
    }

    private void PreparePreviewObject(GameObject instance)
    {
        SetLayerRecursively(instance, _previewLayer);

        foreach (var behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour != null) behaviour.enabled = false;
        }

        foreach (var collider in instance.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;

        foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }

        foreach (var particles in instance.GetComponentsInChildren<ParticleSystem>(true))
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        foreach (var particleRenderer in instance.GetComponentsInChildren<ParticleSystemRenderer>(true))
            particleRenderer.enabled = false;

        foreach (var trail in instance.GetComponentsInChildren<TrailRenderer>(true))
            trail.enabled = false;

        foreach (var line in instance.GetComponentsInChildren<LineRenderer>(true))
            line.enabled = false;
    }

    private void CenterPreviewInstance(Bounds bounds)
    {
        if (_previewInstance == null || _previewPivot == null) return;
        Vector3 localCenter = _previewPivot.InverseTransformPoint(bounds.center);
        _previewInstance.transform.localPosition -= localCenter;
    }

    private void FitPreviewCamera(Bounds bounds)
    {
        if (_previewCamera == null) return;

        float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        float cameraDistance = Mathf.Max(1f, previewCameraDistance);
        _previewCamera.orthographicSize = Mathf.Max(1.1f, maxDimension * 0.62f);
        _previewCamera.transform.localPosition = new Vector3(0f, maxDimension * 0.05f, -cameraDistance);
        _previewCamera.farClipPlane = Mathf.Max(30f, cameraDistance + maxDimension * 4f);
    }

    private static bool TryGetPreviewBounds(GameObject root, out Bounds bounds)
    {
        bounds = new Bounds(Vector3.zero, Vector3.zero);
        bool hasBounds = false;

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || !renderer.enabled) continue;
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = layer;
            child.gameObject.tag = "Untagged";
        }
    }

    private void ApplyPreviewRotation()
    {
        if (_previewPivot == null) return;
        _previewPivot.localRotation = Quaternion.Euler(0f, _previewYaw, 0f);
    }

    private void RegisterPreviewInput()
    {
        if (_previewImageEl == null || _previewInputElement == _previewImageEl) return;

        UnregisterPreviewInput();
        _previewInputElement = _previewImageEl;
        _previewInputElement.RegisterCallback<PointerDownEvent>(HandlePreviewPointerDown);
        _previewInputElement.RegisterCallback<PointerMoveEvent>(HandlePreviewPointerMove);
        _previewInputElement.RegisterCallback<PointerUpEvent>(HandlePreviewPointerUp);
        _previewInputElement.RegisterCallback<PointerCaptureOutEvent>(HandlePreviewPointerCaptureOut);
    }

    private void UnregisterPreviewInput()
    {
        if (_previewInputElement == null) return;

        _previewInputElement.UnregisterCallback<PointerDownEvent>(HandlePreviewPointerDown);
        _previewInputElement.UnregisterCallback<PointerMoveEvent>(HandlePreviewPointerMove);
        _previewInputElement.UnregisterCallback<PointerUpEvent>(HandlePreviewPointerUp);
        _previewInputElement.UnregisterCallback<PointerCaptureOutEvent>(HandlePreviewPointerCaptureOut);
        _previewInputElement = null;
        _isPreviewDragging = false;
        _previewPointerId = -1;
    }

    private void HandlePreviewPointerDown(PointerDownEvent evt)
    {
        if (evt.button != 0 || _previewInstance == null || _previewPivot == null) return;

        _isPreviewDragging = true;
        _previewPointerId = evt.pointerId;
        _lastPreviewPointerPosition = new Vector2(evt.position.x, evt.position.y);
        _previewInputElement?.CapturePointer(evt.pointerId);
        evt.StopPropagation();
    }

    private void HandlePreviewPointerMove(PointerMoveEvent evt)
    {
        if (!_isPreviewDragging || evt.pointerId != _previewPointerId) return;

        var position = new Vector2(evt.position.x, evt.position.y);
        float deltaX = position.x - _lastPreviewPointerPosition.x;
        _lastPreviewPointerPosition = position;
        _previewYaw += deltaX * previewRotationSpeed;
        ApplyPreviewRotation();
        evt.StopPropagation();
    }

    private void HandlePreviewPointerUp(PointerUpEvent evt)
    {
        if (evt.pointerId != _previewPointerId) return;
        EndPreviewDrag(evt.pointerId);
        evt.StopPropagation();
    }

    private void HandlePreviewPointerCaptureOut(PointerCaptureOutEvent evt)
    {
        _isPreviewDragging = false;
        _previewPointerId = -1;
    }

    private void EndPreviewDrag(int pointerId)
    {
        _isPreviewDragging = false;
        _previewPointerId = -1;
        _previewInputElement?.ReleasePointer(pointerId);
    }

    private void ClearPreviewModel()
    {
        if (_previewInstance == null) return;
        _previewInstance.SetActive(false);
        DestroyUnityObject(_previewInstance);
        _previewInstance = null;
    }

    private void SetPreviewRigActive(bool active)
    {
        if (_previewRig != null)
            _previewRig.gameObject.SetActive(active);

        if (_previewCamera != null)
            _previewCamera.enabled = active;
    }

    private void ReleasePreviewResources()
    {
        ClearPreviewModel();

        if (_previewRig != null)
        {
            DestroyUnityObject(_previewRig.gameObject);
            _previewRig = null;
            _previewPivot = null;
            _previewCamera = null;
        }

        ReleasePreviewTexture();
    }

    private void ReleasePreviewTexture()
    {
        if (_previewTexture == null) return;

        if (_previewRenderImage != null)
            _previewRenderImage.image = null;

        _previewTexture.Release();
        DestroyUnityObject(_previewTexture);
        _previewTexture = null;
    }

    private static void DestroyUnityObject(UnityEngine.Object obj)
    {
        UnityObjectUtility.Destroy(obj);
    }
}

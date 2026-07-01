using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

public class TutorialFlowController : MonoBehaviour
{
    [Header("Timing")]
    [SerializeField] private float tutorialDuration = 90f;
    [SerializeField] private float collapseStartTime = 80f;
    [SerializeField] private float bloomStartTime = 85f;

    [Header("Tunnel")]
    [SerializeField] private TunnelGenerator tunnelGenerator;
    [SerializeField] private TunnelSpawnManager spawnManager;
    [SerializeField] private float collapsedRadiusMultiplier = 0.22f;

    [Header("Transition")]
    [SerializeField] private Volume globalVolume;
    [SerializeField] private float targetBloomIntensity = 18f;
    [SerializeField] private float targetBloomScatter = 0.95f;
    [SerializeField] private string nextSceneName = "TerrainScene";

    [Header("Controls Hint")]
    [SerializeField] private bool showControlsHint = true;
    [SerializeField] private float controlsHintDuration = 18f;
    [SerializeField] private float controlsHintFadeTime = 1.2f;
    [SerializeField] private PanelSettings controlsHintPanelSettings;

    private float _timer;
    private bool _collapseStarted;
    private bool _completed;
    private VolumeProfile _runtimeVolumeProfile;
    private Bloom _bloom;
    private GameObject _controlsHintObject;
    private VisualElement _controlsHintRoot;
    private VisualElement _controlsHintPanel;
    private float _initialRadiusMultiplier = 1f;
    private float _initialBloomIntensity;
    private float _initialBloomScatter;

    private void Awake()
    {
        if (tunnelGenerator == null) tunnelGenerator = FindObjectOfType<TunnelGenerator>();
        if (spawnManager == null) spawnManager = FindObjectOfType<TunnelSpawnManager>();
        if (globalVolume == null) globalVolume = FindObjectOfType<Volume>();

        if (tunnelGenerator != null)
        {
            _initialRadiusMultiplier = tunnelGenerator.runtimeRadiusMultiplier;
        }

        PrepareRuntimeBloom();
    }

    private void Start()
    {
        if (showControlsHint) CreateControlsHint();
    }

    private void Update()
    {
        if (_completed) return;

        _timer += Time.deltaTime;

        if (!_collapseStarted && _timer >= collapseStartTime)
        {
            _collapseStarted = true;
            if (spawnManager != null) spawnManager.SetSpawningEnabled(false, clearExistingSpawnedObjects: true);
        }

        UpdateTunnelCollapse();
        UpdateBloomTransition();
        UpdateControlsHint();

        if (_timer >= tutorialDuration)
        {
            CompleteTutorial();
        }
    }

    private void UpdateTunnelCollapse()
    {
        if (tunnelGenerator == null || _timer < collapseStartTime) return;

        float duration = Mathf.Max(0.01f, tutorialDuration - collapseStartTime);
        float t = Mathf.Clamp01((_timer - collapseStartTime) / duration);
        t = Mathf.SmoothStep(0f, 1f, t);

        float multiplier = Mathf.Lerp(_initialRadiusMultiplier, collapsedRadiusMultiplier, t);
        tunnelGenerator.SetRuntimeRadiusMultiplier(multiplier);
    }

    private void UpdateBloomTransition()
    {
        if (_bloom == null || _timer < bloomStartTime) return;

        float duration = Mathf.Max(0.01f, tutorialDuration - bloomStartTime);
        float t = Mathf.Clamp01((_timer - bloomStartTime) / duration);
        t = Mathf.SmoothStep(0f, 1f, t);

        _bloom.intensity.value = Mathf.Lerp(_initialBloomIntensity, targetBloomIntensity, t);
        _bloom.scatter.value = Mathf.Lerp(_initialBloomScatter, targetBloomScatter, t);
    }

    private void PrepareRuntimeBloom()
    {
        if (globalVolume == null) return;

        VolumeProfile sourceProfile = globalVolume.sharedProfile != null ? globalVolume.sharedProfile : globalVolume.profile;
        if (sourceProfile != null)
        {
            _runtimeVolumeProfile = Instantiate(sourceProfile);
            globalVolume.profile = _runtimeVolumeProfile;
        }
        else
        {
            _runtimeVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
            globalVolume.profile = _runtimeVolumeProfile;
        }

        if (!_runtimeVolumeProfile.TryGet(out _bloom))
        {
            _bloom = _runtimeVolumeProfile.Add<Bloom>(true);
        }

        _bloom.active = true;
        _bloom.intensity.overrideState = true;
        _bloom.scatter.overrideState = true;

        _initialBloomIntensity = _bloom.intensity.value;
        _initialBloomScatter = _bloom.scatter.value;
    }

    private void CompleteTutorial()
    {
        _completed = true;
        PlayerProgressManager.Instance?.CompleteTutorial();
        SceneFlow.LoadScene(nextSceneName);
    }

    private void CreateControlsHint()
    {
        if (controlsHintDuration <= 0f) return;

        _controlsHintObject = new GameObject("Tutorial Controls Hint");
        _controlsHintObject.transform.SetParent(transform, false);

        UIDocument document = _controlsHintObject.AddComponent<UIDocument>();
        if (controlsHintPanelSettings == null)
        {
            controlsHintPanelSettings = Resources.Load<PanelSettings>("UI/InGameHUDPanelSettings");
        }
        document.panelSettings = controlsHintPanelSettings;

        _controlsHintRoot = document.rootVisualElement;
        _controlsHintRoot.pickingMode = PickingMode.Ignore;
        _controlsHintRoot.style.position = Position.Absolute;
        _controlsHintRoot.style.left = 0;
        _controlsHintRoot.style.top = 0;
        _controlsHintRoot.style.right = 0;
        _controlsHintRoot.style.bottom = 0;

        _controlsHintPanel = new VisualElement { pickingMode = PickingMode.Ignore };
        _controlsHintPanel.style.position = Position.Absolute;
        _controlsHintPanel.style.left = 0;
        _controlsHintPanel.style.right = 0;
        _controlsHintPanel.style.bottom = 28;
        _controlsHintPanel.style.flexDirection = FlexDirection.Row;
        _controlsHintPanel.style.alignItems = Align.Center;
        _controlsHintPanel.style.justifyContent = Justify.Center;
        _controlsHintPanel.style.paddingLeft = 18;
        _controlsHintPanel.style.paddingRight = 18;
        _controlsHintPanel.style.paddingTop = 7;
        _controlsHintPanel.style.paddingBottom = 7;
        _controlsHintPanel.style.backgroundColor = new Color(0.02f, 0.05f, 0.11f, 0.72f);
        _controlsHintPanel.style.borderTopWidth = 1;
        _controlsHintPanel.style.borderBottomWidth = 1;
        _controlsHintPanel.style.borderLeftWidth = 1;
        _controlsHintPanel.style.borderRightWidth = 1;
        _controlsHintPanel.style.borderTopColor = new Color(1f, 0.72f, 0.22f, 0.7f);
        _controlsHintPanel.style.borderBottomColor = new Color(0.22f, 0.55f, 1f, 0.34f);
        _controlsHintPanel.style.borderLeftColor = new Color(0.22f, 0.55f, 1f, 0.28f);
        _controlsHintPanel.style.borderRightColor = new Color(0.22f, 0.55f, 1f, 0.28f);
        _controlsHintPanel.style.opacity = 0f;

        AddHintCell("MYSZ", "STEROWANIE");
        AddHintCell("LPM", "STRZAL");
        AddHintCell("PPM", "SPECJALNY");
        AddHintCell("SHIFT", "BOOST");
        AddHintCell("R", "PRZELADUJ");
        AddHintCell("Q / E", "OBROT");

        _controlsHintRoot.Add(_controlsHintPanel);
    }

    private void AddHintCell(string key, string label)
    {
        VisualElement cell = new VisualElement { pickingMode = PickingMode.Ignore };
        cell.style.flexDirection = FlexDirection.Column;
        cell.style.alignItems = Align.Center;
        cell.style.justifyContent = Justify.Center;
        cell.style.width = 72;
        cell.style.minWidth = 62;
        cell.style.marginLeft = 3;
        cell.style.marginRight = 3;

        Label keyLabel = new Label(key) { pickingMode = PickingMode.Ignore };
        keyLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        keyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        keyLabel.style.fontSize = 12;
        keyLabel.style.color = new Color(1f, 0.82f, 0.36f, 1f);

        Label textLabel = new Label(label) { pickingMode = PickingMode.Ignore };
        textLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        textLabel.style.fontSize = 8;
        textLabel.style.color = new Color(0.78f, 0.85f, 0.96f, 0.82f);

        cell.Add(keyLabel);
        cell.Add(textLabel);
        _controlsHintPanel.Add(cell);
    }

    private void UpdateControlsHint()
    {
        if (_controlsHintPanel == null) return;

        if (_timer >= controlsHintDuration)
        {
            _controlsHintPanel.style.display = DisplayStyle.None;
            return;
        }

        float fade = Mathf.Max(0.01f, controlsHintFadeTime);
        float fadeIn = Mathf.Clamp01(_timer / fade);
        float fadeOut = Mathf.Clamp01((controlsHintDuration - _timer) / fade);
        _controlsHintPanel.style.opacity = Mathf.Min(fadeIn, fadeOut);
    }

    private void OnDestroy()
    {
        if (_runtimeVolumeProfile != null)
        {
            Destroy(_runtimeVolumeProfile);
        }

        if (_controlsHintObject != null)
        {
            Destroy(_controlsHintObject);
        }
    }
}

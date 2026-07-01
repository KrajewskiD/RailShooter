using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public class OptionsPanelController
{
    private const string PrefMusic      = "opt.music";
    private const string PrefSfx        = "opt.sfx";
    private const string PrefResolution = "opt.resolution";
    private static readonly Vector2Int DefaultResolution = new(1920, 1080);

    private static readonly string[] TabNames   = { "tab-audio", "tab-video", "tab-controls" };
    private static readonly string[] PanelNames = { "panel-audio", "panel-video", "panel-controls" };

    private static readonly Vector2Int[] PopularResolutions =
    {
        new(800,  600),
        new(1024, 768),
        new(1280, 720),
        new(1366, 768),
        new(1600, 900),
        new(1920, 1080),
        new(2560, 1080),
        new(2560, 1440),
        new(3440, 1440),
        new(3840, 2160),
    };

    private VisualElement _overlay;
    private Slider        _musicSlider, _sfxSlider;
    private Label         _musicValue, _sfxValue;
    private Button        _fullscreenBtn;
    private DropdownField _resolutionDropdown;
    private Button[]      _tabButtons;
    private VisualElement[] _panels;
    private int           _activeTab;
    private bool          _fullscreenOn;

    private float  _snapMusic, _snapSfx;
    private bool   _snapFullscreen;
    private string _snapResolutionStr;

    public bool IsOpen { get; private set; }

    public void Bind(VisualElement root)
    {
        _overlay = root?.Q("options-overlay");
        if (_overlay == null) return;

        _musicSlider        = _overlay.Q<Slider>("music-slider");
        _sfxSlider          = _overlay.Q<Slider>("sfx-slider");
        _musicValue         = _overlay.Q<Label>("music-value");
        _sfxValue           = _overlay.Q<Label>("sfx-value");
        _fullscreenBtn      = _overlay.Q<Button>("fullscreen-toggle");
        _resolutionDropdown = _overlay.Q<DropdownField>("resolution-dropdown");

        if (_fullscreenBtn != null) _fullscreenBtn.clicked += () => SetFullscreen(!_fullscreenOn);

        PopulateResolutions();

        _tabButtons = new Button[TabNames.Length];
        _panels     = new VisualElement[PanelNames.Length];
        for (int i = 0; i < TabNames.Length; i++)
        {
            int idx = i;
            _tabButtons[i] = _overlay.Q<Button>(TabNames[i]);
            _panels[i]     = _overlay.Q(PanelNames[i]);
            if (_tabButtons[i] != null) _tabButtons[i].clicked += () => SetActiveTab(idx);
        }

        _musicSlider?.RegisterValueChangedCallback(e =>
        {
            if (_musicValue != null) _musicValue.text = Mathf.RoundToInt(e.newValue).ToString();
        });
        _sfxSlider?.RegisterValueChangedCallback(e =>
        {
            if (_sfxValue != null) _sfxValue.text = Mathf.RoundToInt(e.newValue).ToString();
        });

        var closeBtn  = _overlay.Q<Button>("options-close");
        var cancelBtn = _overlay.Q<Button>("options-cancel");
        var saveBtn   = _overlay.Q<Button>("options-save");
        if (closeBtn  != null) closeBtn.clicked  += Cancel;
        if (cancelBtn != null) cancelBtn.clicked += Cancel;
        if (saveBtn   != null) saveBtn.clicked   += SaveAndClose;

        _overlay.RegisterCallback<PointerDownEvent>(e =>
        {
            if (e.target == _overlay) Cancel();
        });

        LoadFromPrefs();
        ApplyAll();
    }

    public void Open()
    {
        if (_overlay == null) return;

        SetFullscreen(Screen.fullScreen);
        SyncResolutionToScreen();

        _snapMusic         = _musicSlider != null ? _musicSlider.value : 0f;
        _snapSfx           = _sfxSlider   != null ? _sfxSlider.value   : 0f;
        _snapFullscreen    = _fullscreenOn;
        _snapResolutionStr = _resolutionDropdown != null ? _resolutionDropdown.value : "";

        SetActiveTab(0);
        _overlay.style.display = DisplayStyle.Flex;
        IsOpen = true;
    }

    public void Cancel()
    {
        if (_musicSlider != null) _musicSlider.value = _snapMusic;
        if (_sfxSlider   != null) _sfxSlider.value   = _snapSfx;
        if (_resolutionDropdown != null && !string.IsNullOrEmpty(_snapResolutionStr))
        {
            int idx = _resolutionDropdown.choices.IndexOf(_snapResolutionStr);
            if (idx >= 0) _resolutionDropdown.index = idx;
        }
        SetFullscreen(_snapFullscreen);
        CloseOnly();
    }

    public void SaveAndClose()
    {
        PersistAll();
        ApplyAll();
        CloseOnly();
    }

    private void CloseOnly()
    {
        if (_overlay == null) return;
        _overlay.style.display = DisplayStyle.None;
        IsOpen = false;
    }

    private void SetActiveTab(int index)
    {
        if (_tabButtons == null || _tabButtons.Length == 0) return;
        _activeTab = Mathf.Clamp(index, 0, _tabButtons.Length - 1);
        for (int i = 0; i < _tabButtons.Length; i++)
        {
            _tabButtons[i]?.EnableInClassList("is-active", i == _activeTab);
            if (_panels[i] != null)
                _panels[i].style.display = i == _activeTab ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    private void SetFullscreen(bool on)
    {
        _fullscreenOn = on;
        _fullscreenBtn?.EnableInClassList("is-on", on);
    }

    private void LoadFromPrefs()
    {
        if (_musicSlider != null) _musicSlider.value = PlayerPrefs.GetFloat(PrefMusic, 70f);
        if (_sfxSlider   != null) _sfxSlider.value   = PlayerPrefs.GetFloat(PrefSfx, 85f);
        SetFullscreen(Screen.fullScreen);

        if (_resolutionDropdown != null)
        {
            string saved = PlayerPrefs.GetString(PrefResolution, "");
            int idx = -1;
            if (!string.IsNullOrEmpty(saved))
                idx = _resolutionDropdown.choices.IndexOf(saved);
            else
                idx = _resolutionDropdown.choices.IndexOf(FormatRes(DefaultResolution.x, DefaultResolution.y));
            if (idx < 0)
                idx = _resolutionDropdown.choices.IndexOf(FormatRes(Screen.width, Screen.height));
            if (idx < 0 && _resolutionDropdown.choices.Count > 0)
                idx = 0;
            if (idx >= 0) _resolutionDropdown.index = idx;
        }

        if (_musicValue != null && _musicSlider != null)
            _musicValue.text = Mathf.RoundToInt(_musicSlider.value).ToString();
        if (_sfxValue != null && _sfxSlider != null)
            _sfxValue.text = Mathf.RoundToInt(_sfxSlider.value).ToString();
    }

    private void PersistAll()
    {
        if (_musicSlider != null) PlayerPrefs.SetFloat(PrefMusic, _musicSlider.value);
        if (_sfxSlider   != null) PlayerPrefs.SetFloat(PrefSfx,   _sfxSlider.value);
        if (_resolutionDropdown != null && !string.IsNullOrEmpty(_resolutionDropdown.value))
            PlayerPrefs.SetString(PrefResolution, _resolutionDropdown.value);
        PlayerPrefs.Save();
    }

    private void ApplyAll()
    {
        if (_musicSlider != null) AudioListener.volume = Mathf.Clamp01(_musicSlider.value / 100f);

        var mode = _fullscreenOn ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        var res  = _resolutionDropdown != null ? ParseRes(_resolutionDropdown.value) : null;
        if (res.HasValue)
            Screen.SetResolution(res.Value.x, res.Value.y, mode);
        else
            Screen.fullScreenMode = mode;
    }

    private void PopulateResolutions()
    {
        if (_resolutionDropdown == null) return;

        var supported = new HashSet<long>();
        foreach (var r in Screen.resolutions)
            supported.Add(Key(r.width, r.height));

        var seen = new HashSet<long>();
        var list = new List<Vector2Int>();
        foreach (var v in PopularResolutions)
        {
            long k = Key(v.x, v.y);
            if (supported.Contains(k) && seen.Add(k)) list.Add(v);
        }

        long defaultKey = Key(DefaultResolution.x, DefaultResolution.y);
        if (seen.Add(defaultKey)) list.Add(DefaultResolution);

        long curKey = Key(Screen.width, Screen.height);
        if (seen.Add(curKey)) list.Add(new Vector2Int(Screen.width, Screen.height));

        list.Sort((a, b) => b.x != a.x ? b.x - a.x : b.y - a.y);

        var choices = new List<string>(list.Count);
        foreach (var v in list) choices.Add(FormatRes(v.x, v.y));
        _resolutionDropdown.choices = choices;
    }

    private static long Key(int w, int h) => ((long)w << 32) | (uint)h;

    private void SyncResolutionToScreen()
    {
        if (_resolutionDropdown == null) return;
        int idx = _resolutionDropdown.choices.IndexOf(FormatRes(Screen.width, Screen.height));
        if (idx >= 0) _resolutionDropdown.index = idx;
    }

    private static string FormatRes(int w, int h) => $"{w} × {h}";

    private static Vector2Int? ParseRes(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var parts = s.Split('×');
        if (parts.Length != 2) return null;
        if (int.TryParse(parts[0].Trim(), out var w) && int.TryParse(parts[1].Trim(), out var h))
            return new Vector2Int(w, h);
        return null;
    }
}

using System.Collections.Generic;
using UnityEngine;

public class FirePointProvider : MonoBehaviour
{
    [Tooltip("Root z którego skanowane są FirePointMarkery. Jeśli null, używany jest transform tego obiektu.")]
    [SerializeField] private Transform visualRoot;

    [Tooltip("Awaryjny PlaneData jeśli nie ma GameStateManager.ChosenPlaneData (np. testowa scena).")]
    [SerializeField] private PlaneData fallbackPlaneData;

    private readonly List<FirePointMarker> _primaryMarkers = new List<FirePointMarker>();
    private readonly List<FirePointMarker> _specialMarkers = new List<FirePointMarker>();

    private readonly List<Transform> _primaryActive = new List<Transform>();
    private readonly List<Transform> _specialActive = new List<Transform>();

    private PlaneData _activePlaneData;
    private bool _initialized;

    private void Awake() => EnsureInitialized();

    public void SetVisualRoot(Transform root)
    {
        visualRoot = root;
        RebuildFromCurrentVisual();
    }

    public void EnsureInitialized()
    {
        if (_initialized) return;

        _activePlaneData = ResolvePlaneData();
        ScanMarkers();
        _initialized = true;
    }

    public void RebuildFromCurrentVisual()
    {
        _initialized = false;
        EnsureInitialized();
    }

    private PlaneData ResolvePlaneData()
    {
        var gsm = GameStateManager.Instance;
        if (gsm != null && gsm.ChosenPlaneData != null) return gsm.ChosenPlaneData;
        return fallbackPlaneData;
    }

    private void ScanMarkers()
    {
        _primaryMarkers.Clear();
        _specialMarkers.Clear();
        _primaryActive.Clear();
        _specialActive.Clear();

        Transform root = visualRoot != null ? visualRoot : transform;
        var markers = root.GetComponentsInChildren<FirePointMarker>(true);
        if (markers == null || markers.Length == 0) return;

        for (int i = 0; i < markers.Length; i++)
        {
            var m = markers[i];
            if (m == null || m.roles == FirePointRole.None) continue;

            if ((m.roles & FirePointRole.Primary) != 0) _primaryMarkers.Add(m);
            if ((m.roles & FirePointRole.Special) != 0) _specialMarkers.Add(m);
        }

        _primaryMarkers.Sort(CompareMarkers);
        _specialMarkers.Sort(CompareMarkers);
    }

    private static int CompareMarkers(FirePointMarker a, FirePointMarker b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return 1;
        if (b == null) return -1;

        int byOrder = a.order.CompareTo(b.order);
        if (byOrder != 0) return byOrder;
        return string.CompareOrdinal(a.name, b.name);
    }

    public int GetMaxLevel(StatCategory category)
    {
        EnsureInitialized();
        if (_activePlaneData == null) return int.MaxValue;
        return category == StatCategory.SpecialWeapon
            ? Mathf.Max(1, _activePlaneData.maxSpecialFirePointLevel)
            : Mathf.Max(1, _activePlaneData.maxPrimaryFirePointLevel);
    }

    public int ResolveActiveCount(StatCategory category, float statValue)
    {
        EnsureInitialized();

        var src = (category == StatCategory.SpecialWeapon) ? _specialMarkers : _primaryMarkers;
        var dst = (category == StatCategory.SpecialWeapon) ? _specialActive : _primaryActive;

        dst.Clear();
        PruneDestroyedMarkers(src);
        if (src.Count == 0) return 0;

        int maxLevel = GetMaxLevel(category);
        int level = Mathf.Clamp(Mathf.FloorToInt(statValue), 1, maxLevel);

        for (int i = 0; i < src.Count; i++)
        {
            FirePointMarker marker = src[i];
            if (marker == null) continue;
            if (marker.IsActiveAtLevel(level)) dst.Add(marker.transform);
        }

        return dst.Count;
    }

    public Transform GetFirePoint(StatCategory category, int index)
    {
        EnsureInitialized();
        var list = (category == StatCategory.SpecialWeapon) ? _specialActive : _primaryActive;
        if (list.Count == 0 || index < 0 || index >= list.Count) return null;
        return list[index];
    }

    public Transform GetReferenceFirePoint(StatCategory category)
    {
        EnsureInitialized();
        var src = (category == StatCategory.SpecialWeapon) ? _specialMarkers : _primaryMarkers;
        PruneDestroyedMarkers(src);
        return src.Count > 0 && src[0] != null ? src[0].transform : null;
    }

    private static void PruneDestroyedMarkers(List<FirePointMarker> markers)
    {
        for (int i = markers.Count - 1; i >= 0; i--)
        {
            if (markers[i] == null)
                markers.RemoveAt(i);
        }
    }
}

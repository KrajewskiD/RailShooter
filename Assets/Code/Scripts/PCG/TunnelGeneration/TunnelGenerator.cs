using System.Collections.Generic;
using UnityEngine;

public class TunnelGenerator : MonoBehaviour, IScrollable
{
    [Header("Player")]
    [Tooltip("Player transform — center of the active segment window. Falls back to Camera.main if empty.")]
    [SerializeField] private Transform player;

    [Header("Physics")]
    [SerializeField] private PhysicsMaterial tunnelPhysicsMat;

    [Header("Tunnel Geometry")]
    [Tooltip("Radius along the tunnel (curve over Z).")]
    public AnimationCurve tunnelRadiusCurve = AnimationCurve.Linear(0, 80, 6000, 30);

    [Tooltip("Length of a single segment along Z.")]
    [Min(1f)] public float segmentLength = 20f;

    [Tooltip("VertexDensity: rings per segment. More rings = smoother lengthwise deformation.")]
    [Min(2)] public int vertexDensity = 24;

    [Tooltip("RadialResolution: vertices per ring. More = smoother circle.")]
    [Min(3)] public int radialResolution = 24;

    [Header("Chunking")]
    [Tooltip("Active segments AHEAD of the player.")]
    [Min(1)] public int activeSegmentsAhead = 4;

    [Tooltip("Active segments BEHIND the player (before pooling).")]
    [Min(0)] public int activeSegmentsBehind = 1;

    [Tooltip("Shared material for all segments.")]
    [SerializeField] private Material tunnelMaterial;

    [Header("Tunnel Coloring")]
    [Tooltip("Per-vertex color settings applied while each noise-based tunnel segment is generated.")]
    public TunnelColor tunnelColor = new TunnelColor();

    [Header("Damage")]
    [Tooltip("Damage applied to anything that touches the tunnel wall.")]
    [Min(0f)] public float wallContactDamage = 99999999f;

    [Header("Runtime Control")]
    [Tooltip("Runtime multiplier used by scripted moments such as the tutorial tunnel collapse.")]
    [Min(0.01f)] public float runtimeRadiusMultiplier = 1f;

    [Header("Noise")]
    [Tooltip("Noise type: Perlin (smooth, organic) / Worley (distance-based cave-like structure).")]
    public FastNoiseLite.NoiseType noiseType = FastNoiseLite.NoiseType.Perlin;

    [Tooltip("Fractal mode — FBm adds octaves with decreasing amplitude.")]
    public FastNoiseLite.FractalType fractalType = FastNoiseLite.FractalType.FBm;

    [Tooltip("Octave count. 1 = pure noise, 3-5 = richer structure.")]
    [Range(1, 8)] public int octaves = 3;

    [Tooltip("Frequency — how dense noise features are (lower = larger features).")]
    [Min(0.001f)] public float frequency = 0.1f;

    [Tooltip("Amplitude multiplier on the noise applied to the radius.")]
    [Min(0f)] public float noiseStrength = 1f;

    [Tooltip("Seed — different values produce different tunnels.")]
    public int seed = 1337;

    [Header("Path Curving")]
    [Tooltip("Enable horizontal/vertical sway of the tunnel center.")]
    public bool useCurve = true;

    [Tooltip("Frequency of curve sway (independent of main noise frequency).")]
    [Min(0.0001f)] public float curveFrequency = 0.05f;

    [Tooltip("Amplitude of curve sway in world units.")]
    public float curveStrength = 8f;

    [Header("Worley (only when noiseType = Worley)")]
    public FastNoiseLite.CellularDistanceFunction cellularDistance = FastNoiseLite.CellularDistanceFunction.EuclideanSq;
    public FastNoiseLite.CellularReturnType cellularReturn = FastNoiseLite.CellularReturnType.Distance;

    private FastNoiseLite _radiusNoise;
    private FastNoiseLite _curveNoise;
    private Material _runtimeVertexColorMaterial;
    private readonly Dictionary<int, TunnelSegment> _active = new Dictionary<int, TunnelSegment>();
    private readonly Stack<TunnelSegment> _pool = new Stack<TunnelSegment>();
    private int _lastPlayerIndex = int.MinValue;

    public event System.Action<TunnelSegment, int, int> OnSegmentSpawned;

    private float _totalDistance = 0f;
    public float CurrentScrollSpeed { get; set; }
    public static TunnelGenerator Instance { get; private set; }
    private void Awake()
    {
        Instance = this;
        _radiusNoise = BuildRadiusNoise();
        _curveNoise = BuildCurveNoise();
        if (player == null && Camera.main != null) player = Camera.main.transform;
    }

    private void Start() => ForceUpdate();

    private void Update()
    {
        _totalDistance += CurrentScrollSpeed * Time.deltaTime;


        int playerIndex = Mathf.FloorToInt(_totalDistance / segmentLength);
        
        if (playerIndex != _lastPlayerIndex)
        {
            _lastPlayerIndex = playerIndex;
            UpdateActiveSegments(playerIndex);
        }


        transform.position = new Vector3(0, 0, -_totalDistance);
    }
    private FastNoiseLite BuildRadiusNoise()
    {
        var n = new FastNoiseLite(seed);
        n.SetNoiseType(noiseType);
        n.SetFrequency(frequency);
        n.SetFractalType(fractalType);
        n.SetFractalOctaves(octaves);
        n.SetCellularDistanceFunction(cellularDistance);
        n.SetCellularReturnType(cellularReturn);
        return n;
    }

    private FastNoiseLite BuildCurveNoise()
    {
        var n = new FastNoiseLite(seed + 1);
        n.SetNoiseType(FastNoiseLite.NoiseType.Perlin);
        n.SetFrequency(curveFrequency);
        return n;
    }

    private void UpdateActiveSegments(int playerIndex)
    {
        int from = playerIndex - activeSegmentsBehind;
        int to = playerIndex + activeSegmentsAhead;

        var toRemove = new List<int>();
        foreach (var idx in _active.Keys)
            if (idx < from || idx > to) toRemove.Add(idx);

        foreach (var idx in toRemove) DespawnSegment(idx);

        for (int i = from; i <= to; i++)
            if (!_active.ContainsKey(i)) SpawnSegment(i);
    }

    private void SpawnSegment(int index)
    {
        TunnelSegment seg;
        if (_pool.Count > 0)
        {
            seg = _pool.Pop();
            seg.gameObject.SetActive(true);
        }
        else
        {
            seg = CreateSegmentInstance();
        }

        seg.name = $"TunnelSegment_{index}";
        seg.transform.localPosition = new Vector3(0f, 0f, index * segmentLength);
        ApplyTunnelMaterial(seg.GetComponent<MeshRenderer>());
        GenerateSegment(seg, index);

        _active[index] = seg;
        OnSegmentSpawned?.Invoke(seg, index, seed);
    }

    private void GenerateSegment(TunnelSegment seg, int index)
    {
        seg.Generate(
            _radiusNoise,
            globalStartZ:       index * segmentLength,
            radiusCurve:        tunnelRadiusCurve,
            noiseStrength:      noiseStrength,
            rings:              vertexDensity,
            radialSegments:     radialResolution,
            segmentLength:      segmentLength,
            wallContactDamage:  wallContactDamage,
            physicsMaterial: tunnelPhysicsMat,
            tunnelColor: tunnelColor,
            radiusMultiplier: runtimeRadiusMultiplier
        );
    }

    private void DespawnSegment(int index)
    {
        var seg = _active[index];
        seg.gameObject.SetActive(false);
        _pool.Push(seg);
        _active.Remove(index);
    }

    private TunnelSegment CreateSegmentInstance()
    {
        var go = new GameObject("TunnelSegment");
        go.transform.SetParent(transform, worldPositionStays: false);

        go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        ApplyTunnelMaterial(mr);

        return go.AddComponent<TunnelSegment>();
    }

    private void ApplyTunnelMaterial(MeshRenderer renderer)
    {
        if (renderer == null) return;

        Material material = tunnelMaterial;

        if (tunnelColor != null && tunnelColor.enabled && tunnelColor.useVertexColorShader)
        {
            Material vertexColorMaterial = GetVertexColorMaterial();
            if (vertexColorMaterial != null) material = vertexColorMaterial;
        }

        if (material != null) renderer.sharedMaterial = material;
    }

    private Material GetVertexColorMaterial()
    {
        Shader shader = Shader.Find("Custom/VertexColorLit");
        if (shader == null) return null;

        if (_runtimeVertexColorMaterial == null || _runtimeVertexColorMaterial.shader != shader)
        {
            _runtimeVertexColorMaterial = new Material(shader)
            {
                name = "Tunnel Vertex Color Material",
                hideFlags = HideFlags.DontSave
            };
        }

        return _runtimeVertexColorMaterial;
    }

    [ContextMenu("Force Rebuild")]
    public void ForceUpdate()
    {
        var indices = new List<int>(_active.Keys);
        foreach (var idx in indices) DespawnSegment(idx);

        _radiusNoise = BuildRadiusNoise();

        if (player == null) return;

        int playerIndex = Mathf.FloorToInt(player.position.z / segmentLength);
        _lastPlayerIndex = playerIndex;
        UpdateActiveSegments(playerIndex);
    }

    public float GetRadiusAt(float zPosition) => tunnelRadiusCurve.Evaluate(zPosition);

    public void SetRuntimeRadiusMultiplier(float multiplier, bool rebuildActiveSegments = true)
    {
        multiplier = Mathf.Max(0.01f, multiplier);
        if (Mathf.Approximately(runtimeRadiusMultiplier, multiplier)) return;

        runtimeRadiusMultiplier = multiplier;
        if (rebuildActiveSegments) RebuildActiveSegments();
    }

    public void RebuildActiveSegments()
    {
        foreach (var pair in _active)
        {
            GenerateSegment(pair.Value, pair.Key);
        }
    }

    public Vector3 GetCenterAt(float zPosition)
    {
        if (_curveNoise == null) _curveNoise = BuildCurveNoise();
        float xOff = useCurve ? _curveNoise.GetNoise(500f, zPosition) * curveStrength : 0f;
        float yOff = useCurve ? _curveNoise.GetNoise(1500f, zPosition) * curveStrength : 0f;
        return transform.TransformPoint(new Vector3(xOff, yOff, zPosition));
    }

    private void OnDestroy()
    {
        if (_runtimeVertexColorMaterial != null)
        {
            if (Application.isPlaying) Destroy(_runtimeVertexColorMaterial);
            else DestroyImmediate(_runtimeVertexColorMaterial);
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (vertexDensity   < 2)   vertexDensity   = 2;
        if (radialResolution < 3)  radialResolution = 3;
        if (segmentLength  <= 0f)  segmentLength  = 0.1f;
        if (frequency      <= 0f)  frequency      = 0.001f;
        if (curveFrequency <= 0f)  curveFrequency = 0.0001f;
    }
#endif
}

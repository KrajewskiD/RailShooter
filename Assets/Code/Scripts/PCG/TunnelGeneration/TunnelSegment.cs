using UnityEngine;
using UnityEngine.Rendering;
using System.Collections.Generic;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TunnelSegment : MonoBehaviour
{
    private Mesh _mesh;
    private MeshFilter _filter;
    private MeshCollider _collider;

    private List<Vector3> _vertices = new List<Vector3>();
    private List<int> _tris = new List<int>();
    private List<Vector3> _normals = new List<Vector3>();
    private List<Vector2> _uvs = new List<Vector2>();
    private List<Color> _colors = new List<Color>();

    private float[] _cachedCos;
    private float[] _cachedSin;
    private int _cachedSegments = -1;

    private void Awake() => EnsureMesh();

    private void EnsureMesh()
    {
        if (_filter == null) _filter = GetComponent<MeshFilter>();
        if (_filter == null) _filter = gameObject.AddComponent<MeshFilter>();

        if (_collider == null) _collider = GetComponent<MeshCollider>();
        if (_collider == null) _collider = gameObject.AddComponent<MeshCollider>();

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "TunnelMesh" };
            _mesh.indexFormat = IndexFormat.UInt32;
            _filter.sharedMesh = _mesh;
        }
    }

    public void Generate(
        FastNoiseLite noise,
        float globalStartZ,
        AnimationCurve radiusCurve,
        float noiseStrength,
        int rings,
        int radialSegments,
        float segmentLength,
        float wallContactDamage,
        PhysicsMaterial physicsMaterial,
        TunnelColor tunnelColor,
        float radiusMultiplier = 1f)
    {
        EnsureMesh();
        SetLayerTag();

        BuildMesh(_mesh, noise, noiseStrength, rings, radialSegments, segmentLength, globalStartZ, radiusCurve, tunnelColor, radiusMultiplier);

        _collider.cookingOptions = MeshColliderCookingOptions.UseFastMidphase |
                                   MeshColliderCookingOptions.CookForFasterSimulation;

        Physics.BakeMesh(_mesh.GetInstanceID(), false);

        _collider.sharedMesh = null;
        _collider.sharedMesh = _mesh;
        _collider.sharedMaterial = physicsMaterial;

        TerrainObstacle obstacle = GetComponent<TerrainObstacle>() ?? gameObject.AddComponent<TerrainObstacle>();
        obstacle.damageAmount = wallContactDamage;
    }

    private void BuildMesh(Mesh mesh, FastNoiseLite noise, float noiseStrength, int rings, int radialSegments, float segmentLength, float globalStartZ, AnimationCurve radiusCurve, TunnelColor tunnelColor, float radiusMultiplier)
    {
        _vertices.Clear();
        _tris.Clear();
        _normals.Clear();
        _uvs.Clear();
        _colors.Clear();

        int vertexCount = rings * radialSegments;
        bool useTunnelColors = tunnelColor != null && tunnelColor.enabled;

        if (_vertices.Capacity < vertexCount) _vertices.Capacity = vertexCount;
        if (_tris.Capacity < vertexCount * 6) _tris.Capacity = vertexCount * 6;
        if (useTunnelColors && _colors.Capacity < vertexCount) _colors.Capacity = vertexCount;

        float ringSpacing = segmentLength / (rings - 1);

        if (_cachedSegments != radialSegments)
        {
            _cachedCos = new float[radialSegments];
            _cachedSin = new float[radialSegments];
            float angleStep = (Mathf.PI * 2f) / radialSegments;
            for (int s = 0; s < radialSegments; s++)
            {
                _cachedCos[s] = Mathf.Cos(s * angleStep);
                _cachedSin[s] = Mathf.Sin(s * angleStep);
            }
            _cachedSegments = radialSegments;
        }

        for (int r = 0; r < rings; r++)
        {
            float localZ = r * ringSpacing;
            float globalZ = globalStartZ + localZ;

            float currentRadius = radiusCurve.Evaluate(globalZ);

            for (int s = 0; s < radialSegments; s++)
            {
                float cosA = _cachedCos[s];
                float sinA = _cachedSin[s];

                float n = noise.GetNoise(cosA, sinA, globalZ);
                float deformedRadius = Mathf.Max(0.1f, (currentRadius + n * noiseStrength) * radiusMultiplier);

                Vector3 pos = new Vector3(
                    cosA * deformedRadius,
                    sinA * deformedRadius,
                    localZ
                );

                _vertices.Add(pos);
                _normals.Add(new Vector3(-cosA, -sinA, 0f));
                _uvs.Add(new Vector2((float)s / radialSegments, globalZ));

                if (useTunnelColors)
                {
                    _colors.Add(tunnelColor.Evaluate(n, globalZ));
                }
            }
        }

        for (int r = 0; r < rings - 1; r++)
        {
            int rowA = r * radialSegments;
            int rowB = (r + 1) * radialSegments;

            for (int s = 0; s < radialSegments; s++)
            {
                int sNext = (s + 1) % radialSegments;

                int a = rowA + s;
                int b = rowA + sNext;
                int c = rowB + s;
                int d = rowB + sNext;

                _tris.Add(a);
                _tris.Add(c);
                _tris.Add(d);
                _tris.Add(a);
                _tris.Add(d);
                _tris.Add(b);
            }
        }

        mesh.Clear();
        mesh.SetVertices(_vertices);
        mesh.SetTriangles(_tris, 0);
        mesh.SetNormals(_normals);
        mesh.SetUVs(0, _uvs);
        if (useTunnelColors) mesh.SetColors(_colors);
    }

    private void SetLayerTag()
    {
        int tunnelLayerIndex = LayerMask.NameToLayer("Tunnel");
        if (tunnelLayerIndex != -1)
        {
            gameObject.layer = tunnelLayerIndex;
        }

        UnityObjectUtility.TrySetTag(gameObject, "Tunnel");
    }

    private void OnDestroy()
    {
        if (_mesh != null)
        {
            if (Application.isPlaying) Destroy(_mesh);
            else DestroyImmediate(_mesh);
        }
    }
}

using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Jobs;
using System.Collections.Generic;

public class PathMath : MonoBehaviour
{
    public static PathMath Instance;
    public SplineContainer pathSpline;
    public float corridorWidth = 8f;
    public NativeSpline nativeSpline;
    private bool _isNativeSplineAllocated = false;
    private struct PendingDisposal
    {
        public NativeSpline spline;
        public JobHandle dependency;
    }
    private List<PendingDisposal> _disposalQueue = new List<PendingDisposal>();
    private readonly List<JobHandle> _activeSplineReaders = new List<JobHandle>();






    private Vector3[] _splineWaypoints = System.Array.Empty<Vector3>();
    private const float k_WaypointSpacingMeters = 32f;
    private const int k_MaxWaypoints = 1024;
    public IReadOnlyList<Vector3> SplineWaypoints => _splineWaypoints;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }
    private void Start()
    {
        if (pathSpline != null && pathSpline.Spline != null)
        {
            nativeSpline = new NativeSpline(pathSpline.Spline, pathSpline.transform.localToWorldMatrix, Allocator.Persistent);
            _isNativeSplineAllocated = true;
            RebuildWaypointCache();
        }
    }

    private void Update() 
    {
        PruneCompletedSplineReaders();

        for (int i = _disposalQueue.Count - 1; i >= 0; i--)
        {
            if (_disposalQueue[i].dependency.IsCompleted)
            {
                PendingDisposal pending = _disposalQueue[i];
                pending.dependency.Complete();
                pending.spline.Dispose();
                _disposalQueue.RemoveAt(i);
            }
        }
    }

    public void RegisterSplineReader(JobHandle reader)
    {
        _activeSplineReaders.Add(reader);
    }

    public void UpdateNativeSplineAsync(JobHandle activeJobs)
    {
        PruneCompletedSplineReaders();
        JobHandle disposalDependency = CombineSplineReaderDependencies(activeJobs);

        if (_isNativeSplineAllocated)
        {
            _disposalQueue.Add(new PendingDisposal
            {
                spline = nativeSpline,
                dependency = disposalDependency
            });
        }
        nativeSpline = new NativeSpline(pathSpline.Spline, pathSpline.transform.localToWorldMatrix, Allocator.Persistent);
        _isNativeSplineAllocated = true;
        RebuildWaypointCache();
    }






    private void RebuildWaypointCache()
    {
        if (pathSpline == null || pathSpline.Spline == null || pathSpline.Spline.Count < 2)
        {
            _splineWaypoints = System.Array.Empty<Vector3>();
            return;
        }

        float totalLength = pathSpline.Spline.CalculateLength(pathSpline.transform.localToWorldMatrix);
        if (totalLength <= 0.01f)
        {
            _splineWaypoints = System.Array.Empty<Vector3>();
            return;
        }

        int count = Mathf.Clamp(Mathf.CeilToInt(totalLength / k_WaypointSpacingMeters) + 1, 2, k_MaxWaypoints);

        if (_splineWaypoints.Length != count)
        {
            _splineWaypoints = new Vector3[count];
        }

        for (int i = 0; i < count; i++)
        {
            float t = (count == 1) ? 0f : i / (float)(count - 1);
            float3 localPos = pathSpline.Spline.EvaluatePosition(t);
            _splineWaypoints[i] = pathSpline.transform.TransformPoint((Vector3)localPos);
        }
    }


    public float GetMinDistanceXZSqrToSpline(float worldX, float worldZ)
    {
        var waypoints = _splineWaypoints;
        if (waypoints == null || waypoints.Length == 0) return float.MaxValue;

        float minSqr = float.MaxValue;
        for (int i = 0; i < waypoints.Length; i++)
        {
            float dx = waypoints[i].x - worldX;
            float dz = waypoints[i].z - worldZ;
            float sqr = (dx * dx) + (dz * dz);
            if (sqr < minSqr) minSqr = sqr;
        }
        return minSqr;
    }





    public int AppendCorridorSamples(float chunkCenterX, float chunkCenterZ, float radius, ref Unity.Collections.FixedList512Bytes<float3> outSamples)
    {
        var waypoints = _splineWaypoints;
        if (waypoints == null || waypoints.Length == 0) return 0;

        float radiusSq = radius * radius;
        int capacity = outSamples.Capacity;
        int written = 0;

        for (int i = 0; i < waypoints.Length; i++)
        {
            float dx = waypoints[i].x - chunkCenterX;
            float dz = waypoints[i].z - chunkCenterZ;
            float sqr = (dx * dx) + (dz * dz);
            if (sqr > radiusSq) continue;

            if (outSamples.Length < capacity)
            {
                outSamples.Add(new float3(waypoints[i].x, waypoints[i].y, waypoints[i].z));
                written++;
            }
            else
            {



                break;
            }
        }
        return written;
    }

    private void OnDestroy()
    {
        CompleteAllSplineReaders();

        if (_isNativeSplineAllocated) nativeSpline.Dispose();
        foreach (var item in _disposalQueue)
        {
            item.dependency.Complete();
            item.spline.Dispose();
        }
        _disposalQueue.Clear();

        if (Instance == this) Instance = null;
    }

    private void PruneCompletedSplineReaders()
    {
        for (int i = _activeSplineReaders.Count - 1; i >= 0; i--)
        {
            if (_activeSplineReaders[i].IsCompleted)
            {
                _activeSplineReaders[i].Complete();
                _activeSplineReaders.RemoveAt(i);
            }
        }
    }

    private JobHandle CombineSplineReaderDependencies(JobHandle extraDependency)
    {
        JobHandle combined = extraDependency;
        for (int i = 0; i < _activeSplineReaders.Count; i++)
        {
            combined = JobHandle.CombineDependencies(combined, _activeSplineReaders[i]);
        }
        return combined;
    }

    private void CompleteAllSplineReaders()
    {
        for (int i = 0; i < _activeSplineReaders.Count; i++)
        {
            _activeSplineReaders[i].Complete();
        }
        _activeSplineReaders.Clear();
    }
    public PathSample GetClosestSample(float worldX, float worldZ)
    {
        Vector3 worldPoint = new Vector3(worldX, 0f, worldZ);
        float3 localPoint = pathSpline.transform.InverseTransformPoint(worldPoint);
        
        SplineUtility.GetNearestPoint(nativeSpline, localPoint, out float3 localNearest, out float t);

        Vector3 worldNearest = pathSpline.transform.TransformPoint(localNearest);
        
        Vector3 rawTangent = (Vector3)nativeSpline.EvaluateTangent(t);
        Vector3 tangent;

        if (rawTangent.sqrMagnitude > 1e-6f)
        {
            tangent = rawTangent.normalized;
        }
        else
        {
            tangent = Vector3.forward; 
        }

        float dx = worldX - worldNearest.x;
        float dz = worldZ - worldNearest.z;
        float dist2D = Mathf.Sqrt(dx * dx + dz * dz);

        return new PathSample
        {
            position = worldNearest,
            tangent = tangent,
            distance = dist2D,
            t = t
        };
    }

    public PathSample GetSampleAtDistance(float distanceInMeters)
    {
        if (pathSpline == null || pathSpline.Spline == null) return default;

        float totalLength = pathSpline.Spline.CalculateLength(pathSpline.transform.localToWorldMatrix);
        
        if (totalLength <= 0.01f)
        {
            return new PathSample
            {
                position = pathSpline.transform.position,
                tangent = Vector3.forward,
                t = 0f
            };
        }

        float clampedDistance = Mathf.Clamp(distanceInMeters, 0f, totalLength);
        float t = pathSpline.Spline.ConvertIndexUnit(clampedDistance, PathIndexUnit.Distance, PathIndexUnit.Normalized);

        float3 pos, tan, up;
        pathSpline.Evaluate(t, out pos, out tan, out up);

        return new PathSample
        {
            position = pathSpline.transform.TransformPoint(pos),
            tangent = pathSpline.transform.TransformDirection(math.normalize(tan)),
            t = t,
        };
    }

    public void GetRailOrientation(float t, out Vector3 up, out Vector3 right)
    {
        pathSpline.Evaluate(t, out _, out float3 tan, out float3 uDir);
        Vector3 fwd = pathSpline.transform.TransformDirection(math.normalize(tan));
        up = pathSpline.transform.TransformDirection(math.normalize(uDir));
        right = Vector3.Cross(up, fwd).normalized;
    }

    public Vector3 EvaluateAt(float t)
    {
        return pathSpline.EvaluatePosition(t);
    }

}

public struct PathSample
{
    public Vector3 position;
    public Vector3 tangent;
    public float distance;
    public float t;
}

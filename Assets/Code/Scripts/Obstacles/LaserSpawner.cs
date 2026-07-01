using UnityEngine;
using System.Collections.Generic;

public class LaserSpawner : MonoBehaviour
{
    [Header("References")]
    public GameObject laserPrefab;
    public LaserGridSettings settings;
    private Transform railTop, railBot, laserContainer;
    private List<Transform> laserParentObjects = new List<Transform>();
    private List<Transform> beamPivots = new List<Transform>();
    private List<float> baseLocalXPositions = new List<float>();

    private float centerY, distanceY, maxOffset;
    private bool isInitialized = false;

    private const float LASER_SAFETY_MARGIN = 0.1f;

    public void Initialize()
    {
        railTop = transform.Find("Rail_Top");
        railBot = transform.Find("Rail_Bot");
        laserContainer = transform.Find("LaserContainer");

        if (railTop == null || railBot == null || laserContainer == null) return;

        float halfDistance = settings.railsOffsetY / 2f;
        railTop.localPosition = new Vector3(0, halfDistance, 0);
        railBot.localPosition = new Vector3(0, -halfDistance, 0);

        foreach (Transform child in laserContainer) Destroy(child.gameObject);
        SpawnLasers();

        if (baseLocalXPositions.Count > 0)
        {
            float firstLaserX = baseLocalXPositions[0];
            float lastLaserX = baseLocalXPositions[baseLocalXPositions.Count - 1];

            float laserDistance = Mathf.Abs(lastLaserX - firstLaserX);
            if (settings.laserCount <= 1) laserDistance = 0.2f;

            float desiredLocalWidth = laserDistance + (maxOffset * 2f) + (LASER_SAFETY_MARGIN * 2f);

            float topMeshWidth = GetMeshWidth(railTop);
            float botMeshWidth = GetMeshWidth(railBot);

            railTop.localScale = new Vector3(desiredLocalWidth / topMeshWidth, railTop.localScale.y, railTop.localScale.z);
            railBot.localScale = new Vector3(desiredLocalWidth / botMeshWidth, railBot.localScale.y, railBot.localScale.z);
        }

        isInitialized = true;
    }

    private float GetMeshWidth(Transform rail)
    {
        MeshFilter mf = rail.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            float width = mf.sharedMesh.bounds.size.x;
            if (width > 0.001f) return width;
        }
        return 1f;
    }

    void SpawnLasers()
    {
        laserParentObjects.Clear();
        beamPivots.Clear();
        baseLocalXPositions.Clear();

        float tY = railTop.localPosition.y;
        float bY = railBot.localPosition.y;
        centerY = (tY + bY) / 2f;
        distanceY = Mathf.Abs(tY - bY);

        float limitX = 1f - settings.sideMargin;
        if (limitX < 0) limitX = 0;

        float startX = -limitX;
        float endX = limitX;

        float step = (settings.laserCount > 1) ? (endX - startX) / (settings.laserCount - 1) : 0f;

        maxOffset = settings.isMovement && settings.laserCount > 1 ? (step / 2f) * settings.movementFreedom : 0f;

        for (int i = 0; i < settings.laserCount; i++)
        {
            float posX = (settings.laserCount > 1) ? startX + (i * step) : 0f;

            GameObject laserObj = Instantiate(laserPrefab, laserContainer, false);
            laserObj.transform.localPosition = new Vector3(posX, centerY, 0f);

            Transform recTop = laserObj.transform.Find("LaserReceiver_Top");
            Transform recBot = laserObj.transform.Find("LaserReceiver_Bot");
            if (recTop != null) recTop.localPosition = new Vector3(recTop.localPosition.x, tY - centerY, recTop.localPosition.z);
            if (recBot != null) recBot.localPosition = new Vector3(recBot.localPosition.x, bY - centerY, recBot.localPosition.z);

            laserParentObjects.Add(laserObj.transform);
            baseLocalXPositions.Add(posX);

            SetupBeams(laserObj, i);
        }
        ApplyRotation();
    }

    private void SetupBeams(GameObject laserObj, int i)
    {
        if (settings.directionMode == LaserDirection.Double)
        {
            Transform beam1 = laserObj.transform.Find("Beam");
            GameObject beam2Obj = Instantiate(beam1.gameObject, laserObj.transform, false);
            ApplyPivot(laserObj, beam1, "LaserReceiver_Bot", true, i);
            ApplyPivot(laserObj, beam2Obj.transform, "LaserReceiver_Top", false, i);
        }
        else
        {
            bool isGoingUp = (settings.directionMode != LaserDirection.TopToBottom);
            if (settings.directionMode == LaserDirection.Alternate)
                isGoingUp = ((i / Mathf.Max(1, settings.sequenceLength)) % 2 == 0);

            Transform beam = laserObj.transform.Find("Beam");
            ApplyPivot(laserObj, beam, isGoingUp ? "LaserReceiver_Bot" : "LaserReceiver_Top", isGoingUp, i);
        }
    }

    void ApplyRotation()
    {
        if (settings.isHorizontal)
        {
            transform.Rotate(0, 0, 90);
        }
    }

    void ApplyPivot(GameObject parent, Transform beam, string anchorName, bool isUp, int index)
    {
        Transform anchor = parent.transform.Find(anchorName);
        if (anchor == null || beam == null) return;
        beam.localScale = new Vector3(beam.localScale.x, 1f, beam.localScale.z);
        GameObject pivotObj = new GameObject("Pivot_" + index + (isUp ? "_Up" : "_Down"));

        pivotObj.transform.SetParent(parent.transform, false);
        pivotObj.transform.localPosition = anchor.localPosition;

        beam.SetParent(pivotObj.transform, false);
        beam.localPosition = new Vector3(0f, isUp ? 0.5f : -0.5f, 0f);
        beamPivots.Add(pivotObj.transform);
    }

    void Update()
    {
        if (!isInitialized || settings == null) return;
        HandleRoll();
        HandleMovement();
        HandleSequentialScaling();
    }

    void HandleRoll()
    {
        if (settings.isRoll)
        {
            transform.Rotate(0, 0, settings.rollSpeed * Time.deltaTime);
        }
    }

    void HandleMovement()
    {
        if (!settings.isMovement || laserParentObjects.Count == 0) return;
        float pingPong = Mathf.PingPong(Time.time * settings.moveSpeed, 1f);
        float currentOffset = Mathf.Lerp(-maxOffset, maxOffset, pingPong);
        for (int i = 0; i < laserParentObjects.Count; i++)
            laserParentObjects[i].localPosition = new Vector3(baseLocalXPositions[i] + currentOffset, centerY, 0f);
    }

    void HandleSequentialScaling()
    {
        if (beamPivots.Count == 0) return;

        if (settings.alwaysLight || !settings.isBlinking)
        {
            float maxS = (settings.directionMode == LaserDirection.Double) ? 0.5f : 1.0f;
            foreach (var pivot in beamPivots)
            {
                if (pivot != null)
                    pivot.localScale = new Vector3(1f, maxS * distanceY, 1f);
            }
            return;
        }

        float timePerGroup = 2f + settings.pauseDuration + settings.waveDelay;
        int totalGroups = CalculateTotalGroups();
        float totalSystemCycle = totalGroups * timePerGroup;

        for (int i = 0; i < beamPivots.Count; i++)
        {
            if (beamPivots[i] == null) continue;
            int waveGroup = CalculateWaveGroup(i);

            float groupOffset = waveGroup * timePerGroup;
            float t = (Time.time * settings.cycleSpeed - groupOffset) % totalSystemCycle;
            if (t < 0) t += totalSystemCycle;

            float rawScale = CalculateRawScale(t);
            float animationAlpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(rawScale));
            float finalScale = settings.invertWave ? (1f - animationAlpha) : animationAlpha;

            float maxS = (settings.directionMode == LaserDirection.Double) ? 0.5f : 1.0f;
            beamPivots[i].localScale = new Vector3(1f, finalScale * maxS * distanceY, 1f);
        }
    }

    int CalculateTotalGroups()
    {
        if (settings.waveStyle == WaveStyle.Linear) return Mathf.CeilToInt((float)settings.laserCount / Mathf.Max(1, settings.lasersPerWave));
        if (settings.waveStyle == WaveStyle.OneByOne) return settings.laserCount;
        return Mathf.CeilToInt((float)settings.laserCount / 2f);
    }

    int CalculateWaveGroup(int i)
    {
        int segmentIndex = (settings.directionMode == LaserDirection.Double) ? i / 2 : i;
        switch (settings.waveStyle)
        {
            case WaveStyle.OneByOne: return segmentIndex;
            case WaveStyle.Linear: return segmentIndex / Mathf.Max(1, settings.lasersPerWave);
            case WaveStyle.OutsideIn: return segmentIndex >= settings.laserCount / 2 ? (settings.laserCount - 1) - segmentIndex : segmentIndex;
            case WaveStyle.InsideOut:
                if (settings.laserCount % 2 == 0) return segmentIndex >= settings.laserCount / 2 ? segmentIndex - (settings.laserCount / 2) : ((settings.laserCount / 2) - 1) - segmentIndex;
                return Mathf.Abs(segmentIndex - (settings.laserCount - 1) / 2);
            default: return 0;
        }
    }

    float CalculateRawScale(float t)
    {
        if (t < 1f) return t;
        if (t < 1f + settings.pauseDuration) return 1f;
        if (t < 2f + settings.pauseDuration) return 1f - (t - (1f + settings.pauseDuration));
        return 0f;
    }
}

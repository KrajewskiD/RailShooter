using UnityEngine;

public class RunProgressTracker : MonoBehaviour
{
    private static RunProgressTracker _instance;

    [SerializeField] private SplineGenerator splineGenerator;
    [SerializeField] private float sampleInterval = 0.5f;

    private float _timer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject(nameof(RunProgressTracker));
        DontDestroyOnLoad(go);
        go.AddComponent<RunProgressTracker>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        if (splineGenerator == null)
            splineGenerator = FindFirstObjectByType<SplineGenerator>();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private void Update()
    {
        if (GameStateManager.Instance != null && GameStateManager.Instance.CurrentState != GameState.InGame)
            return;

        _timer += Time.deltaTime;
        if (_timer < sampleInterval) return;
        _timer = 0f;

        if (splineGenerator == null)
            splineGenerator = FindFirstObjectByType<SplineGenerator>();

        if (splineGenerator != null &&
            splineGenerator.TryGetPlayerArc(out float arc) &&
            PlayerProgressManager.Instance != null)
        {
            PlayerProgressManager.Instance.SetRunDistanceMeters(arc);
        }
    }
}

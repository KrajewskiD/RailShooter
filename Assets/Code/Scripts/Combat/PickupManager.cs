using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(1100)]
public class PickupManager : MonoBehaviour
{
    private static PickupManager _instance;
    private static bool _quitting;

    public static PickupManager Instance => _instance;

    public static PickupManager GetOrCreate()
    {
        if (_instance != null) return _instance;
        if (_quitting) return null;
        _instance = FindAnyObjectByType<PickupManager>();
        if (_instance != null)
        {
            _instance.EnsureRuntimeState();
            return _instance;
        }

        var go = new GameObject("[PickupManager]");
        _instance = go.AddComponent<PickupManager>();
        return _instance;
    }

    [Tooltip("Tag used to resolve the player transform when not set explicitly.")]
    [SerializeField] private string playerTag = "Player";
    [Tooltip("How often to retry resolving the player when missing (seconds).")]
    [SerializeField] private float playerResolveInterval = 0.5f;

    private readonly List<Pickup> _pickups = new List<Pickup>(256);
    private readonly HashSet<Pickup> _pickupSet = new HashSet<Pickup>();
    private Transform _player;
    private IFlightVehicle _vehicle;
    private float _nextResolveTime;

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        EnsureRuntimeState();
    }

    private void OnEnable()
    {
        if (_instance == null) _instance = this;
        EnsureRuntimeState();
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        _pickups.Clear();
        _pickupSet.Clear();
    }

    private void OnApplicationQuit()
    {
        _quitting = true;
    }

    public void Register(Pickup p)
    {
        if (p == null) return;
        if (_pickupSet.Add(p)) _pickups.Add(p);
    }

    public void Unregister(Pickup p)
    {
        if (p == null) return;
        if (!_pickupSet.Remove(p)) return;

        int index = _pickups.IndexOf(p);
        if (index >= 0) RemovePickupAt(index);
    }

    public void SetPlayer(Transform t)
    {
        _player = t;
        _vehicle = PlayerReferenceResolver.ResolveVehicle(t);
    }

    public void SetPlayer(IFlightVehicle vehicle)
    {
        _vehicle = PlayerReferenceResolver.IsVehicleAlive(vehicle) ? vehicle : null;
        _player = _vehicle != null ? _vehicle.VehicleTransform : null;
    }

    private void LateUpdate()
    {
        if (_instance == null) _instance = this;
        PruneInvalidPickups();

        if (_player == null || !PlayerReferenceResolver.IsVehicleAlive(_vehicle))
        {
            _vehicle = null;
            if (_player == null) _nextResolveTime = 0f;
        }

        if (_player == null && Time.time >= _nextResolveTime)
        {
            _nextResolveTime = Time.time + playerResolveInterval;
            Transform player = PlayerReferenceResolver.ResolvePlayerTransform(null, playerTag);
            if (player != null)
            {
                IFlightVehicle vehicle = PlayerReferenceResolver.ResolveVehicle(player);
                if (vehicle != null) SetPlayer(vehicle);
                else SetPlayer(player);
            }
        }

        if (!PlayerReferenceResolver.IsVehicleAlive(_vehicle) && _player != null)
        {
            _vehicle = PlayerReferenceResolver.ResolveVehicle(_player);
        }

        if (PlayerReferenceResolver.IsVehicleAlive(_vehicle))
        {
            _player = _vehicle.VehicleTransform;
        }

        Transform pickupTarget = GetPickupTarget();
        bool hasPlayer = _player != null || pickupTarget != null;
        Vector3 playerPos = pickupTarget != null
            ? pickupTarget.position
            : hasPlayer ? _player.position : Vector3.zero;

        float levelSpeed = PlayerReferenceResolver.ResolveLevelSpeed();
        float forwardSpeed = PlayerReferenceResolver.IsVehicleAlive(_vehicle)
            ? Mathf.Max(levelSpeed, _vehicle.CurrentForwardSpeed)
            : levelSpeed;

        float dt = Time.deltaTime;
        float t = Time.time;

        for (int i = 0; i < _pickups.Count; i++)
        {
            var p = _pickups[i];
            if (p == null) continue;
            p.Tick(dt, t, hasPlayer, playerPos, _player, pickupTarget, forwardSpeed);
        }
    }

    private Transform GetPickupTarget()
    {
        if (!PlayerReferenceResolver.IsVehicleAlive(_vehicle)) return _player;
        return _vehicle.PickupTargetTransform != null ? _vehicle.PickupTargetTransform : _player;
    }

    private void EnsureRuntimeState()
    {
        _quitting = false;
        PruneInvalidPickups();

        if (!PlayerReferenceResolver.IsVehicleAlive(_vehicle)) _vehicle = null;
        if (_player == null) _nextResolveTime = 0f;
    }

    private void PruneInvalidPickups()
    {
        for (int i = _pickups.Count - 1; i >= 0; i--)
        {
            Pickup pickup = _pickups[i];
            if (pickup == null || !_pickups[i].isActiveAndEnabled)
            {
                RemovePickupAt(i);
            }
        }
    }

    private void RemovePickupAt(int index)
    {
        Pickup pickup = _pickups[index];
        _pickupSet.Remove(pickup);

        int last = _pickups.Count - 1;
        _pickups[index] = _pickups[last];
        _pickups.RemoveAt(last);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        _quitting = false;
    }
}

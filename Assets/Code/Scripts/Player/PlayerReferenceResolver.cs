using UnityEngine;

public static class PlayerReferenceResolver
{
    public const string DefaultPlayerTag = "Player";

    public static Transform ResolvePlayerTransform(Transform cached = null, string playerTag = DefaultPlayerTag)
    {
        if (cached != null)
        {
            return cached;
        }

        if (!string.IsNullOrEmpty(playerTag))
        {
            GameObject tagged = GameObject.FindGameObjectWithTag(playerTag);
            if (tagged != null)
            {
                return tagged.transform;
            }
        }

        SplinePlayerController spline = Object.FindFirstObjectByType<SplinePlayerController>();
        if (spline != null)
        {
            return spline.transform;
        }

        PlayerController rail = Object.FindFirstObjectByType<PlayerController>();
        if (rail != null)
        {
            return rail.transform;
        }

        IFlightVehicle vehicle = FindAnyVehicle();
        return vehicle != null ? vehicle.VehicleTransform : null;
    }

    public static IFlightVehicle ResolveVehicle(Transform source)
    {
        if (source == null)
        {
            return null;
        }

        IFlightVehicle vehicle = source.GetComponent<IFlightVehicle>();
        if (vehicle != null)
        {
            return vehicle;
        }

        vehicle = source.GetComponentInParent<IFlightVehicle>();
        if (vehicle != null)
        {
            return vehicle;
        }

        return source.GetComponentInChildren<IFlightVehicle>();
    }

    public static IFlightVehicle FindAnyVehicle()
    {
        MonoBehaviour[] behaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IFlightVehicle vehicle)
            {
                return vehicle;
            }
        }

        return null;
    }

    public static bool IsVehicleAlive(IFlightVehicle vehicle)
    {
        if (vehicle == null)
        {
            return false;
        }

        return !(vehicle is Object unityObject) || unityObject != null;
    }

    public static Transform ResolvePickupTarget(Transform player)
    {
        if (player == null)
        {
            return null;
        }

        IFlightVehicle vehicle = ResolveVehicle(player);
        return vehicle != null && vehicle.PickupTargetTransform != null
            ? vehicle.PickupTargetTransform
            : player;
    }

    public static float ResolveForwardSpeed(Transform player)
    {
        float levelSpeed = ResolveLevelSpeed();
        IFlightVehicle vehicle = ResolveVehicle(player);
        return vehicle != null ? Mathf.Max(levelSpeed, vehicle.CurrentForwardSpeed) : levelSpeed;
    }

    public static float ResolveLevelSpeed()
    {
        if (LevelManager.Instance == null)
        {
            return 0f;
        }

        return LevelManager.Instance.CurrentTargetSpeed > 0f
            ? LevelManager.Instance.CurrentTargetSpeed
            : LevelManager.Instance.baseSpeed;
    }

    public static T FindOnPlayer<T>(Transform playerTransform) where T : Component
    {
        if (playerTransform == null)
        {
            return null;
        }

        T component = playerTransform.GetComponent<T>();
        if (component != null)
        {
            return component;
        }

        component = playerTransform.GetComponentInChildren<T>();
        if (component != null)
        {
            return component;
        }

        return playerTransform.GetComponentInParent<T>();
    }
}

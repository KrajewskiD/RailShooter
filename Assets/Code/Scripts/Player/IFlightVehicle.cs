using UnityEngine;

public interface IFlightVehicle
{
    float CurrentForwardSpeed { get; }

    Vector2 SmoothMousePos { get; }
    Transform VehicleTransform { get; }
    Transform PickupTargetTransform { get; }
    bool ShowMouseCursor { get; } 
    Vector3 AimTarget { get; }
}

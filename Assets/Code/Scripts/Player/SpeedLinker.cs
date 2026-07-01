using UnityEngine;

public class SpeedLinker : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("Optional explicit source. Existing scene links use this field; if empty, the linker finds the active player automatically.")]
    [SerializeField] private MonoBehaviour playerController;

    private IFlightVehicle _playerController;
    private IScrollable _target;

    void Awake()
    {
        _target = GetComponent<IScrollable>();
        ResolvePlayerController();
    }

    void Update()
    {
        if (_playerController == null)
        {
            ResolvePlayerController();
        }

        if (_playerController != null && _target != null)
        {
            _target.CurrentScrollSpeed = _playerController.CurrentForwardSpeed;
        }
    }

    private void ResolvePlayerController()
    {
        _playerController = playerController as IFlightVehicle;
        if (_playerController != null) return;

        Transform player = PlayerReferenceResolver.ResolvePlayerTransform();
        if (player != null)
        {
            _playerController = PlayerReferenceResolver.ResolveVehicle(player);
            if (_playerController != null) return;
        }

        _playerController = PlayerReferenceResolver.FindAnyVehicle();
    }
}

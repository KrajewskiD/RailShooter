using UnityEngine;
using UnityEngine.VFX;

[RequireComponent(typeof(VisualEffect))]
public class VFXTrailSpeedController : MonoBehaviour
{
    [SerializeField] private MonoBehaviour playerControllerSource;
    
    public string speedPropertyName = "TunnelSpeed";
    
    private VisualEffect _vfx;
    private IFlightVehicle _playerController;

    void Awake()
    {
        _vfx = GetComponent<VisualEffect>();
        ResolvePlayerController();
    }

    private void OnEnable()
    {
        ResolvePlayerController();
    }

    void LateUpdate()
    {
        if (_vfx == null) return;

        if (_playerController == null)
        {
            ResolvePlayerController();
        }

        if (_playerController != null && _vfx.HasFloat(speedPropertyName))
        {
            _vfx.SetFloat(speedPropertyName, _playerController.CurrentForwardSpeed);
        }
    }

    private void ResolvePlayerController()
    {
        _playerController = playerControllerSource as IFlightVehicle;
        if (_playerController != null) return;

        _playerController = GetComponentInParent<IFlightVehicle>();
        if (_playerController != null) return;

        _playerController = PlayerReferenceResolver.FindAnyVehicle();
    }
}

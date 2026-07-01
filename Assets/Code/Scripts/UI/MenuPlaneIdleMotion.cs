using UnityEngine;

public class MenuPlaneIdleMotion : MonoBehaviour
{
    [SerializeField] private float verticalAmplitude = 0.18f;
    [SerializeField] private float cycleDuration = 6f;
    [SerializeField] private float rightYawDegrees = 4f;
    [SerializeField] private float rightRollDegrees = -7f;

    private Vector3 _baseLocalPosition;
    private Quaternion _baseLocalRotation;
    private float _phaseOffset;

    private void OnEnable()
    {
        _baseLocalPosition = transform.localPosition;
        _baseLocalRotation = transform.localRotation;
        _phaseOffset = Random.Range(0f, Mathf.PI * 2f);
    }

    private void LateUpdate()
    {
        float duration = Mathf.Max(0.1f, cycleDuration);
        float wave = Mathf.Sin((Time.time / duration) * Mathf.PI * 2f + _phaseOffset);
        float normalized = (wave + 1f) * 0.5f;

        Vector3 position = _baseLocalPosition;
        position.y += wave * verticalAmplitude;
        transform.localPosition = position;

        Quaternion rightLean = Quaternion.Euler(
            0f,
            rightYawDegrees * normalized,
            rightRollDegrees * normalized
        );
        transform.localRotation = _baseLocalRotation * rightLean;
    }
}

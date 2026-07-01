using UnityEngine;
using UnityEngine.UI;

public class XPBarUI : MonoBehaviour
{
    public Slider xpSlider;

    private void OnEnable()
    {
        ProgressionManager.OnXPChanged += UpdateXPBar;
    }

    private void OnDisable()
    {
        ProgressionManager.OnXPChanged -= UpdateXPBar;
    }

    private void UpdateXPBar(float current, float max)
    {
        xpSlider.value = (current / max) * 100f;
    }
}
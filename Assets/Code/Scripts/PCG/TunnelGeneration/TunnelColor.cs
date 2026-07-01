using UnityEngine;

[System.Serializable]
public class TunnelColor
{
    [Header("Color Source")]
    public bool enabled = true;

    [Tooltip("Gradient sampled by the tunnel deformation noise. Left side is low noise, right side is high noise.")]
    public Gradient noiseGradient = CreateDefaultNoiseGradient();

    [Tooltip("Pushes sampled noise values away from or toward the gradient center.")]
    [Range(0.1f, 5f)] public float contrast = 1.2f;

    [Tooltip("Moves the sampled point along the gradient after contrast is applied.")]
    [Range(-1f, 1f)] public float offset = 0f;

    [Header("Terrain Style")]
    [Tooltip("Extra saturation for the terrain-inspired tunnel palette.")]
    [Range(0f, 1f)] public float saturationBoost = 0.35f;

    [Tooltip("Small brightness lift so vertex colors stay readable in the lit tunnel shader.")]
    [Range(0f, 1f)] public float brightnessBoost = 0.12f;

    [Header("Depth Variation")]
    public bool useDepthVariation = true;

    [Tooltip("Optional color pass repeated along tunnel Z.")]
    public Gradient depthGradient = CreateDefaultDepthGradient();

    [Tooltip("How much the depth gradient blends into the noise color.")]
    [Range(0f, 1f)] public float depthBlend = 0.25f;

    [Tooltip("How often the depth gradient repeats along the tunnel.")]
    [Min(0f)] public float depthFrequency = 0.015f;

    [Header("Material")]
    [Tooltip("Uses the existing Custom/VertexColorLit shader so mesh vertex colors are visible in game.")]
    public bool useVertexColorShader = true;

    public Color Evaluate(float noiseValue, float globalZ)
    {
        float sample = Mathf.Clamp01((noiseValue + 1f) * 0.5f);
        sample = Mathf.Clamp01(((sample - 0.5f) * contrast) + 0.5f + offset);

        Color color = noiseGradient.Evaluate(sample);

        if (useDepthVariation && depthBlend > 0f && depthFrequency > 0f)
        {
            float depthSample = Mathf.Repeat(globalZ * depthFrequency, 1f);
            Color depthColor = depthGradient.Evaluate(depthSample);
            color = Color.Lerp(color, depthColor, depthBlend);
        }

        color = BoostSaturation(color, saturationBoost);
        color.r = Mathf.Clamp01(color.r + brightnessBoost);
        color.g = Mathf.Clamp01(color.g + brightnessBoost);
        color.b = Mathf.Clamp01(color.b + brightnessBoost);
        color.a = 1f;
        return color;
    }

    private static Color BoostSaturation(Color color, float strength)
    {
        if (strength <= 0f) return color;

        float gray = color.r * 0.299f + color.g * 0.587f + color.b * 0.114f;
        return new Color(
            Mathf.Clamp01(Mathf.Lerp(gray, color.r, 1f + strength)),
            Mathf.Clamp01(Mathf.Lerp(gray, color.g, 1f + strength)),
            Mathf.Clamp01(Mathf.Lerp(gray, color.b, 1f + strength)),
            color.a
        );
    }

    private static Gradient CreateDefaultNoiseGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color32(60, 165, 220, 255), 0f),
                new GradientColorKey(new Color32(165, 215, 175, 255), 0.18f),
                new GradientColorKey(new Color32(55, 165, 80, 255), 0.36f),
                new GradientColorKey(new Color32(170, 230, 90, 255), 0.52f),
                new GradientColorKey(new Color32(245, 225, 110, 255), 0.68f),
                new GradientColorKey(new Color32(255, 175, 90, 255), 0.84f),
                new GradientColorKey(new Color32(248, 252, 255, 255), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        return gradient;
    }

    private static Gradient CreateDefaultDepthGradient()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color32(76, 0, 130, 255), 0f),
                new GradientColorKey(new Color32(30, 80, 220, 255), 0.16f),
                new GradientColorKey(new Color32(105, 205, 235, 255), 0.32f),
                new GradientColorKey(new Color32(70, 185, 95, 255), 0.50f),
                new GradientColorKey(new Color32(245, 225, 80, 255), 0.64f),
                new GradientColorKey(new Color32(245, 135, 45, 255), 0.78f),
                new GradientColorKey(new Color32(215, 40, 40, 255), 0.90f),
                new GradientColorKey(new Color32(105, 0, 35, 255), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        return gradient;
    }
}

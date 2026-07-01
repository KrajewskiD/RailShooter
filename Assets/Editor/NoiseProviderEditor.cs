using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(NoiseProvider))]
public class NoiseProviderEditor : Editor
{
    private bool baseFoldout = true;
    private bool tempFoldout = false;
    private bool moistureFoldout = false;
    private bool biomeHeightsFoldout = false;
    private readonly List<bool> baseLayerFoldouts = new List<bool>();

    public override void OnInspectorGUI()
    {
        NoiseProvider noise = (NoiseProvider)target;

        EditorGUILayout.LabelField("Execution", EditorStyles.boldLabel);
        noise.useBurst = EditorGUILayout.Toggle("Use Burst", noise.useBurst);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Seed", EditorStyles.boldLabel);
        noise.seed = EditorGUILayout.IntField("Seed", noise.seed);

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Noise Sampling", EditorStyles.boldLabel);
        noise.hashMode = (NoiseHashMode)EditorGUILayout.EnumPopup("Hash Mode", noise.hashMode);

        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("Terrain Data (Data-Driven Generation)", EditorStyles.boldLabel);


        baseFoldout = EditorGUILayout.Foldout(baseFoldout, "Base Terrain", true);
        if (baseFoldout)
        {
            EditorGUI.indentLevel++;
            noise.baseEnabled = EditorGUILayout.Toggle("Enabled", noise.baseEnabled);
            using (new EditorGUI.DisabledScope(!noise.baseEnabled))
            {
                noise.baseAmplitude = EditorGUILayout.FloatField("Amplitude", noise.baseAmplitude);

                EditorGUILayout.LabelField("Height Shape Curve", EditorStyles.miniBoldLabel);
                EditorGUILayout.HelpBox("Mapuje baseHeight ∈ [-1, 1] na shaped ∈ [-1, 1]. Wynik mnożony przez Amplitude.", MessageType.None);
                noise.heightShapeCurve = EditorGUILayout.CurveField(
                    "Curve",
                    noise.heightShapeCurve,
                    Color.cyan,
                    new Rect(-1f, -1f, 2f, 2f)
                );

                DrawBaseNoiseStack(noise);
            }
            EditorGUI.indentLevel--;
        }


        tempFoldout = EditorGUILayout.Foldout(tempFoldout, "Temperature", true);
        if (tempFoldout)
        {
            EditorGUI.indentLevel++;
            noise.temperatureEnabled = EditorGUILayout.Toggle("Enabled", noise.temperatureEnabled);
            using (new EditorGUI.DisabledScope(!noise.temperatureEnabled))
            {
                noise.temperatureType      = (NoiseProvider.NoiseType)EditorGUILayout.EnumPopup("Type", noise.temperatureType);
                noise.temperatureFractal   = (NoiseProvider.FractalType)EditorGUILayout.EnumPopup("Fractal", noise.temperatureFractal);
                noise.temperatureFrequency = EditorGUILayout.FloatField("Frequency", noise.temperatureFrequency);

                if (noise.temperatureFractal != NoiseProvider.FractalType.None)
                {
                    noise.temperatureOctaves = EditorGUILayout.IntSlider("Octaves", noise.temperatureOctaves, 1, 8);
                    noise.temperatureLacunarity = EditorGUILayout.Slider("Lacunarity", noise.temperatureLacunarity, 1.0f, 4.0f);
                    noise.temperaturePersistence = EditorGUILayout.Slider("Persistence (Gain)", noise.temperaturePersistence, 0.0f, 1.0f);
                }

                if (noise.temperatureType == NoiseProvider.NoiseType.Worley)
                {
                    EditorGUILayout.LabelField("Worley", EditorStyles.miniBoldLabel);
                    noise.temperatureCellDistance = (CellularDistance)EditorGUILayout.EnumPopup("Distance Metric", noise.temperatureCellDistance);
                    noise.temperatureCellReturn   = WorleyReturnDrawer.Popup("Return Type", noise.temperatureCellReturn);
                    noise.temperatureJitter       = EditorGUILayout.Slider("Jitter", noise.temperatureJitter, 0f, 1f);
                }
            }
            EditorGUI.indentLevel--;
        }


        moistureFoldout = EditorGUILayout.Foldout(moistureFoldout, "Moisture", true);
        if (moistureFoldout)
        {
            EditorGUI.indentLevel++;
            noise.moistureEnabled = EditorGUILayout.Toggle("Enabled", noise.moistureEnabled);
            using (new EditorGUI.DisabledScope(!noise.moistureEnabled))
            {
                noise.moistureType      = (NoiseProvider.NoiseType)EditorGUILayout.EnumPopup("Type", noise.moistureType);
                noise.moistureFractal   = (NoiseProvider.FractalType)EditorGUILayout.EnumPopup("Fractal", noise.moistureFractal);
                noise.moistureFrequency = EditorGUILayout.FloatField("Frequency", noise.moistureFrequency);

                if (noise.moistureFractal != NoiseProvider.FractalType.None)
                {
                    noise.moistureOctaves = EditorGUILayout.IntSlider("Octaves", noise.moistureOctaves, 1, 8);
                    noise.moistureLacunarity = EditorGUILayout.Slider("Lacunarity", noise.moistureLacunarity, 1.0f, 4.0f);
                    noise.moisturePersistence = EditorGUILayout.Slider("Persistence (Gain)", noise.moisturePersistence, 0.0f, 1.0f);
                }

                if (noise.moistureType == NoiseProvider.NoiseType.Worley)
                {
                    EditorGUILayout.LabelField("Worley", EditorStyles.miniBoldLabel);
                    noise.moistureCellDistance = (CellularDistance)EditorGUILayout.EnumPopup("Distance Metric", noise.moistureCellDistance);
                    noise.moistureCellReturn   = WorleyReturnDrawer.Popup("Return Type", noise.moistureCellReturn);
                    noise.moistureJitter       = EditorGUILayout.Slider("Jitter", noise.moistureJitter, 0f, 1f);
                }
            }
            EditorGUI.indentLevel--;
        }


        biomeHeightsFoldout = EditorGUILayout.Foldout(biomeHeightsFoldout, "Biome Heights (ułamki amplitudy)", true);
        if (biomeHeightsFoldout)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox("Każdy próg = ułamek × baseAmplitude. Przy fAmp=1000 i ułamku=0.3 → próg na 300m. " +
                "Zmiana amplitudy auto-skaluje wszystkie biome thresholds.", MessageType.None);

            float amp = noise.baseAmplitude;

            noise.beachTopFraction = SliderWithPreview("Beach Top", "Plaża kończy się przy tym progu (sea → biome).",
                noise.beachTopFraction, amp);
            noise.biomeTopFraction = SliderWithPreview("Biome Top", "Pełen biom widoczny od tej wysokości.",
                noise.biomeTopFraction, amp);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Rock", EditorStyles.miniBoldLabel);
            noise.rockStartFraction = SliderWithPreview("Rock Start", "Skała zaczyna się tu mieszać z biomem.",
                noise.rockStartFraction, amp);
            noise.rockEndFraction = SliderWithPreview("Rock End", "Skała w pełni dominuje.",
                noise.rockEndFraction, amp);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Snow", EditorStyles.miniBoldLabel);
            noise.snowLineColdFraction = SliderWithPreview("Snow Line (Cold)", "W zimnym klimacie śnieg zaczyna się tutaj.",
                noise.snowLineColdFraction, amp);
            noise.snowLineHotFraction = SliderWithPreview("Snow Line (Hot)", "W gorącym klimacie śnieg dopiero tutaj (zwykle blisko 1.0 = tylko szczyty).",
                noise.snowLineHotFraction, amp);
            noise.snowBandWidthFraction = SliderWithPreview("Snow Band Width", "Szerokość strefy przejścia w pełen śnieg.",
                noise.snowBandWidthFraction, amp);

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Moisture Modulators", EditorStyles.miniBoldLabel);
            noise.valleyTopFraction = SliderWithPreview("Valley Top", "Efekt doliny — wilgoć rośnie poniżej tego progu.",
                noise.valleyTopFraction, amp);
            noise.peakHeightFraction = SliderWithPreview("Peak Height", "Efekt szczytów — wilgoć maleje do 0 przy tym progu (zmarzlina).",
                noise.peakHeightFraction, amp);

            EditorGUI.indentLevel--;
        }

        if (GUI.changed)
        {
            EditorUtility.SetDirty(noise);
            noise.SetupNoise();
        }
    }

    private void DrawBaseNoiseStack(NoiseProvider noise)
    {
        if (noise.baseNoiseLayers == null)
        {
            noise.baseNoiseLayers = new List<NoiseProvider.BaseNoiseLayer>();
        }

        if (noise.baseNoiseLayers.Count == 0)
        {
            noise.baseNoiseLayers.Add(NoiseProvider.CreateDefaultBaseLayer(0));
        }

        EnsureBaseLayerFoldouts(noise.baseNoiseLayers.Count);

        EditorGUILayout.Space(8);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Noise Stack", EditorStyles.miniBoldLabel);
        if (GUILayout.Button("+ Add Noise", GUILayout.Width(110)))
        {
            Undo.RecordObject(noise, "Add Base Noise Layer");
            noise.baseNoiseLayers.Add(NoiseProvider.CreateDefaultBaseLayer(noise.baseNoiseLayers.Count));
            baseLayerFoldouts.Add(true);
            GUI.changed = true;
        }
        EditorGUILayout.EndHorizontal();

        for (int i = 0; i < noise.baseNoiseLayers.Count; i++)
        {
            NoiseProvider.BaseNoiseLayer layer = noise.baseNoiseLayers[i];
            if (layer == null)
            {
                layer = NoiseProvider.CreateDefaultBaseLayer(i);
                noise.baseNoiseLayers[i] = layer;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            baseLayerFoldouts[i] = EditorGUILayout.Foldout(
                baseLayerFoldouts[i],
                i == 0 ? $"{i + 1}. Base {layer.type}" : $"{i + 1}. {layer.blendMode} {layer.type}",
                true);

            using (new EditorGUI.DisabledScope(i == 0))
            {
                if (GUILayout.Button("Up", GUILayout.Width(34)))
                {
                    SwapLayers(noise, i, i - 1);
                }
            }

            using (new EditorGUI.DisabledScope(i >= noise.baseNoiseLayers.Count - 1))
            {
                if (GUILayout.Button("Down", GUILayout.Width(48)))
                {
                    SwapLayers(noise, i, i + 1);
                }
            }

            using (new EditorGUI.DisabledScope(noise.baseNoiseLayers.Count <= 1))
            {
                if (GUILayout.Button("Remove", GUILayout.Width(64)))
                {
                    Undo.RecordObject(noise, "Remove Base Noise Layer");
                    noise.baseNoiseLayers.RemoveAt(i);
                    baseLayerFoldouts.RemoveAt(i);
                    GUI.changed = true;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
            }
            EditorGUILayout.EndHorizontal();

            if (baseLayerFoldouts[i])
            {
                EditorGUI.indentLevel++;
                layer.enabled = EditorGUILayout.Toggle("Enabled", layer.enabled);
                using (new EditorGUI.DisabledScope(!layer.enabled))
                {
                    if (i > 0)
                    {
                        layer.blendMode = (NoiseProvider.BaseNoiseBlendMode)EditorGUILayout.EnumPopup("Blend Mode", layer.blendMode);
                    }
                    layer.strength = EditorGUILayout.Slider("Strength", layer.strength, 0f, 2f);
                    layer.type = (NoiseProvider.NoiseType)EditorGUILayout.EnumPopup("Type", layer.type);
                    layer.fractal = (NoiseProvider.FractalType)EditorGUILayout.EnumPopup("Fractal", layer.fractal);
                    layer.frequency = EditorGUILayout.FloatField("Frequency", layer.frequency);

                    if (layer.fractal != NoiseProvider.FractalType.None)
                    {
                        layer.octaves = EditorGUILayout.IntSlider("Octaves", layer.octaves, 1, 8);
                        layer.lacunarity = EditorGUILayout.Slider("Lacunarity", layer.lacunarity, 1f, 4f);
                        layer.persistence = EditorGUILayout.Slider("Persistence (Gain)", layer.persistence, 0f, 1f);
                    }

                    if (layer.type == NoiseProvider.NoiseType.Worley)
                    {
                        EditorGUILayout.LabelField("Worley", EditorStyles.miniBoldLabel);
                        layer.cellDistance = (CellularDistance)EditorGUILayout.EnumPopup("Distance Metric", layer.cellDistance);
                        layer.cellReturn = WorleyReturnDrawer.Popup("Return Type", layer.cellReturn);
                        layer.jitter = EditorGUILayout.Slider("Jitter", layer.jitter, 0f, 1f);
                    }
                }
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }
    }

    private void EnsureBaseLayerFoldouts(int count)
    {
        while (baseLayerFoldouts.Count < count)
        {
            baseLayerFoldouts.Add(true);
        }

        while (baseLayerFoldouts.Count > count)
        {
            baseLayerFoldouts.RemoveAt(baseLayerFoldouts.Count - 1);
        }
    }

    private void SwapLayers(NoiseProvider noise, int from, int to)
    {
        Undo.RecordObject(noise, "Move Base Noise Layer");
        NoiseProvider.BaseNoiseLayer temp = noise.baseNoiseLayers[from];
        noise.baseNoiseLayers[from] = noise.baseNoiseLayers[to];
        noise.baseNoiseLayers[to] = temp;

        bool foldout = baseLayerFoldouts[from];
        baseLayerFoldouts[from] = baseLayerFoldouts[to];
        baseLayerFoldouts[to] = foldout;
        GUI.changed = true;
    }


    private static float SliderWithPreview(string label, string tooltip, float value, float amplitude)
    {
        EditorGUILayout.BeginHorizontal();
        float newValue = EditorGUILayout.Slider(new GUIContent(label, tooltip), value, 0f, 1f);
        EditorGUILayout.LabelField($"= {newValue * amplitude:F0} m", GUILayout.Width(70));
        EditorGUILayout.EndHorizontal();
        return newValue;
    }
}

public static class WorleyReturnDrawer
{
    private static readonly CellularReturn[] TerrainReturnValues =
    {
        CellularReturn.Distance,
        CellularReturn.Distance2,
        CellularReturn.Distance2Add,
        CellularReturn.Distance2Sub
    };

    private static readonly GUIContent[] TerrainReturnLabels =
    {
        new GUIContent("F1"),
        new GUIContent("F2"),
        new GUIContent("F2 + F1"),
        new GUIContent("F2 - F1")
    };

    private static readonly FastNoiseLite.CellularReturnType[] FastNoiseReturnValues =
    {
        FastNoiseLite.CellularReturnType.Distance,
        FastNoiseLite.CellularReturnType.Distance2,
        FastNoiseLite.CellularReturnType.Distance2Add,
        FastNoiseLite.CellularReturnType.Distance2Sub,
        FastNoiseLite.CellularReturnType.Distance2Mul,
        FastNoiseLite.CellularReturnType.Distance2Div
    };

    private static readonly GUIContent[] FastNoiseReturnLabels =
    {
        new GUIContent("F1"),
        new GUIContent("F2"),
        new GUIContent("F2 + F1"),
        new GUIContent("F2 - F1"),
        new GUIContent("F2 * F1"),
        new GUIContent("F1 / F2")
    };

    public static CellularReturn Popup(string label, CellularReturn current)
    {
        int index = TerrainReturnIndex(current);
        int next = EditorGUILayout.Popup(new GUIContent(label), index, TerrainReturnLabels);
        return TerrainReturnValues[next];
    }

    public static void PropertyField(Rect position, SerializedProperty property, GUIContent label)
    {
        CellularReturn current = (CellularReturn)property.enumValueIndex;
        int index = TerrainReturnIndex(current);
        int next = EditorGUI.Popup(position, label, index, TerrainReturnLabels);
        property.enumValueIndex = (int)TerrainReturnValues[next];
    }

    public static void FastNoisePropertyField(Rect position, SerializedProperty property, GUIContent label)
    {
        FastNoiseLite.CellularReturnType current = (FastNoiseLite.CellularReturnType)property.enumValueIndex;
        int index = FastNoiseReturnIndex(current);
        int next = EditorGUI.Popup(position, label, index, FastNoiseReturnLabels);
        property.enumValueIndex = (int)FastNoiseReturnValues[next];
    }

    private static int TerrainReturnIndex(CellularReturn value)
    {
        value = NoiseProvider.NormalizeWorleyReturn(value);
        for (int i = 0; i < TerrainReturnValues.Length; i++)
        {
            if (TerrainReturnValues[i] == value)
            {
                return i;
            }
        }

        return 0;
    }

    private static int FastNoiseReturnIndex(FastNoiseLite.CellularReturnType value)
    {
        if (value == FastNoiseLite.CellularReturnType.CellValue)
        {
            value = FastNoiseLite.CellularReturnType.Distance;
        }

        for (int i = 0; i < FastNoiseReturnValues.Length; i++)
        {
            if (FastNoiseReturnValues[i] == value)
            {
                return i;
            }
        }

        return 0;
    }
}

[CustomPropertyDrawer(typeof(CellularReturn))]
public sealed class CellularReturnPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        WorleyReturnDrawer.PropertyField(position, property, label);
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight;
    }
}

[CustomPropertyDrawer(typeof(FastNoiseLite.CellularReturnType))]
public sealed class FastNoiseLiteCellularReturnPropertyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        WorleyReturnDrawer.FastNoisePropertyField(position, property, label);
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight;
    }
}

using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TerrainPreviewGenerator))]
public class TerrainPreviewGeneratorEditor : Editor
{
    private SerializedProperty _noiseProvider;
    private SerializedProperty _coloredTerrainMaterial;
    private SerializedProperty _texturePreviewMaterial;
    private SerializedProperty _sampleOrigin;
    private SerializedProperty _terrainSize;
    private SerializedProperty _meshResolution;
    private SerializedProperty _centerMeshOnObject;
    private SerializedProperty _textureResolution;
    private SerializedProperty _textureProperty;
    private SerializedProperty _overrideNoise;
    private SerializedProperty _noiseType;
    private SerializedProperty _fractal;
    private SerializedProperty _hashMode;
    private SerializedProperty _strength;
    private SerializedProperty _frequency;
    private SerializedProperty _octaves;
    private SerializedProperty _lacunarity;
    private SerializedProperty _persistence;
    private SerializedProperty _jitter;
    private SerializedProperty _cellDistance;
    private SerializedProperty _cellReturn;

    [MenuItem("GameObject/PCG/Terrain Preview Generator", false, 10)]
    private static void CreateTerrainPreviewGenerator(MenuCommand menuCommand)
    {
        GameObject go = new GameObject("Terrain Preview Generator");
        go.AddComponent<TerrainPreviewGenerator>();
        GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Create Terrain Preview Generator");
        Selection.activeObject = go;
    }

    private void OnEnable()
    {
        _noiseProvider = serializedObject.FindProperty("noiseProvider");
        _coloredTerrainMaterial = serializedObject.FindProperty("coloredTerrainMaterial");
        _texturePreviewMaterial = serializedObject.FindProperty("texturePreviewMaterial");
        _sampleOrigin = serializedObject.FindProperty("sampleOrigin");
        _terrainSize = serializedObject.FindProperty("terrainSize");
        _meshResolution = serializedObject.FindProperty("meshResolution");
        _centerMeshOnObject = serializedObject.FindProperty("centerMeshOnObject");
        _textureResolution = serializedObject.FindProperty("textureResolution");
        _textureProperty = serializedObject.FindProperty("textureProperty");
        _overrideNoise = serializedObject.FindProperty("overrideNoise");
        _noiseType = serializedObject.FindProperty("noiseType");
        _fractal = serializedObject.FindProperty("fractal");
        _hashMode = serializedObject.FindProperty("hashMode");
        _strength = serializedObject.FindProperty("strength");
        _frequency = serializedObject.FindProperty("frequency");
        _octaves = serializedObject.FindProperty("octaves");
        _lacunarity = serializedObject.FindProperty("lacunarity");
        _persistence = serializedObject.FindProperty("persistence");
        _jitter = serializedObject.FindProperty("jitter");
        _cellDistance = serializedObject.FindProperty("cellDistance");
        _cellReturn = serializedObject.FindProperty("cellReturn");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawSection("References");
        EditorGUILayout.PropertyField(_noiseProvider, new GUIContent("Noise Provider"));
        EditorGUILayout.PropertyField(_coloredTerrainMaterial, new GUIContent("Colored Terrain Material"));
        EditorGUILayout.PropertyField(_texturePreviewMaterial, new GUIContent("Texture Preview Material"));

        DrawSection("Terrain Area");
        EditorGUILayout.PropertyField(_sampleOrigin, new GUIContent("Sample Origin"));
        EditorGUILayout.PropertyField(_terrainSize, new GUIContent("Terrain Size"));
        EditorGUILayout.PropertyField(_meshResolution, new GUIContent("Mesh Resolution"));
        EditorGUILayout.PropertyField(_centerMeshOnObject, new GUIContent("Center Mesh On Object"));

        DrawSection("Noise");
        EditorGUILayout.PropertyField(_overrideNoise, new GUIContent("Override Noise Provider"));
        using (new EditorGUI.DisabledScope(!_overrideNoise.boolValue))
        {
            EditorGUILayout.PropertyField(_noiseType, new GUIContent("Noise Type"));
            EditorGUILayout.PropertyField(_fractal, new GUIContent("Fractal"));
            EditorGUILayout.PropertyField(_strength, new GUIContent("Strength"));
            EditorGUILayout.PropertyField(_frequency, new GUIContent("Frequency"));

            NoiseProvider.NoiseType type = (NoiseProvider.NoiseType)_noiseType.enumValueIndex;
            NoiseProvider.FractalType fractalValue = (NoiseProvider.FractalType)_fractal.enumValueIndex;

            if (type == NoiseProvider.NoiseType.Perlin || type == NoiseProvider.NoiseType.SimplexNoise)
            {
                EditorGUILayout.PropertyField(_hashMode, new GUIContent("Hash Mode"));
            }

            if (type == NoiseProvider.NoiseType.Worley)
            {
                EditorGUILayout.PropertyField(_jitter, new GUIContent("Jitter"));
                EditorGUILayout.PropertyField(_cellDistance, new GUIContent("Worley Distance Metric"));
                EditorGUILayout.PropertyField(_cellReturn, new GUIContent("Worley Return"));
            }

            if (fractalValue != NoiseProvider.FractalType.None)
            {
                EditorGUILayout.PropertyField(_octaves, new GUIContent("Octaves"));
                EditorGUILayout.PropertyField(_lacunarity, new GUIContent("Lacunarity"));
                EditorGUILayout.PropertyField(_persistence, new GUIContent("Persistence"));
            }
        }

        DrawSection("Texture Output");
        EditorGUILayout.PropertyField(_textureResolution, new GUIContent("Texture Resolution"));
        EditorGUILayout.PropertyField(_textureProperty, new GUIContent("Texture Property"));

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(12);
        TerrainPreviewGenerator generator = (TerrainPreviewGenerator)target;

        if (GUILayout.Button("Generate", GUILayout.Height(28)))
        {
            Generate(generator);
        }

        DrawSection("2D");
        DrawModeRow(
            generator,
            ("Noise", TerrainPreviewGenerator.PreviewMode.Noise2D),
            ("Colored Noise", TerrainPreviewGenerator.PreviewMode.ColoredNoise2D),
            ("Climate Raw", TerrainPreviewGenerator.PreviewMode.ClimateNoise2D));

        DrawSection("Terrain + Climate");
        DrawModeRow(
            generator,
            ("Terrain Noise", TerrainPreviewGenerator.PreviewMode.TerrainNoise),
            ("Terrain Colored Noise", TerrainPreviewGenerator.PreviewMode.TerrainColoredNoise),
            ("3D", TerrainPreviewGenerator.PreviewMode.Terrain3D));

        DrawSection("Climate Map");
        DrawLabeledModeRow(
            "Climate",
            generator,
            ("Raw", TerrainPreviewGenerator.PreviewMode.ClimateNoise2D),
            ("Effective", TerrainPreviewGenerator.PreviewMode.ClimateEffective2D));
        DrawLabeledModeRow(
            "Humidity",
            generator,
            ("Raw", TerrainPreviewGenerator.PreviewMode.HumidityRaw),
            ("Effective", TerrainPreviewGenerator.PreviewMode.HumidityEffective));
        DrawLabeledModeRow(
            "Temperature",
            generator,
            ("Raw", TerrainPreviewGenerator.PreviewMode.TemperatureRaw),
            ("Effective", TerrainPreviewGenerator.PreviewMode.TemperatureEffective));

        EditorGUILayout.Space(6);
        if (GUILayout.Button("Clear Preview"))
        {
            Undo.RecordObject(generator, "Clear Terrain Preview");
            generator.ClearGenerated();
            EditorUtility.SetDirty(generator);
        }
    }

    private static void DrawSection(string label)
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
    }

    private static void DrawModeRow(
        TerrainPreviewGenerator generator,
        (string label, TerrainPreviewGenerator.PreviewMode mode) first,
        (string label, TerrainPreviewGenerator.PreviewMode mode) second,
        (string label, TerrainPreviewGenerator.PreviewMode mode) third)
    {
        EditorGUILayout.BeginHorizontal();
        DrawModeButton(generator, first.label, first.mode);
        DrawModeButton(generator, second.label, second.mode);
        DrawModeButton(generator, third.label, third.mode);
        EditorGUILayout.EndHorizontal();
    }

    private static void DrawLabeledModeRow(
        string label,
        TerrainPreviewGenerator generator,
        (string label, TerrainPreviewGenerator.PreviewMode mode) first,
        (string label, TerrainPreviewGenerator.PreviewMode mode) second)
    {
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(90));
        DrawModeButton(generator, first.label, first.mode);
        DrawModeButton(generator, second.label, second.mode);
        EditorGUILayout.EndHorizontal();
    }

    private static void DrawModeButton(
        TerrainPreviewGenerator generator,
        string label,
        TerrainPreviewGenerator.PreviewMode mode)
    {
        if (GUILayout.Button(label))
        {
            Undo.RecordObject(generator, "Generate Terrain Preview");
            generator.SetPreviewMode(mode);
            generator.GenerateTerrain();
            EditorUtility.SetDirty(generator);
        }
    }

    private static void Generate(TerrainPreviewGenerator generator)
    {
        Undo.RecordObject(generator, "Generate Terrain Preview");
        generator.GenerateTerrain();
        EditorUtility.SetDirty(generator);
    }
}

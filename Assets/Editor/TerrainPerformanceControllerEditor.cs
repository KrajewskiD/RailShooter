using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(TerrainPerformanceController.NoiseBenchmarkConfig))]
public class NoiseBenchmarkConfigDrawer : PropertyDrawer
{
    private static readonly float LineHeight = EditorGUIUtility.singleLineHeight;
    private static readonly float Spacing = EditorGUIUtility.standardVerticalSpacing;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = LineHeight;
        if (property.isExpanded)
        {
            AddPropertyHeight(ref height, property, "enabled", "Enabled");
            AddPropertyHeight(ref height, property, "label", "Label");
            AddPropertyHeight(ref height, property, "useLayerStack", "Use Layer Stack");

            if (GetUseLayerStack(property))
            {
                AddPropertyHeight(ref height, property, "hashMode", "Hash Mode");
                AddPropertyHeight(ref height, property, "baseNoiseLayers", "Noise Layers");
            }
            else
            {
                AddPropertyHeight(ref height, property, "type", "Type");
                AddPropertyHeight(ref height, property, "fractal", "Fractal");
                NoiseProvider.NoiseType type = GetNoiseType(property);
                if (type != NoiseProvider.NoiseType.Worley)
                {
                    AddPropertyHeight(ref height, property, "hashMode", "Hash Mode");
                }
                AddPropertyHeight(ref height, property, "strength", "Strength");
                AddPropertyHeight(ref height, property, "frequency", "Frequency");

                NoiseProvider.FractalType fractal = GetFractal(property);
                if (fractal != NoiseProvider.FractalType.None)
                {
                    AddPropertyHeight(ref height, property, "octaves", "Octaves");
                    AddPropertyHeight(ref height, property, "lacunarity", "Lacunarity");
                    AddPropertyHeight(ref height, property, "persistence", "Persistence");
                }

                if (type == NoiseProvider.NoiseType.Worley)
                {
                    AddPropertyHeight(ref height, property, "cellDistance", "Worley Distance Metric");
                    AddPropertyHeight(ref height, property, "cellReturn", "Worley Return");
                    AddPropertyHeight(ref height, property, "jitter", "Jitter");
                }
            }
        }

        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        Rect row = new Rect(position.x, position.y, position.width, LineHeight);
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);

        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            NextRow(ref row);

            DrawProperty(ref row, property, "enabled", "Enabled");
            DrawProperty(ref row, property, "label", "Label");
            DrawProperty(ref row, property, "useLayerStack", "Use Layer Stack");

            if (GetUseLayerStack(property))
            {
                DrawProperty(ref row, property, "hashMode", "Hash Mode");
                DrawProperty(ref row, property, "baseNoiseLayers", "Noise Layers");
            }
            else
            {
                DrawProperty(ref row, property, "type", "Type");
                DrawProperty(ref row, property, "fractal", "Fractal");
                NoiseProvider.NoiseType type = GetNoiseType(property);
                if (type != NoiseProvider.NoiseType.Worley)
                {
                    DrawProperty(ref row, property, "hashMode", "Hash Mode");
                }
                DrawProperty(ref row, property, "strength", "Strength");
                DrawProperty(ref row, property, "frequency", "Frequency");

                NoiseProvider.FractalType fractal = GetFractal(property);
                if (fractal != NoiseProvider.FractalType.None)
                {
                    DrawProperty(ref row, property, "octaves", "Octaves");
                    DrawProperty(ref row, property, "lacunarity", "Lacunarity");
                    DrawProperty(ref row, property, "persistence", "Persistence");
                }

                if (type == NoiseProvider.NoiseType.Worley)
                {
                    DrawProperty(ref row, property, "cellDistance", "Worley Distance Metric");
                    DrawProperty(ref row, property, "cellReturn", "Worley Return");
                    DrawProperty(ref row, property, "jitter", "Jitter");
                }
            }

            EditorGUI.indentLevel--;
        }

        EditorGUI.EndProperty();
    }

    private static void DrawProperty(ref Rect row, SerializedProperty property, string propertyName, string label)
    {
        SerializedProperty child = property.FindPropertyRelative(propertyName);
        if (child == null)
        {
            return;
        }

        row.height = EditorGUI.GetPropertyHeight(child, new GUIContent(label), true);
        EditorGUI.PropertyField(row, child, new GUIContent(label), true);
        NextRow(ref row);
    }

    private static void NextRow(ref Rect row)
    {
        row.y += row.height + Spacing;
        row.height = LineHeight;
    }

    private static void AddPropertyHeight(ref float height, SerializedProperty property, string propertyName, string label)
    {
        SerializedProperty child = property.FindPropertyRelative(propertyName);
        if (child == null)
        {
            return;
        }

        height += Spacing + EditorGUI.GetPropertyHeight(child, new GUIContent(label), true);
    }

    private static NoiseProvider.NoiseType GetNoiseType(SerializedProperty property)
    {
        return (NoiseProvider.NoiseType)property.FindPropertyRelative("type").enumValueIndex;
    }

    private static NoiseProvider.FractalType GetFractal(SerializedProperty property)
    {
        return (NoiseProvider.FractalType)property.FindPropertyRelative("fractal").enumValueIndex;
    }

    private static bool GetUseLayerStack(SerializedProperty property)
    {
        SerializedProperty useLayerStack = property.FindPropertyRelative("useLayerStack");
        return useLayerStack != null && useLayerStack.boolValue;
    }
}

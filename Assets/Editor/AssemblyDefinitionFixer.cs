using UnityEditor;
using UnityEngine;
using System.IO;

public class AssemblyDefinitionFixer
{
    [MenuItem("Tools/Fix Assembly Definitions")]
    public static void FixAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset");
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);


            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }
    }
}
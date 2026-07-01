using UnityEngine;

public static class UnityObjectUtility
{
    public static bool TrySetTag(GameObject gameObject, string tagName)
    {
        if (gameObject == null || string.IsNullOrEmpty(tagName))
        {
            return false;
        }

        try
        {
            gameObject.tag = tagName;
            return true;
        }
        catch (UnityException)
        {
            return false;
        }
    }

    public static void Destroy(Object obj)
    {
        if (obj == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Object.Destroy(obj);
        }
        else
        {
            Object.DestroyImmediate(obj);
        }
    }
}

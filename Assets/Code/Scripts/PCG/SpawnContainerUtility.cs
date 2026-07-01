using UnityEngine;

public static class SpawnContainerUtility
{
    public static Transform GetOrCreateClearedContainer(Transform parent, string name)
    {
        if (parent == null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Spawned";
        }

        Transform existing = parent.Find(name);
        if (existing != null)
        {
            ClearChildren(existing);
            return existing;
        }

        GameObject containerObject = new GameObject(name);
        containerObject.transform.SetParent(parent, false);
        return containerObject.transform;
    }

    public static void ClearChildren(Transform parent)
    {
        if (parent == null)
        {
            return;
        }

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            UnityObjectUtility.Destroy(parent.GetChild(i).gameObject);
        }
    }
}

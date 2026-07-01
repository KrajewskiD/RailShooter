using UnityEngine;

public static class PlaneVisualLoader
{
    public static Transform Load(PlaneData data, Transform visualSlot, Component owner)
    {
        if (data == null || data.visualPrefab == null || visualSlot == null || owner == null)
        {
            return null;
        }

        ClearSlot(visualSlot);

        GameObject instance = Object.Instantiate(data.visualPrefab, visualSlot);
        instance.name = data.visualPrefab.name;
        instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;
        SetLayerRecursively(instance, owner.gameObject.layer);

        FirePointProvider firePointProvider = owner.GetComponent<FirePointProvider>();
        if (firePointProvider == null)
        {
            firePointProvider = owner.gameObject.AddComponent<FirePointProvider>();
        }

        firePointProvider.SetVisualRoot(instance.transform);
        return instance.transform;
    }

    private static void ClearSlot(Transform visualSlot)
    {
        for (int i = visualSlot.childCount - 1; i >= 0; i--)
        {
            Transform child = visualSlot.GetChild(i);
            child.SetParent(null);
            Object.Destroy(child.gameObject);
        }

        foreach (Renderer renderer in visualSlot.GetComponents<Renderer>())
        {
            renderer.enabled = false;
        }

        foreach (Collider collider in visualSlot.GetComponents<Collider>())
        {
            collider.enabled = false;
        }

        foreach (FirePointMarker marker in visualSlot.GetComponents<FirePointMarker>())
        {
            marker.enabled = false;
        }
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        Transform transform = root.transform;
        for (int i = 0; i < transform.childCount; i++)
        {
            SetLayerRecursively(transform.GetChild(i).gameObject, layer);
        }
    }
}

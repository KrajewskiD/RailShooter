using UnityEngine;

[DisallowMultipleComponent]
public class FirePointMarker : MonoBehaviour
{
    [Tooltip("Role tego firepointu. Można zaznaczyć Primary, Special, lub oba (wspólny firepoint). None = ignorowany.")]
    public FirePointRole roles = FirePointRole.Primary;

    [Tooltip("Kolejność firepointu w obrębie roli. Przy remisie decyduje nazwa transformu.")]
    public int order = 0;

    [Tooltip("Levele, na których ten marker jest aktywny. Puste = bezpieczny fallback aktywny tylko na level 1.")]
    public int[] activeAtLevels;

    public bool IsActiveAtLevel(int level)
    {
        if (activeAtLevels == null || activeAtLevels.Length == 0)
            return level == 1;

        for (int i = 0; i < activeAtLevels.Length; i++)
        {
            if (activeAtLevels[i] == level) return true;
        }
        return false;
    }
}

using UnityEngine;

public static class GameStateBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        GameStateManager.GetOrCreate();
    }
}

using UnityEngine;
using UnityEngine.SceneManagement;

public static class SceneFlow
{
    public static void EnsureRuntimeTimeScale()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = 0.02f;
    }

    public static void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            return;
        }

        EnsureRuntimeTimeScale();
        SceneManager.LoadScene(sceneName);
    }

    public static void ReloadCurrentScene()
    {
        EnsureRuntimeTimeScale();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public static void StartNewRunAndLoad(int planeId, string sceneName)
    {
        string resolvedScene = string.IsNullOrEmpty(sceneName)
            ? SceneManager.GetActiveScene().name
            : sceneName;

        PlayerProgressManager.Instance?.StartNewRun(planeId, resolvedScene);
        GameStateManager.Instance?.ChangeState(GameState.InGame);
        LoadScene(resolvedScene);
    }

    public static void SaveActiveRunAndLoadMenu(string menuSceneName)
    {
        PlayerProgressManager.Instance?.SaveActiveRun();
        GameStateManager.Instance?.ChangeState(GameState.MainMenu);
        LoadScene(menuSceneName);
    }

    public static void Quit(bool saveActiveRun = true)
    {
        EnsureRuntimeTimeScale();
        if (saveActiveRun)
        {
            PlayerProgressManager.Instance?.SaveActiveRun();
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}

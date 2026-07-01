using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuCardUI : MonoBehaviour
{
    [SerializeField] private Button continueButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button exitButton;

    private void Awake()
    {
        continueButton?.onClick.AddListener(() => GameStateManager.GetOrCreate().ChangeState(GameState.InGame));
        restartButton?.onClick.AddListener(RestartLevel);
        exitButton?.onClick.AddListener(ExitGame);
    }

    private void OnEnable()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged += HandleStateChanged;
            HandleStateChanged(GameStateManager.Instance.CurrentState);
        }
    }

    private void OnDisable()
    {
        if (GameStateManager.Instance != null)
            GameStateManager.Instance.OnStateChanged -= HandleStateChanged;
    }

    private void HandleStateChanged(GameState newState)
    {
        gameObject.SetActive(newState == GameState.Pause);
    }

    private void RestartLevel()
    {
        SceneFlow.StartNewRunAndLoad(GameStateManager.ChosenPlaneID, SceneManager.GetActiveScene().name);
    }

    private void ExitGame()
    {
        SceneFlow.Quit();
    }
}

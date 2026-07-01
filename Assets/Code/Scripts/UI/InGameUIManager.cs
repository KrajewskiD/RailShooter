using UnityEngine;
using System.Collections;
using UnityEngine.SceneManagement;

public class InGameUIManager : MonoBehaviour
{
    [Header("Panels")]
    public GameObject menuPanel;
    public GameObject hudPanel;
    public GameObject upgradePanel;
    public GameObject projectilesSelectionPanel;
    public GameObject gameOverPanel;

    private void OnEnable()
    {
        GameStateManager gameStateManager = GameStateManager.GetOrCreate();
        gameStateManager.OnStateChanged -= HandleStateChanged;
        gameStateManager.OnStateChanged += HandleStateChanged;
        StartCoroutine(RefreshStateRoutine());
    }

    private void OnDisable()
    {
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.OnStateChanged -= HandleStateChanged;
        }
    }

    private void HandleStateChanged(GameState state)
    {
        if (this == null) return;

        bool usesToolkitSelection = SelectionScreenController.IsAvailable;

        if (menuPanel) menuPanel.SetActive(state == GameState.Pause);
        if (hudPanel) hudPanel.SetActive(state == GameState.InGame);
        if (upgradePanel) upgradePanel.SetActive(!usesToolkitSelection && state == GameState.UpgradeSelection);
        if (projectilesSelectionPanel) projectilesSelectionPanel.SetActive(!usesToolkitSelection && state == GameState.ProjectileSelection);
        if (gameOverPanel) gameOverPanel.SetActive(false);
    }

    private IEnumerator RefreshStateRoutine()
    {

        yield return new WaitForEndOfFrame();
        
        if (GameStateManager.Instance != null)
        {

            GameStateManager.Instance.ChangeState(GameStateManager.Instance.CurrentState);
            HandleStateChanged(GameStateManager.Instance.CurrentState);
        }
    }
    
    public void RestartGame()
    {
        SceneFlow.StartNewRunAndLoad(GameStateManager.ChosenPlaneID, SceneManager.GetActiveScene().name);
    }
}

using UnityEngine;
using System.Collections;

public class TutorialSequence : MonoBehaviour
{
    [SerializeField] float enemyAtMeters = 300f;
    [SerializeField] float flashAtMeters = 450f;
    [SerializeField] string nextSceneName = "TerrainScene";
    [SerializeField] GameObject enemyPrefab;
    [SerializeField] Transform player;
    [SerializeField] TunnelGenerator tunnelGenerator;
    [SerializeField] CanvasGroup whiteFlash;

    private float distance;
    private bool enemySpawned;
    private bool transitionStarted;

    void Update()
    {
        distance += tunnelGenerator.CurrentScrollSpeed * Time.deltaTime;

        if (!enemySpawned && distance >= enemyAtMeters)
        {
            enemySpawned = true;
            Instantiate(enemyPrefab, player.position + player.forward * 80f, Quaternion.LookRotation(-player.forward));
        }

        if (!transitionStarted && distance >= flashAtMeters)
        {
            transitionStarted = true;
            StartCoroutine(FlashAndLoad());
        }
    }

    IEnumerator FlashAndLoad()
    {
        // biały flash
        for (float t = 0; t < 0.25f; t += Time.deltaTime)
        {
            whiteFlash.alpha = Mathf.Lerp(0f, 1f, t / 0.25f);
            yield return null;
        }

        whiteFlash.alpha = 1f;
        yield return new WaitForSeconds(0.25f);

        PlayerProgressManager.Instance?.CompleteTutorial();
        SceneFlow.LoadScene(nextSceneName);
    }

}

using UnityEngine;
// 1. Musisz dodać tę przestrzeń nazw na samej górze skryptu:
using UnityEngine.InputSystem; 

public class ScreenshotTool : MonoBehaviour
{
    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
        {
            TakeScreenshot(); 
        }
    }

    void TakeScreenshot()
    {
        ScreenCapture.CaptureScreenshot("Screenshot.png");
        Debug.Log("Zrobiono screenshot!");
    }
}
using System.Collections;
using System.IO;
using Masar.Game.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Masar.Game
{
    /// <summary>
    /// QA only, enabled by the "-masarUiCapture" launch argument: steps through every screen
    /// (main menu, settings, devices, drive HUD with touch controls, pause, in-game settings),
    /// saves a screenshot of each to persistentDataPath/ui_*.png, then quits.
    /// It checks the UI layout at the iPad aspect ratio without needing a touchscreen.
    /// </summary>
    public sealed class UiCapture : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void MaybeStart()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-masarUiCapture") < 0) return;
            var go = new GameObject("MASAR UiCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<UiCapture>();
        }

        private string _dir;

        private IEnumerator Shot(string name)
        {
            yield return new WaitForSecondsRealtime(0.6f);
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(_dir, $"ui_{name}.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[MASAR ui] {path}");
            yield return new WaitForSecondsRealtime(0.4f);
        }

        private IEnumerator Start()
        {
            _dir = Application.persistentDataPath;
            if (SceneManager.GetActiveScene().name != "MainMenu") { SceneManager.LoadScene("MainMenu"); yield return null; }
            yield return new WaitForSecondsRealtime(1.0f);
            var menu = FindFirstObjectByType<MainMenu>();
            yield return Shot("01_main_menu");
            if (menu != null)
            {
                menu.ToggleSettings(); yield return Shot("02_settings");
                menu.ToggleSettings();
                menu.ToggleDevices(); yield return Shot("03_devices");
                menu.ToggleDevices();
            }

            SceneManager.LoadScene("TestBlock");
            yield return new WaitForSecondsRealtime(2.0f);
            var session = FindFirstObjectByType<DriveSession>();
            if (session != null)
            {
                session.ForceTouchControls(true);
                yield return Shot("04_drive_touch");
                session.SetPaused(true); yield return Shot("05_pause");
                session.OpenSettings(); yield return Shot("06_pause_settings");
                session.SetPaused(false);
            }
            Application.Quit(0);
        }
    }
}

using System.Collections;
using System.IO;
using Masar.Game.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Masar.Game
{
    /// <summary>
    /// QA only, enabled by the "-masarUiCapture" launch argument: steps through every screen
    /// (intro, main menu, garage, settings, devices, about, drive HUD with touch controls, pause,
    /// in-game settings), saves a screenshot of each to persistentDataPath/ui_*.png, then quits.
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

        private IEnumerator Shot(string name, float settle = 0.6f)
        {
            yield return new WaitForSecondsRealtime(settle);
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
            // The intro, caught while the logo and wordmark are fully in
            yield return Shot("00_intro", 2.6f);
            while (IntroSequence.Playing) yield return null;
            var menu = FindFirstObjectByType<MainMenu>();
            yield return Shot("01_main_menu", 1.2f);
            if (menu != null)
            {
                menu.ToggleCars(); yield return Shot("02_garage");
                menu.ToggleCars();
                menu.ToggleSettings(); yield return Shot("03_settings");
                menu.ToggleSettings();
                menu.ToggleDevices(); yield return Shot("04_devices");
                menu.ToggleDevices();
                menu.ToggleAbout(); yield return Shot("05_about");
                menu.ToggleAbout();
            }

            SceneManager.LoadScene("TestBlock");
            yield return new WaitForSecondsRealtime(2.0f);
            var session = FindFirstObjectByType<DriveSession>();
            if (session != null)
            {
                session.ForceTouchControls(true);
                yield return Shot("06_drive_touch");
                session.SetPaused(true); yield return Shot("07_pause");
                session.OpenSettings(); yield return Shot("08_pause_settings");
                session.SetPaused(false);
            }

            // The city through the menu: the loading screen while the building blocks stream in, then the street
            if (Application.CanStreamedLevelBeLoaded("Tahlia"))
            {
                SceneManager.LoadScene("MainMenu");
                yield return null; yield return null;
                menu = FindFirstObjectByType<MainMenu>();
                if (menu != null)
                {
                    menu.OnDrive("Tahlia");
                    while (SceneManager.GetActiveScene().name != "Tahlia") yield return null;
                    yield return Shot("09_loading_city", 0.05f);
                    float t0 = Time.realtimeSinceStartup;
                    while (!Masar.World.CityLodManager.Settled && Time.realtimeSinceStartup - t0 < 120f) yield return null;
                    yield return Shot("10_city_start", 1.2f);
                }
            }
            Application.Quit(0);
        }
    }
}

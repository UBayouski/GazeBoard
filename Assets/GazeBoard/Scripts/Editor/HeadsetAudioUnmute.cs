using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GazeBoard.EditorTools
{
    /// <summary>
    /// Horizon OS mutes an app (PLAY_AUDIO app-op set to "ignore") when it is started with a bare
    /// component intent, which is how Unity's Build And Run launches it over adb. Starting it with
    /// the LAUNCHER and com.oculus.intent.category.VR categories, as the Library does, keeps audio
    /// on. After each Android build this waits for Unity's launch, then restarts the app properly.
    /// </summary>
    public class HeadsetAudioUnmute : IPostprocessBuildWithReport
    {
        private const string VrCategory = "com.oculus.intent.category.VR";

        public int callbackOrder => 1000;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            string package = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            string adb = AdbPath();
            if (string.IsNullOrEmpty(adb)) return;

            // Build And Run installs and launches after this callback returns. An instance from the
            // previous session may still be running, so wait for a *new* process id: the install
            // kills the old one and Unity's launch creates a different one.
            string oldPid = Run(adb, $"shell pidof {package}").Trim();
            new Thread(() =>
            {
                // Up to 10 minutes: with the headset on the desk the process may only start when it is worn.
                for (int i = 0; i < 300; i++)
                {
                    Thread.Sleep(2000);
                    string pid = Run(adb, $"shell pidof {package}").Trim();
                    if (pid.Length == 0 || pid == oldPid) continue;

                    Thread.Sleep(2000);
                    Debug.Log($"[GazeBoard] {RelaunchAsVrApp(adb, package)}");
                    return;
                }
                Debug.Log("[GazeBoard] App did not start within 10 minutes; use GazeBoard > Headset: Relaunch App if it is silent.");
            }) { IsBackground = true }.Start();
        }

        [MenuItem("GazeBoard/Headset: Relaunch App (fixes muted audio)")]
        private static void RelaunchNow()
        {
            string package = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            string adb = AdbPath();
            if (string.IsNullOrEmpty(adb))
            {
                Debug.LogError("[GazeBoard] adb not found. Check Preferences > External Tools > Android SDK.");
                return;
            }
            Debug.Log($"[GazeBoard] {RelaunchAsVrApp(adb, package)}");
        }

        private static string RelaunchAsVrApp(string adb, string package)
        {
            string component = ResolveLauncherActivity(adb, package);
            if (string.IsNullOrEmpty(component)) return $"could not resolve the launcher activity of {package}";

            // Already started the right way (e.g. from the Library)? Then leave it alone.
            string tasks = Run(adb, "shell dumpsys activity activities");
            foreach (string line in tasks.Split('\n'))
            {
                // e.g. "Intent { act=android.intent.action.MAIN cat=[...,com.oculus.intent.category.VR] ... cmp=<package>/... }"
                if (line.Contains("Intent {") && line.Contains("cmp=" + package + "/") && line.Contains(VrCategory))
                {
                    return "app was already launched as a VR app; no relaunch needed.";
                }
            }

            Run(adb, $"shell am force-stop {package}");
            string start = Run(adb,
                $"shell am start -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -c {VrCategory} -n {component}");

            // The shell may still flip the op to "ignore" for a while, e.g. until the headset is worn.
            // Keep asserting "allow" so the app's next audio stream is created unmuted.
            string op = "";
            for (int i = 0; i < 15; i++)
            {
                Thread.Sleep(3000);
                op = Run(adb, $"shell appops get {package} PLAY_AUDIO").Trim().Replace("\n", " ");
                if (op.Contains("ignore")) Run(adb, $"shell appops set {package} PLAY_AUDIO allow");
            }
            return $"relaunched {component} as a VR app. {start.Trim()} | PLAY_AUDIO after 45 s: {op}";
        }

        private static string ResolveLauncherActivity(string adb, string package)
        {
            string output = Run(adb, $"shell cmd package resolve-activity --brief -c android.intent.category.LAUNCHER {package}");
            foreach (string line in output.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith(package + "/")) return trimmed;
            }
            return null;
        }

        private static string AdbPath()
        {
            string sdk = EditorPrefs.GetString("AndroidSdkRoot");
            if (string.IsNullOrEmpty(sdk))
            {
                sdk = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer", "SDK");
            }
            string exe = Application.platform == RuntimePlatform.WindowsEditor ? "adb.exe" : "adb";
            string path = Path.Combine(sdk, "platform-tools", exe);
            return File.Exists(path) ? path : null;
        }

        private static string Run(string exe, string args)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                });
                string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                return output;
            }
            catch (Exception e)
            {
                return "error: " + e.Message;
            }
        }
    }
}

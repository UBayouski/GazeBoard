using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace GazeBoard.Speech
{
    /// <summary>
    /// Copies the baked speech clips out of the APK into the app's own storage.
    ///
    /// On Android, StreamingAssets lives inside the APK ("jar:file://...") and the Voice SDK cannot
    /// stream a clip from there: it builds "file://jar:file://..." and the request hangs until it
    /// times out. Plain files in persistentDataPath work, so on first launch (and after every
    /// update) the clips listed in StreamingAssets/TTS/manifest.txt are copied to
    /// persistentDataPath/TTS, the folder the SDK uses for its Persistent disk cache.
    /// </summary>
    public static class PreloadInstaller
    {
        private const string ManifestName = "manifest.txt";
        private const string CacheFolder = "TTS";
        private const string VersionKey = "gazeboard.preload.installedVersion";

        public static bool Done { get; private set; }
        public static int Installed { get; private set; }

        public static string CacheDirectory => Path.Combine(Application.persistentDataPath, CacheFolder);

        /// <summary>True when the baked clips must be read from the copied folder, not from StreamingAssets.</summary>
        public static bool UsesCopiedClips => Application.platform == RuntimePlatform.Android;

        public static IEnumerator Install()
        {
            if (!UsesCopiedClips)
            {
                Done = true;
                yield break;
            }

            string stamp = Application.version + "|" + Application.buildGUID;
            bool sameBuild = PlayerPrefs.GetString(VersionKey, "") == stamp;

            string manifestUrl = Path.Combine(Application.streamingAssetsPath, CacheFolder, ManifestName);
            string manifest = null;
            using (var request = UnityWebRequest.Get(manifestUrl))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success) manifest = request.downloadHandler.text;
            }
            if (string.IsNullOrEmpty(manifest))
            {
                Debug.LogWarning("[GazeBoard.Speech] No clip manifest in StreamingAssets/TTS; offline speech unavailable. Run GazeBoard > Preload Board Phrases.");
                Done = true;
                yield break;
            }

            Directory.CreateDirectory(CacheDirectory);
            int copied = 0, present = 0, failed = 0;
            foreach (string raw in manifest.Split('\n'))
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                string target = Path.Combine(CacheDirectory, name);
                if (sameBuild && File.Exists(target)) { present++; continue; }

                string source = Path.Combine(Application.streamingAssetsPath, CacheFolder, name);
                using var request = UnityWebRequest.Get(source);
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    failed++;
                    continue;
                }
                try
                {
                    File.WriteAllBytes(target, request.downloadHandler.data);
                    copied++;
                }
                catch (Exception e)
                {
                    failed++;
                    Debug.LogWarning($"[GazeBoard.Speech] Could not write {name}: {e.Message}");
                }
            }

            if (failed == 0)
            {
                PlayerPrefs.SetString(VersionKey, stamp);
                PlayerPrefs.Save();
            }
            Installed = copied + present;
            Done = true;
            Debug.Log($"[GazeBoard.Speech] Baked clips ready in {CacheDirectory}: {copied} copied, {present} already present, {failed} failed.");
        }
    }
}

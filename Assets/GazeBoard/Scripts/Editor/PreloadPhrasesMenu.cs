using System.Collections.Generic;
using System.IO;
using System.Linq;
using GazeBoard.Board;
using GazeBoard.Speech;
using Meta.WitAi.TTS;
using Meta.WitAi.TTS.Integrations;
using Meta.WitAi.TTS.Preload;
using Meta.WitAi.TTS.Utilities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GazeBoard.EditorTools
{
    /// <summary>
    /// Bakes every board phrase and every fixed spoken string into StreamingAssets/TTS so the
    /// app speaks them offline. Spelled words are not covered; they stream from Wit.ai.
    /// Run again whenever the board content or the voice preset changes.
    /// </summary>
    public static class PreloadPhrasesMenu
    {
        private const string DefinitionPath = "Assets/GazeBoard/DefaultBoard.asset";
        private const string PreloadSettingsPath = "Assets/GazeBoard/TTSPreload-GazeBoard.asset";

        // The voice lives on the scene's TTSSpeaker. Choosing one here sets it there and bakes
        // every phrase again in that voice, so the app and its clips can never disagree.
        private const string VoiceMenu = "GazeBoard/Voice (sets voice + preloads phrases)/";
        private const string Charlie = "WIT$CHARLIE", Rebecca = "WIT$REBECCA", Cooper = "WIT$COOPER", Butler = "WIT$BRITISH BUTLER";

        [MenuItem(VoiceMenu + "Charlie (US, male)")] private static void UseCharlie() => PreloadPhrases(Charlie);
        [MenuItem(VoiceMenu + "Charlie (US, male)", true)] private static bool CheckCharlie() => MarkCurrent(VoiceMenu + "Charlie (US, male)", Charlie);
        [MenuItem(VoiceMenu + "Rebecca (US, female)")] private static void UseRebecca() => PreloadPhrases(Rebecca);
        [MenuItem(VoiceMenu + "Rebecca (US, female)", true)] private static bool CheckRebecca() => MarkCurrent(VoiceMenu + "Rebecca (US, female)", Rebecca);
        [MenuItem(VoiceMenu + "Cooper (US, male)")] private static void UseCooper() => PreloadPhrases(Cooper);
        [MenuItem(VoiceMenu + "Cooper (US, male)", true)] private static bool CheckCooper() => MarkCurrent(VoiceMenu + "Cooper (US, male)", Cooper);
        [MenuItem(VoiceMenu + "British Butler (UK, male)")] private static void UseButler() => PreloadPhrases(Butler);
        [MenuItem(VoiceMenu + "British Butler (UK, male)", true)] private static bool CheckButler() => MarkCurrent(VoiceMenu + "British Butler (UK, male)", Butler);

        /// <summary>Ticks the menu entry of the voice the scene's speaker uses now.</summary>
        private static bool MarkCurrent(string menuPath, string voiceId)
        {
            TTSSpeaker speaker = Object.FindFirstObjectByType<TTSSpeaker>(FindObjectsInactive.Include);
            Menu.SetChecked(menuPath, speaker != null && speaker.VoiceID == voiceId);
            return !Application.isPlaying;
        }

        [MenuItem("GazeBoard/Preload Board Phrases (Wit, needs internet)")]
        private static void PreloadCurrentVoice() => PreloadPhrases(null);

        /// <summary>Bakes all phrases. voice null keeps the voice the scene's speaker already has.</summary>
        private static void PreloadPhrases(string voice)
        {
            if (Application.isPlaying)
            {
                EditorUtility.DisplayDialog("GazeBoard", "Exit Play mode first.", "OK");
                return;
            }

            TTSSpeaker speaker = Object.FindFirstObjectByType<TTSSpeaker>(FindObjectsInactive.Include);
            TTSService service = speaker != null ? speaker.TTSService : Object.FindFirstObjectByType<TTSService>(FindObjectsInactive.Include);
            if (speaker == null || service == null)
            {
                EditorUtility.DisplayDialog("GazeBoard", "No TTSSpeaker / TTS service in the open scene. Add the Voice SDK default TTS setup first.", "OK");
                return;
            }

            EnsureDiskCacheHandler(service);

            var definition = AssetDatabase.LoadAssetAtPath<BoardDefinition>(DefinitionPath);
            List<string> phrases = CollectPhrases(definition);
            if (phrases.Count == 0)
            {
                EditorUtility.DisplayDialog("GazeBoard", "No phrases found. Run GazeBoard > Create Default Board Definition first.", "OK");
                return;
            }

            TTSPreloadSettings settings = AssetDatabase.LoadAssetAtPath<TTSPreloadSettings>(PreloadSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<TTSPreloadSettings>();
                AssetDatabase.CreateAsset(settings, PreloadSettingsPath);
            }

            string voiceId = string.IsNullOrEmpty(voice) ? speaker.VoiceID : voice;
            if (service.GetPresetVoiceSettings(voiceId) == null)
            {
                EditorUtility.DisplayDialog("GazeBoard", $"The TTS service has no voice preset '{voiceId}'.", "OK");
                return;
            }
            if (speaker.VoiceID != voiceId)
            {
                Undo.RecordObject(speaker, "Set speech voice");
                speaker.VoiceID = voiceId;
                EditorUtility.SetDirty(speaker);
                EditorSceneManager.MarkSceneDirty(speaker.gameObject.scene);
                Debug.Log($"[GazeBoard] Speech voice set to '{voiceId}' on the scene's TTSSpeaker. Save the scene (Ctrl+S).");
            }

            settings.data = new TTSPreloadData
            {
                voices = new[]
                {
                    new TTSPreloadVoiceData
                    {
                        presetVoiceID = voiceId,
                        phrases = phrases.Select(p => new TTSPreloadPhraseData { textToSpeak = p }).ToArray(),
                    },
                },
            };
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Debug.Log($"[GazeBoard] Preloading {phrases.Count} phrases with voice '{voiceId}'. This downloads from Wit.ai; watch the progress bar.");

            TTSPreloadUtility.PreloadData(service, settings.data,
                progress => EditorUtility.DisplayProgressBar("GazeBoard", $"Downloading speech clips ({progress:P0})", progress),
                (data, log) =>
                {
                    EditorUtility.ClearProgressBar();
                    EditorUtility.SetDirty(settings);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();

                    int ok = data.voices.Sum(v => v.phrases.Count(p => p.downloaded));
                    int total = data.voices.Sum(v => v.phrases.Length);
                    string folder = Path.Combine(Application.streamingAssetsPath, "TTS");
                    // Clips of an earlier voice or of reworded phrases would only add weight to
                    // the APK. Clear them, but only once every current phrase is safely on disk.
                    int removed = ok == total ? RemoveStaleClips(folder, data) : 0;
                    int files = WriteManifest(folder);
                    AssetDatabase.Refresh();
                    Debug.Log($"[GazeBoard] Preload finished: {ok}/{total} phrases downloaded with voice '{voiceId}', {files} clip files in StreamingAssets/TTS, {removed} old clips removed." +
                              (string.IsNullOrEmpty(log) ? "" : "\n" + log));
                });
        }

        /// <summary>Deletes clip files that no phrase in the current preload uses. Returns how many.</summary>
        private static int RemoveStaleClips(string folder, TTSPreloadData data)
        {
            if (!Directory.Exists(folder)) return 0;
            var wanted = new HashSet<string>(data.voices.SelectMany(v => v.phrases).Select(p => p.clipID).Where(id => !string.IsNullOrEmpty(id)));
            if (wanted.Count == 0) return 0;

            int removed = 0;
            foreach (string file in Directory.GetFiles(folder))
            {
                string name = Path.GetFileName(file);
                if (name.EndsWith(".meta") || name == "manifest.txt" || !name.StartsWith("tts_")) continue;
                if (wanted.Contains(Path.GetFileNameWithoutExtension(name))) continue;
                File.Delete(file);
                if (File.Exists(file + ".meta")) File.Delete(file + ".meta");
                removed++;
            }
            return removed;
        }

        /// <summary>
        /// Lists the clip files in StreamingAssets/TTS/manifest.txt. On Android the app copies them
        /// out of the APK at launch (PreloadInstaller) and needs the list to know what is there.
        /// Returns the number of clip files.
        /// </summary>
        private static int WriteManifest(string folder)
        {
            if (!Directory.Exists(folder)) return 0;
            var names = Directory.GetFiles(folder)
                .Select(Path.GetFileName)
                .Where(n => !n.EndsWith(".meta") && !n.EndsWith(".tmp") && n != "manifest.txt")
                .OrderBy(n => n)
                .ToList();
            File.WriteAllText(Path.Combine(folder, "manifest.txt"), string.Join("\n", names) + "\n");
            return names.Count;
        }

        /// <summary>The service needs a disk cache handler or it never reads or writes clip files.</summary>
        private static void EnsureDiskCacheHandler(TTSService service)
        {
            if (service.DiskCacheHandler != null) return;
            TTSDiskCache cache = service.GetComponent<TTSDiskCache>();
            if (cache == null) cache = Undo.AddComponent<TTSDiskCache>(service.gameObject);
            service.DiskCacheHandler = cache;
            EditorUtility.SetDirty(service);
            EditorSceneManager.MarkSceneDirty(service.gameObject.scene);
            Debug.Log("[GazeBoard] Added a TTSDiskCache handler to the TTS service. Save the scene.");
        }

        private static List<string> CollectPhrases(BoardDefinition definition)
        {
            var set = new HashSet<string>();
            if (definition != null)
            {
                foreach (BoardCategory category in definition.categories)
                {
                    foreach (TileData tile in category.tiles)
                    {
                        if (tile.kind == TileKind.Phrase && !string.IsNullOrWhiteSpace(tile.Text)) set.Add(tile.Text);
                    }
                }
            }
            foreach (string s in SpokenPhrases.All()) set.Add(s);
            return set.OrderBy(s => s).ToList();
        }
    }
}

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

        [MenuItem("GazeBoard/Preload Board Phrases (Wit, needs internet)")]
        private static void PreloadPhrases()
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

            string voiceId = speaker.VoiceID;
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
                    int files = Directory.Exists(folder) ? Directory.GetFiles(folder).Count(f => !f.EndsWith(".meta")) : 0;
                    Debug.Log($"[GazeBoard] Preload finished: {ok}/{total} phrases downloaded, {files} clip files in StreamingAssets/TTS." +
                              (string.IsNullOrEmpty(log) ? "" : "\n" + log));
                });
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

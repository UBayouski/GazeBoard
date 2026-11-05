using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace GazeBoard.Notify
{
    /// <summary>
    /// Sends what the user says to a paired phone through ntfy (https://ntfy.sh), a free
    /// publish-subscribe service: one HTTPS POST per message, no account, no backend. The
    /// caregiver installs the ntfy app and subscribes to this headset's topic, which is a random
    /// code generated on first use and shown on the Settings page. One-way only; the headset
    /// never receives anything. Requires internet; the chime and voice work regardless.
    /// </summary>
    public sealed class PhoneNotifier : MonoBehaviour
    {
        private const string EnabledKey = "gazeboard.phone.enabled";
        private const string CodeKey = "gazeboard.phone.code";
        private const string Server = "https://ntfy.sh";
        public const string TopicPrefix = "gazeboard-";

        private static PhoneNotifier _instance;
        private static bool _enabled;
        private static string _code;

        /// <summary>Result of the last send, for the Settings page: null until something was sent.</summary>
        public static bool? LastSendOk { get; private set; }

        public static PhoneNotifier Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("PhoneNotifier");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<PhoneNotifier>();
                }
                return _instance;
            }
        }

        /// <summary>Whether spoken sentences are forwarded to the phone. Persisted.</summary>
        public static bool Enabled
        {
            get { Load(); return _enabled; }
            set
            {
                Load();
                _enabled = value;
                PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        /// <summary>The pairing code; the ntfy topic is TopicPrefix + Code.</summary>
        public static string Code
        {
            get { Load(); return _code; }
        }

        public static string Topic => TopicPrefix + Code;

        /// <summary>Forwards a sentence if phone alerts are on.</summary>
        public static void Say(string text)
        {
            if (!Enabled) return;
            Send(text, "GazeBoard", urgent: false);
        }

        /// <summary>Forwards a help call as an urgent notification if phone alerts are on.</summary>
        public static void Help(string text)
        {
            if (!Enabled) return;
            Send(text, "Help needed", urgent: true);
        }

        /// <summary>Sends regardless of the Enabled setting, so pairing can be checked.</summary>
        public static void SendTest(Action<bool> done)
        {
            Send("GazeBoard is paired with this phone.", "GazeBoard", urgent: false, done);
        }

        private static void Send(string text, string title, bool urgent, Action<bool> done = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Instance.StartCoroutine(Instance.Post(text, title, urgent, done));
        }

        private IEnumerator Post(string text, string title, bool urgent, Action<bool> done)
        {
            string url = Server + "/" + Topic;
            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(text));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "text/plain; charset=utf-8");
            request.SetRequestHeader("Title", title);
            request.SetRequestHeader("Priority", urgent ? "urgent" : "default");
            request.SetRequestHeader("Tags", urgent ? "rotating_light" : "speech_balloon");
            request.timeout = 10;

            yield return request.SendWebRequest();

            bool ok = request.result == UnityWebRequest.Result.Success;
            LastSendOk = ok;
            if (ok) Debug.Log($"[GazeBoard.Phone] Sent to {Topic}: \"{text}\"");
            else Debug.LogWarning($"[GazeBoard.Phone] Send failed ({request.responseCode} {request.error}): \"{text}\"");
            done?.Invoke(ok);
        }

        private static void Load()
        {
            if (_code != null) return;
            _enabled = PlayerPrefs.GetInt(EnabledKey, 0) == 1;
            _code = PlayerPrefs.GetString(CodeKey, "");
            if (_code.Length == 0)
            {
                // Letters and digits that cannot be confused when read aloud or typed.
                const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
                var sb = new StringBuilder();
                var random = new System.Random();
                for (int i = 0; i < 8; i++) sb.Append(alphabet[random.Next(alphabet.Length)]);
                _code = sb.ToString();
                PlayerPrefs.SetString(CodeKey, _code);
                PlayerPrefs.Save();
            }
        }

        /// <summary>The code read out character by character, so a caregiver can type it in.</summary>
        public static string SpokenCode()
        {
            var sb = new StringBuilder("Phone code: ");
            foreach (char c in Code)
            {
                sb.Append(char.IsDigit(c) ? c.ToString() : c.ToString().ToUpperInvariant()).Append(", ");
            }
            return sb.ToString().TrimEnd(' ', ',');
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
        }
    }
}

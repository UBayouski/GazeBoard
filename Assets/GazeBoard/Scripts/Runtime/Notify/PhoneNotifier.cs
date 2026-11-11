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
    ///
    /// Privacy: the topic is not protected by a password. Anyone who knows the code can read
    /// what is sent to it, so the code is long, random and hard to guess, and it can be replaced
    /// at any time (NewCode). That is a private code, not authenticated pairing.
    ///
    /// "Sent" means the service accepted the message. Whether the phone showed it depends on
    /// the phone, so nothing here ever claims that a person received it.
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

        /// <summary>
        /// Raised after every send. First argument: the service accepted the message. Second:
        /// it was a help call. The board uses it to show when an alert did not go out.
        /// </summary>
        public static event Action<bool, bool> WhenSent;

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
            // Neither the topic nor, in a release build, the sentence goes to the log: the topic
            // is the only thing that keeps the messages private.
            if (ok) Debug.Log($"[GazeBoard.Phone] Accepted by the notification service: {LogText.Of(text)}");
            else Debug.LogWarning($"[GazeBoard.Phone] Send failed ({request.responseCode} {request.error}): {LogText.Of(text)}");
            done?.Invoke(ok);
            WhenSent?.Invoke(ok, urgent);
        }

        private static void Load()
        {
            if (_code != null) return;
            _enabled = PlayerPrefs.GetInt(EnabledKey, 0) == 1;
            _code = PlayerPrefs.GetString(CodeKey, "");
            if (_code.Length == 0) NewCode();
        }

        /// <summary>
        /// Replaces the pairing code. The old topic stops receiving anything, so a phone that
        /// was paired (or anyone else who had the code) must be given the new one.
        /// </summary>
        public static void NewCode()
        {
            // Letters and digits that cannot be confused when read aloud or typed.
            const string alphabet = "abcdefghjkmnpqrstuvwxyz23456789";
            const int length = 8;
            var sb = new StringBuilder();
            // The code is the only secret, so it comes from the system's secure generator.
            using (var random = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                var one = new byte[1];
                while (sb.Length < length)
                {
                    random.GetBytes(one);
                    // Drop values that would favour the first letters of the alphabet.
                    if (one[0] >= 256 - 256 % alphabet.Length) continue;
                    sb.Append(alphabet[one[0] % alphabet.Length]);
                }
            }
            _code = sb.ToString();
            PlayerPrefs.SetString(CodeKey, _code);
            PlayerPrefs.Save();
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

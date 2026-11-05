#if UNITY_ANDROID && !UNITY_EDITOR
using System;
using UnityEngine;

namespace GazeBoard.Speech
{
    /// <summary>
    /// Requests Android audio focus for this app. Horizon OS mutes an app's audio through
    /// AppOps while the system shell still holds focus, which happens when the app is
    /// launched over adb (Build And Run) rather than from the Library. Asking for focus
    /// explicitly hands it to us in both cases.
    /// </summary>
    internal static class AndroidAudioFocus
    {
        private const string Tag = "[GazeBoard.Speech]";
        private const int StreamMusic = 3;
        private const int AudioFocusGain = 1;
        private const int RequestGranted = 1;

        private static AndroidJavaObject _audioManager;
        private static FocusListener _listener;

        public static void Request()
        {
            try
            {
                if (_audioManager == null)
                {
                    using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    _audioManager = activity.Call<AndroidJavaObject>("getSystemService", "audio");
                    _listener = new FocusListener();
                }

                int result = _audioManager.Call<int>("requestAudioFocus", _listener, StreamMusic, AudioFocusGain);
                Debug.Log($"{Tag} Audio focus request result: {(result == RequestGranted ? "granted" : "failed (" + result + ")")}.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} Audio focus request threw: {e.Message}");
            }
        }

        public static void Abandon()
        {
            try
            {
                if (_audioManager != null && _listener != null)
                {
                    _audioManager.Call<int>("abandonAudioFocus", _listener);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} Audio focus abandon threw: {e.Message}");
            }
        }

        private sealed class FocusListener : AndroidJavaProxy
        {
            public FocusListener() : base("android.media.AudioManager$OnAudioFocusChangeListener") { }

            // Called by Android on its main thread. Logging only; Unity keeps playing regardless.
            public void onAudioFocusChange(int focusChange)
            {
                Debug.Log($"{Tag} Audio focus changed: {focusChange}");
            }
        }
    }
}
#endif

# GazeBoard

A gaze-only communication board (AAC) for Meta Quest. Look at a phrase tile and hold your gaze to
select it; the headset speaks it aloud. Every tile can also be chosen with a pinch, so the app is
fully usable by hand as well.

- Head gaze on Quest 3S / Quest 3, eye gaze on devices with eye tracking (automatic).
- Dwell selection with a visible progress ring, adjustable dwell time.
- Speech through the Meta Voice SDK (Wit.ai).

Built for the Meta VR Start Developer Competition 2026, Productivity track.

## Requirements

- Unity 6000.3.27f2 with the Android module
- Meta XR All-in-One SDK 207 (restored from `Packages/manifest.json`)
- A Meta Quest headset in developer mode

## Speech setup (required once per clone)

Speech uses Wit.ai, and the access tokens are deliberately not committed.

1. Create a free app at https://wit.ai (language English, visibility Private) and copy its
   **Server Access Token** from the app's Settings page.
2. In Unity open **Meta > Voice SDK > Get Started**, paste the token, click Link, and save the
   configuration as `Assets/GazeBoard/WitConfiguration-GazeBoard.asset`.
3. Select the `TTSWitService` object in the scene and make sure its Configuration field points at
   that asset.

The asset, `ProjectSettings/wit.config` and the Wit export folder are git-ignored.

## Editor commands

Under the **GazeBoard** menu:

- **Add Head Gaze Fallback To Scene**: makes the gaze block work on headsets without eye tracking.
- **Add Dwell To Buttons In Selection**: adds dwell selection and a progress ring to every Button under
  the selected object.
- **Add Speak To Buttons In Selection**: makes every Button under the selection speak its label.
- **Test Speech (Play Mode)**: speaks a test phrase through the active speech backend.

## Building

File > Build Profiles > Android > Build And Run. APKs go to `Builds/` (ignored).

Note: Horizon OS mutes an app that is started with a bare component intent, which is how Unity's
Build And Run launches it over adb (the PLAY_AUDIO app-op flips to "ignore"). Launching with the
LAUNCHER and `com.oculus.intent.category.VR` categories, as the Library does, keeps audio on.
A post-build hook in `HeadsetAudioUnmute.cs` waits for Unity's launch and then restarts the app that
way. If it is ever silent, run **GazeBoard > Headset: Relaunch App** or start it from
**Library > Unknown Sources**. Store installs are not affected. Manual equivalent:

```
adb shell am start -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -c com.oculus.intent.category.VR -n com.ubayouski.gazeboard/com.unity3d.player.UnityPlayerGameActivity
```

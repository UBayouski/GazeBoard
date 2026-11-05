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

Note: when launched over adb (which Build And Run does), Horizon OS may keep audio focus in the
system shell and mute the app. The app requests audio focus on start to counter this; if it is still
silent, launch it from **Library > Unknown Sources** on the headset.

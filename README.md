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
An app launched while the headset is off (asleep) is muted too, whichever intent is used, and
putting the headset on does not unmute it. A post-build hook in `HeadsetAudioUnmute.cs` waits for
Unity's launch and for the headset to be worn, then restarts the app the right way. If it is ever
silent, put the headset on and run **GazeBoard > Headset: Relaunch App**, or start it from
**Library > Unknown Sources** while wearing it. Store installs are not affected. Manual equivalent:

```
adb shell am start -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -c com.oculus.intent.category.VR -n com.ubayouski.gazeboard/com.unity3d.player.UnityPlayerGameActivity
```

## Board features

- Categories down the left (Mine, Quick, Needs, Feelings, People, Spell), each with its own colour.
- Fixed quick row: **Yes**, **No**, **Help** (attention chime, then "I need help"), **Repeat**, **Save**.
- **Spell** shows three predicted words above the letters; words you have spoken rank first.
- **Save** turns the sentence into a tile under **Mine**, together with your recent sentences.
- Settings (gear in the sentence bar): dwell time, gaze on/off, voice speed, theme, passthrough or calm room.

Setup in a fresh clone: **GazeBoard > Setup Board (assets + definition + UI)** generates the sprites
and font assets, writes the default board, and rebuilds the board UI in the open scene.

## Phone alerts (optional)

Everything the user says, and every Help call, can be forwarded to a caregiver's phone through
[ntfy](https://ntfy.sh), a free notification service. No account is needed on either side.

1. On the phone, install the ntfy app (Android or iOS) and subscribe to the topic
   `gazeboard-` followed by the code shown on the second Settings page (the Code tile reads it aloud).
2. On the headset, turn **Phone** on in Settings and choose **Test phone**. The phone gets a message.

Help calls arrive as urgent notifications. The headset needs internet for this; the chime and the
voice work without it. Messages pass through ntfy.sh unencrypted, so keep the code private.

## Credits

- [Atkinson Hyperlegible](https://www.brailleinstitute.org/freefont) by Braille Institute, SIL Open Font License (`Assets/GazeBoard/Licenses`).
- [Lucide](https://lucide.dev) icons, ISC licence.
- Word frequency list derived from [FrequencyWords](https://github.com/hermitdave/FrequencyWords) (OpenSubtitles), MIT licence.

# GazeBoard Privacy Policy

Effective date: November 11, 2026

GazeBoard is a communication board for Meta Quest headsets, made by Uladzislau Bayouski ("I",
"me"). This policy explains what information the app handles, where it goes, and what you can do
about it.

## Summary

- GazeBoard has no accounts, no advertising and no analytics of its own.
- What you save in the app stays on your headset.
- Text leaves the headset in two cases only: when a new sentence is turned into speech, and when
  you switch on phone alerts.

## Information that stays on your headset

GazeBoard stores the following on the headset, and I never receive it:

- phrases you save, your recent sentences, and words the prediction has learned from you;
- your settings (hold time, voice speed, theme, board position and size, and similar);
- the pairing code for phone alerts;
- audio clips of sentences the app has already spoken, so they can be spoken again offline.

Uninstalling GazeBoard deletes all of it.

## Information that leaves your headset

**Speech.** The board's built-in phrases are spoken from audio stored in the app and need no
internet connection. When you speak a sentence that is not built in, for example one you spelled,
its text is sent to Wit.ai, a speech service operated by Meta, which returns the audio. The text
is sent without your name or account. Meta's handling of it is described in Meta's own terms and
privacy policy for Wit.ai (https://wit.ai/terms).

**Phone alerts (off by default).** If you switch on phone alerts in Settings, each phrase or
sentence you speak, and each Help call, is sent over an encrypted connection (HTTPS) to ntfy.sh, a
public notification service, under a random code created on your headset. A phone subscribed to
that code receives the messages. Please be aware:

- the code is not protected by a password, so anyone who knows it can read messages sent to it;
- you can replace the code at any time with "New code" in Settings, which stops the old code from
  receiving anything new;
- ntfy.sh keeps messages for a limited time, as described in its own privacy policy
  (https://ntfy.sh/docs/privacy/);
- with phone alerts off, nothing is sent to ntfy.sh.

## Sensors and permissions

- **Head and hand tracking** are used on the headset to aim at and select tiles. This data is not
  stored and is not sent anywhere by GazeBoard.
- **Passthrough** (seeing your room) is drawn by the headset itself. GazeBoard does not receive,
  store or send camera images.
- **Microphone.** The app's package lists the microphone permission because a software library it
  includes declares it. GazeBoard does not ask for microphone access, and does not record or send
  audio from the microphone.
- **Internet** is used only for the two cases described above.

## Meta platform

GazeBoard is built with Meta's software development kits and runs on Meta Horizon OS. Meta may
collect information about your use of the headset and of apps under its own privacy policy
(https://www.meta.com/legal/privacy-policy/). I do not receive that information, apart from the
email address and alias Meta shares with a developer when you join a test release of the app.
I use those only to manage access to that release.

## Children

GazeBoard is not directed to children under 13 (or the applicable age in your region), and I do
not knowingly collect information from them.

## Your choices

- Keep phone alerts off, or switch them off at any time.
- Replace the phone pairing code at any time.
- Remove saved phrases in the app's "Mine" page.
- Uninstall the app to delete everything it stored.

## Changes

If this policy changes, the new version will be published at https://github.com/UBayouski/GazeBoard/blob/main/PRIVACY.md with a new
effective date.

## Contact

Questions about this policy: open an issue at https://github.com/UBayouski/GazeBoard/issues

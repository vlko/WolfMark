# WolfMark for Android

A share target and a read-only viewer for the notes WolfMark keeps in OneDrive.

- **Share** a link, a selection, a whole text or images from any app → a note `YYYY/MM/DD/Share_HHmm.md` (tag
  `Share`) in your notes folder. On Wi-Fi the app first tries a computer where WolfMark runs with *Settings →
  Sharing from the phone* switched on; otherwise it writes the note through OneDrive. Without a connection the
  share waits on the phone and is sent later, once.
- **Read** your notes: days, tag filter, rendered Markdown with images. A copy is kept on the phone for reading
  without a connection. The app never changes or deletes a note.

Design and rejected alternatives: [`docs/adr/0017-android-share-lan-onedrive.md`](../docs/adr/0017-android-share-lan-onedrive.md),
SPEC §4.8.

## One-time setup: the Microsoft app registration

The app signs in to OneDrive with MSAL, which needs an app registration of your own (free, a few minutes):

1. [Microsoft Entra admin center](https://entra.microsoft.com) → *App registrations* → *New registration*.
   Supported account types: **Personal Microsoft accounts only**.
2. *Authentication* → *Add a platform* → **Android**. Package name `io.github.vlko.wolfmark`, and the signature hash
   of the key the APK is signed with – the build prints it:
   ```sh
   ./gradlew :app:printSignatureHash
   ```
   Several hashes can be registered (this machine's debug key, your own key below).
3. *API permissions* → *Add* → Microsoft Graph → Delegated → **Files.ReadWrite**.
4. Put the *Application (client) ID* into `android/local.properties` (not committed):
   ```properties
   wolfmark.msalClientId=00000000-0000-0000-0000-000000000000
   ```

Without it the app still builds and says on its first screen what is missing. The hash is read from the signing
key at build time; `wolfmark.msalSignatureHash` overrides it.

## A signing key of your own

A debug build is signed with the debug key of the machine it is built on – every machine and every CI run has
another, so its builds neither install over each other nor share a signature hash. One key of your own fixes that:

```sh
keytool -genkeypair -keystore wolfmark.jks -alias wolfmark -keyalg RSA -keysize 4096 -validity 10000
```

Keep the file and its password safe and out of the repository (`*.jks` is ignored): an app can only be updated by
a build signed with the same key. Then, in `local.properties`:

```properties
wolfmark.keystore=/path/to/wolfmark.jks
wolfmark.keystorePassword=…
wolfmark.keyAlias=wolfmark
```

Debug and release builds are then both signed with it (`./gradlew :app:assembleRelease`). Register its hash
(`printSignatureHash`) in Entra.

**GitHub.** With these in *Settings → Secrets and variables → Actions*, every CI run attaches an installable
`WolfMark-android-debug` APK and a release tag publishes `WolfMark-<version>-android.apk`:

| Kind | Name | Value |
|---|---|---|
| secret | `ANDROID_KEYSTORE_BASE64` | `base64 -w0 wolfmark.jks` |
| secret | `ANDROID_KEYSTORE_PASSWORD` | the keystore password |
| secret | `ANDROID_KEY_ALIAS` | `wolfmark` |
| secret | `ANDROID_KEY_PASSWORD` | only if the key has a password of its own |
| variable | `WOLFMARK_MSAL_CLIENT_ID` | the client ID (not a secret – it is in every APK) |

Without the secrets CI still tests and builds, and a release simply has no APK.

## Building

JDK 21 and the Android SDK (platform 37); Android Studio brings both.

```sh
cd android
./gradlew :core:test            # name rules, protocol, OneDrive client, queue, mirror – no device needed
./gradlew :app:assembleDebug    # app/build/outputs/apk/debug/app-debug.apk
./gradlew :app:lintDebug        # warnings are errors
./gradlew :app:assembleRelease -Pwolfmark.version=0.9.0   # signed only with a key of your own (below)
adb install -r app/build/outputs/apk/debug/app-debug.apk
```

## Layout

| Module | What |
|---|---|
| `core` | Plain Kotlin/JVM, no Android: `NoteName`/`NotePath`/`TagName` (the desktop's rules), `ShareFrame`/`ShareMessage`/`ShareClient` (the LAN protocol), `ShareComposer` (shared content → Markdown), `GraphClient` (OneDrive over `HttpURLConnection`), `ShareQueue` + `ShareDelivery`, `NoteMirror` (the phone's copy, through the change feed). |
| `app` | Android: `ShareActivity` (stores the share, shows a toast, goes away), `DeliveryWorker` (WorkManager), `Auth` (MSAL), `MainActivity` + Compose screens. |

`core` is tested against the same vectors as the desktop – [`testdata/note-names.json`](../testdata/note-names.json)
and [`testdata/share-frames.json`](../testdata/share-frames.json). A change to the file-name rules or to the
protocol changes both implementations and those files together.

## Things to know

- The notes folder defaults to `WolfMark` in the root of OneDrive (the desktop's default); if it is not there the
  app asks. Change it later from the menu.
- Computers are found through `.wolfmark/share/*.json` in the notes folder, read when the notes are refreshed and
  at most every 12 hours while sharing. After switching receiving on at the desktop, press *Refresh* once.
- Images the desktop cannot show (HEIC, AVIF) are stored as JPEG.
- A shared link is stored as a link: the app does not download the page.
- MSAL adds the NFC permission to the manifest (security keys at sign-in); the app itself does not use NFC.

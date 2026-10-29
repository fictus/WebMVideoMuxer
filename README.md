# WebM Audio Editor

A small Windows desktop app for adding an audio track to a **WebM video with transparency (alpha)**, lining it up visually on a timeline, and exporting the result with the transparency intact.

It looks and behaves like a simplified video editor: the video preview sits at the top, the audio track sits at the bottom, and you drag the audio clip until it lines up with the picture. Press play to hear and see both together, then click **EXPORT**.

Built with .NET MAUI (Blazor Hybrid). All the heavy lifting is done by [FFmpeg](https://ffmpeg.org/).

---

## Features

- Load a transparent `.webm` video and an audio file (`.wav`, `.mp3`, `.ogg`, `.flac`, `.m4a`)
- Video preview over a checkerboard so you can see the transparency
- Audio waveform on a zoomable timeline
- **Drag the audio clip** to set where it starts
- **Drag the clip's edges** to trim the start and end of the audio
- Play / pause / stop that plays video and audio **together**
- Click or drag on the ruler to scrub
- Snapping to time 0, the playhead, and the end of the video
- Precise nudge buttons (±0.01 s and ±0.1 s) and a numeric start field
- One-click export with a clear success / failure banner and a "Show in folder" button
- The video stream is **copied, not re-encoded**, so alpha is preserved and export is fast

---

## Requirements

| Requirement | Notes |
|---|---|
| Windows 10 (1809 / build 17763) or later | Windows only |
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | |
| .NET MAUI workload | `dotnet workload install maui` |
| [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) | Already installed on current Windows 10/11 |
| [FFmpeg](https://ffmpeg.org/download.html) | `ffmpeg.exe` on your `PATH`, or browse to it in the app |

---

## Build and run

```powershell
git clone <your-repo-url>
cd WebmEditor

dotnet workload install maui          # first time only
dotnet run -f net10.0-windows10.0.19041.0
```

To make a release build:

```powershell
dotnet publish -c Release -f net10.0-windows10.0.19041.0
```

The app is built as an unpackaged Windows app, so no signing or MSIX install is needed.

---

## How to use it

1. **Open video…** and choose your transparent `.webm`.
2. **Open audio…** and choose your audio file. The waveform appears in the bottom track.
3. Press **Space** (or the ▶ button) to play the video and audio together.
4. Line the audio up:
   - Drag the audio clip left or right.
   - Or use the **Audio start** nudge buttons / number box for fine control.
   - Zoom in with the **Zoom** slider for frame-accurate placement.
5. Trim the audio if needed by dragging the clip's left or right edge.
6. Check the **Output** path at the bottom (it defaults to `<video name>_with_audio.webm` next to your video). Edit it if you want it somewhere else.
7. Click **EXPORT**. A banner shows progress, then a green **Export complete** message (with **Show in folder**) or a red failure message.

### Controls

| Control | What it does |
|---|---|
| ⏮ | Jump to the start |
| ▶ / ⏸ (or **Space**) | Play / pause video and audio together |
| ⏹ | Stop and return to the start |
| Click / drag on the ruler or empty track | Move the playhead |
| Drag the audio clip | Change when the audio starts |
| Drag the clip's left edge | Trim the start of the audio (the audio's content stays in place) |
| Drag the clip's right edge | Trim the end of the audio |
| `-0.1` `-0.01` `+0.01` `+0.1` | Nudge the audio start (seconds) |
| **Audio start (s)** box | Type an exact start time |
| **Zoom** slider | Zoom the timeline |

### Tips

- **Line up with a specific moment:** pause the video on the frame where the sound should begin, then drag the clip so its left edge snaps to the playhead.
- **Snapping:** while dragging, the clip's start snaps to `0` and to the playhead, and its end snaps to the end of the video.
- **Audio needs to start *before* the video:** drag the clip to the left of `0` (the dark area on the ruler), or trim its start. On export, anything that would play before the video begins is cut off automatically.
- **Audio longer than the video:** the export is always capped at the video's length, so the extra audio is dropped.
- **Audio shorter than the video:** the rest of the video simply has no sound.

---

## What export does

EXPORT runs FFmpeg with a command in this shape:

```
ffmpeg -y -i video.webm
       -ss <trim in>  -t <audio length to keep>  -i audio.wav
       -map 0:v:0 -map 1:a:0
       -c:v copy
       -af adelay=<delay ms>:all=1
       -c:a libopus -b:a 128k
       -t <video duration>
       output.webm
```

- `-c:v copy` copies the video stream untouched, which is what keeps the **alpha channel** intact (and makes export fast).
- Only the audio is encoded, to **Opus at 128 kbps** (WebM requires Opus or Vorbis, not raw WAV).
- `-ss` / `-t` before the audio input apply the trim; `adelay` applies the start offset.
- The exact command that was run is printed at the top of the log each time.

To confirm transparency survived, run `ffprobe output.webm` and look for `alpha_mode=1` in the video stream tags.

---

## Project layout

```
WebmEditor/
├── WebmEditor.csproj          Windows-only MAUI Blazor Hybrid project (net10.0-windows)
├── MauiProgram.cs             App startup (dev tools enabled in Debug builds)
├── App.xaml(.cs)              MAUI application / window
├── MainPage.xaml(.cs)         Hosts the BlazorWebView
├── MediaHost.cs               Makes local files reachable by the WebView (see below)
├── AudioSyncEditor.razor      Top bar, export bar, status banner, file pickers, FFmpeg export
├── Platforms/Windows/         WinUI app entry and manifest
└── wwwroot/
    ├── index.html
    ├── css/app.css            Dark editor theme
    └── js/editor.js           Timeline, waveform, dragging, trimming, synced playback
```

### How it works

- **Preview:** the video and audio play in the embedded WebView2 browser using standard HTML5 elements. WebView2 (Chromium) renders VP9 transparency correctly, which is why the app uses a web UI instead of native MAUI media controls.
- **Timeline and sync:** `editor.js` keeps a single clock and re-syncs the video and audio elements whenever they drift by more than about 0.15 s. The preview is a close approximation; the exported file is the source of truth.
- **Getting local files into the WebView:** `MediaHost` offers each file at two addresses, and the page tries them in order: a WebView2 virtual host mapped to the file's folder, then a tiny loopback-only HTTP server (`127.0.0.1`) with Range request support. The server only serves files you have opened in the app and is not reachable from other machines.
- **Export:** the page reports the final audio placement (delay, trim in, kept length) to C#, which builds the FFmpeg arguments and runs it.

---

## Troubleshooting

The log at the bottom of the window shows what happened when you opened each file and the full FFmpeg output during export.

| Symptom | Likely cause / fix |
|---|---|
| `Could not start ffmpeg` | FFmpeg isn't on your `PATH`. Put the full path to `ffmpeg.exe` in the **ffmpeg** box at the top (or use the **…** button). |
| `Video failed: media error code 4 …` | The browser couldn't load or decode that file. The log lists each address it tried and the error. Check that the file plays in Chrome/Edge. |
| `Audio failed: Failed to fetch` or a decode error | Same idea for the audio; try converting it to a standard 16-bit PCM `.wav`. |
| Video loads but looks opaque (no checkerboard showing through) | The source may not actually contain alpha. Check with `ffprobe` for `alpha_mode=1`. |
| Red banner: "output must be a different file than the source video" | Change the **Output** path; FFmpeg can't overwrite its own input. |
| Red banner: "audio clip is empty" | The trims (or placing the clip before the video) removed all of the audio. |
| Red banner: "Export failed (ffmpeg exit code N)" | Read the FFmpeg error lines in the log just above the banner. |
| Export succeeded but the audio is slightly off | Zoom in and nudge by 0.01 s, then export again. Judge final sync from the exported file in a media player. |

**Debugging the UI:** in a Debug build (`dotnet run`), press **F12** or right-click and choose **Inspect** in the app window to open the browser developer tools.

---

## Known limitations

- Windows only.
- Output audio is always Opus at 128 kbps.
- Very long audio at high zoom is limited by browser canvas size; the app reduces the zoom automatically when needed.
- The preview uses the browser's decoders, so a file that FFmpeg can read may still fail to preview. If that happens, convert it to `.wav` (audio) or a standard VP9 `.webm` (video).

---

## License

Add your license here.

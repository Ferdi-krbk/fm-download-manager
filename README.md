# MC Download Manager for Windows (port)

C# / .NET 8 port of [mstcvk/mc-download-manager](https://github.com/mstcvk/mc-download-manager).

```
src/McDm.Core    engine + queue + persistence + browser bridge (127.0.0.1:48213)   <- cross-platform, tested
src/McDm.App     WPF shell: window, tray icon, start-with-Windows                    <- Windows only
tests/McDm.Tests end-to-end tests against a local Range-capable HTTP server         <- `dotnet run`
```

## Build (Windows, .NET 8 SDK)
```
dotnet sln add src/McDm.App     # once: the app project could not be added on Linux
dotnet build -c Release
dotnet run --project tests/McDm.Tests     # engine + bridge tests
dotnet run --project src/McDm.App
dotnet publish src/McDm.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Status
Done + tested: segmented downloads (Range, adaptive pool on 429/503, retries, pause/resume, restart resume),
unknown-length streams, redirects (cookies dropped cross-host), safe file names, bridge (Origin/Host checks).
Not done: video (yt-dlp/ffmpeg), settings UI, TR/EN localisation, installer, real icon, clipboard watcher,
connecting the original Chrome extension (needs a check against Extension/ in the upstream repo).

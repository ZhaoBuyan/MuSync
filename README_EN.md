# MuSync

[中文](README_CN.md) ｜ **English**

By [ZhaoBuyan](https://github.com/ZhaoBuyan)

MuSync is a Windows desktop tool that syncs the playback status of your music player — and the running state of any app — to your Steam profile status. Your friends will see what you're listening to (and what you're running) right in their Steam friends list.

What friends actually see:

![Steam friends list](docs/steam-preview.png)

The status text is fully user-defined (template engine + block editor). For example:

```
Playing a non-Steam game
Listening to: Rice Field - Jay Chou [#####-----] 2:30/4:15     ← while listening
Listening to: Rice Field - Jay Chou [❤️❤️❤️❤️❤️❤️💕💕💕💕] 2:30/4:15  ← progress-bar style & length are yours to pick; emoji paste right in
VS Code ‖ Listening to: Rice Field - Jay Chou                     ← coding with music (combined template)
Strinova                                                           ← playing a non-Steam game
```

## Features

### App sync

- Sync the running state of any app to Steam: games, work apps (e.g. VS Code), anything
- A built-in dictionary of ~50 common apps plus fullscreen detection automatically sorts apps into Game / Work / Media / Social (all adjustable)
- New apps are queued for confirmation automatically; you can also add the current foreground app from Settings
- Two display modes: `When in foreground` (hidden when you switch away) / `Always when running` (shown while idle too)
- System processes and music players are excluded by default; apps categorized as "Ignore" never affect the current status

### Music sync

- Supports NetEase Cloud Music / QQ Music / LX Music / KuGou Music
- Multiple players at once are arbitrated automatically: now-playing wins; player priority is reorderable in Settings and applies instantly
- Music and app sync have independent switches; with "Hide music status when paused" enabled, a paused player never shows up in your friends list

### Display customization

- Block editor: compose the status text from blocks — Song / Artist / Progress bar / App name / Separator / Custom text — reorder freely with a live preview
- Template presets: one click to switch between "Simple / With prefix (Playing·Listening) / Name only"
- Progress bar style: presets plus any characters you like, length 1–50; emoji work out of the box, e.g. `[❤️❤️❤️❤️❤️❤️💕💕💕💕] 1:59/3:32`
- Appearance: title / song colors, font, background color, background image (stretch / fit / tile / center) — all customizable with one-click reset
- Combined display and separator are configurable; text over Steam's limit is truncated intelligently at 128 bytes

### Steam connection

- Connects directly to the Steam network via SteamKit2; the Steam client doesn't need to be running
- Automatic reconnect with exponential backoff; signs back in with the saved token after recovery
- Falls back to WebSocket (port 443) when TCP fails; the server list is cached locally so it can connect even when official endpoints are unreachable; automatic server switching and retries up to 4 rounds when the server asks you to try another CM
- Auto-pauses while you play a real Steam game, resumes when you exit the game
- The login token is encrypted with Windows DPAPI; network hiccups never wipe your login state
- Sync rate options: Fast 0.25s / Standard 0.5s (recommended) / Data saver 1s

### Desktop & settings

- Two panels on the main window: "Music" (follows the current player) and "App" — with live sync status; double-click the song title to open its page in your browser
- Lives in the notification area: hover shows the current status, right-click offers "Pause sync" for temporary invisibility, optional start with Windows
- The Settings dialog is a single window with tabs: General / Sync / Display / Apps / Diagnostics / About
- Update check: on launch, then every 6 hours; new versions surface on the "Settings" button and tray menu; the update dialog can download directly — the full/lite editions use "Exit & open folder" for a manual replace, the installer edition updates with one click ("Update now")
- The Diagnostics tab offers "Open logs folder"; logs live in `%LocalAppData%\MuSync\logs\`

## Usage

1. Download **MuSync.exe** (full edition, recommended — a portable single file, just double-click) from [Releases](https://github.com/ZhaoBuyan/MuSync/releases); **MuSync-Setup.exe** (installer edition, one-click auto-updates afterwards) is also available
2. Sign in to Steam on first launch (mobile authenticator / email code supported). If you hit an "unusual login" warning, follow the in-window instructions: Steam app on your phone → select "Steam Client" → confirm your location
3. Open your music player and syncing starts automatically
4. Want friends to see which app you're using? Settings → Sync → enable "Sync non-game apps"
5. Upgrading: the app checks for updates automatically — for the full/lite editions, download then click "Exit & open folder" and drag to replace; for the installer edition, click "Update now" and it's done

> **LX Music users**: please enable "Open API service" under LX Music → Settings → Open API, and allow LAN access.

> **Co-existing with the Steam client**: signing in to Steam on the same account from both MuSync and the Steam client won't log either out (both are SteamKit sessions). You'll see a device session named MuSync in Steam's account management — that's expected.

> **Which edition?** We recommend **MuSync.exe** (full edition) — a portable single file, just double-click; **MuSync-Setup.exe** (installer edition) suits people who want zero maintenance — new versions auto-update with one click and no manual file replacement; **MuSync-lite.exe** is smaller but requires the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0). All three editions are functionally identical and share the same config & login.

## FAQ

- **Sign-in keeps failing / "Server busy" (TryAnotherCM)**
  This is Steam's risk control reacting to a "new device + network environment change" — it usually clears up within minutes (MuSync automatically switches servers and retries, up to 4 rounds). If it happens often: turn off Steam network tools such as Steam++ (Watt Toolkit) and retry; if you use an accelerator, try to keep the same node.

- **A friend might not see you**
  In rare cases the Steam client's friends list "drops" an individual friend entry, so that person can't see you (your profile still works). This is almost certainly on the Steam client side — right-click refresh or restart the Steam client to recover.

- **LX Music is not detected**
  Please enable "Open API service" under LX Music → Settings → Open API, and allow LAN access.

- **Will my settings and login survive an upgrade?**
  Yes. Config and login token live in `%LocalAppData%\MuSync\config.json`, separate from the program files, and are picked up automatically after replacing the program (the token is DPAPI-encrypted, so the same PC and user won't need to sign in again).

- **Config or login state looks broken**
  If config parsing fails, the app backs it up automatically (`config.json.bak`) and keeps as much as it can. Logs are in `%LocalAppData%\MuSync\logs` (Settings → Diagnostics → "Open logs folder").

## Build & test

```powershell
dotnet build MuSync.sln -c Release     # build
dotnet test  MuSync.sln                # unit tests (87)

# Portable single-file publish (bundled .NET runtime)
dotnet publish MuSync.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish

# Installer (requires Inno Setup 6.5+)
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

CI (pushes to `main` / `v*` tags) builds three artifacts: `MuSync.exe` (portable), `MuSync-lite.exe` (lightweight, requires the .NET 9 runtime), `MuSync-Setup.exe` (installer).

## Relationship to upstream projects

MuSync's player memory-reading implementation descends from an open-source lineage (all MIT):

```
MuSync (this project)
 └─ wuyan1337/yySync ............... direct basis (Steam variant)
     └─ kriYamiHikari/Music-DiscordRPC
         └─ Kxnrl/NetEase-Cloud-Music-DiscordRPC
             └─ Copyright (c) 2018 Kyle — the original project
```

Only the player memory reverse-engineering (NetEase patterns / QQ Music offsets / LX API) comes from that lineage; the connection layer, state arbitration, display engine, app sync, and the config & security model are all independently implemented in this project. As a note: the Chinese description 「半新写」 literally means "only half rewritten". The full list of changes relative to yySync is in [CHANGELOG.md](CHANGELOG.md).

## Project layout

```
MuSync.csproj                  # main app (WinForms)
Program.cs                     # entry / tray / update checks
MainForm / SettingsForm        # main window / settings dialog (single window, tabs)
FormatBlockEditorForm          # block editor
UpdateForm                     # update dialog (download / open release page)
SteamLoginForm                 # Steam sign-in window
Players/                       # player readers (NetEase / QQ / LX Music / KuGou)
Utils/                         # foreground watcher / classifier / template codec / update / logging / DPAPI, etc.
Models/                        # data models
Win32Api/                      # Win32 / process memory access
installer/                     # Inno Setup installer scripts & local build
tests/MuSync.Tests/            # xUnit unit tests (87)
reference-yySync/              # upstream reference sources (MIT, not compiled, for comparison only)
```

## Security & privacy

Before trusting MuSync with your Steam account, please read [SECURITY.md](SECURITY.md): the sign-in flow, what is read, network traffic, risks, and how to revoke access — all laid out plainly.

## License & credits

MuSync is built on an open-source lineage (see "Relationship to upstream projects" above). Thanks to everyone who paved the way:

- [wuyan1337/yySync](https://github.com/wuyan1337/yySync) — direct basis
- [kriYamiHikari/Music-DiscordRPC](https://github.com/kriYamiHikari/Music-DiscordRPC)
- [Kxnrl/NetEase-Cloud-Music-DiscordRPC](https://github.com/Kxnrl/NetEase-Cloud-Music-DiscordRPC)
- [SteamRE/SteamKit](https://github.com/SteamRE/SteamKit) — Steam network protocol support

Released under the MIT license, see [LICENSE](LICENSE). Full third-party notices in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

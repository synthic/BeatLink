# BeatLink

***There you go, you're off the Grid.***

Designed and developed for the [Beat Revival](https://beatrevival.me) project.

## Usage

1. Run the app and click **Log In**.
2. A browser window opens asking you to authenticate. While it waits, the
   button becomes **Cancel**.
3. When the page says you can return to the app, come back to BeatLink. The
   button now reads **Log Out** and shows a tick.
4. Start *Mirror's Edge™ Catalyst*. **Connect** enables itself once the game is
   detected.
5. Click **Connect**.

Click **Log Out** to delete the stored session.

No configuration is needed: the Beat Revival client id and server are built in.

## Configuration

Everything below is optional. BeatLink reads `beatlink.json` from
`%LOCALAPPDATA%\BeatLink\` if it exists. See
[`beatlink.example.json`](beatlink.example.json) for the full set of keys.

| Key | Meaning | Default |
| --- | --- | --- |
| `ClientId` | Discord application (public) client id. | Beat Revival app |
| `ServerHost` | Beat Revival redirector hostname the game is redirected to. | `redirector.ploxxy.dev` |
| `Scope` | OAuth scope requested from Discord. | `identify` |
| `RedirectPort` | Loopback port for the OAuth redirect. | `14000` |
| `DisableEncryption` | Also apply the request-encryption bypass. | `false` |

The server can also be changed for a single run with `--server <host>`, or from
the **Server: …** link at the bottom of the window (max 29 ASCII characters).

The Beat Revival Discord application is already configured for this (public
client, redirect `http://localhost:14000/`), so no setup is needed to use the
default client id. If you point `ClientId` at your own Discord app instead, that
app must register the redirect `http://localhost:14000/` (adjust the port to
match `RedirectPort`) and be a public client.

## Game build

The patcher writes to offsets relative to the game's module base, writes data
before the pointers that reference it and the activating code last, uses
full-width values, and checks every write's byte count — so a half-applied or
partial write can't silently leave the game inconsistent.

Connect works out of the box against the supported build. Optionally, with an
**unpatched** game running you can capture the original bytes once (launch with
`--capture`); they are stored in `%LOCALAPPDATA%\BeatLink\expected-bytes.json`. If
that file is present, Connect additionally verifies each site before writing
(refusing an unexpected build) and can report "already patched" on a second
press. Without it, the patches are simply applied.

## Troubleshooting

- **"Mirror's Edge™ Catalyst is not running."** Start the game first; Connect
  enables itself when the process is found.
- **"Access to the game process was denied."** Run BeatLink as administrator.
- **"This is likely an unsupported or updated game build."** The bytes at a patch
  site did not match. Re-capture the expected bytes against an unpatched game, or
  the build is not supported.
- **Login never completes.** Check that port `14000` (or your `RedirectPort`) is
  free and that `ClientId` is set.
- Logs are written to `%LOCALAPPDATA%\BeatLink\beatlink.log` (tokens redacted).

## Project layout

| Project | Target | Purpose |
| --- | --- | --- |
| `BeatLink` | `net10.0-windows` | The WinUI 3 app. |
| `BeatLink.Core` | `net10.0` | Platform-neutral OAuth, token storage and patch planning. |
| `BeatLink.Tests` | `net10.0` | Tests for the core (run with `dotnet run`). |

## Building

1. Install Visual Studio 2026 (with the .NET desktop and WinUI workloads), or the
   .NET 10 SDK.
2. Clone the repository.
3. Build and test the core:
   ```
   dotnet run --project BeatLink.Tests
   ```
4. Build the app (Windows only):
   ```
   dotnet build BeatLink/BeatLink.csproj -p:Platform=x64
   ```
   Or, in Visual Studio, right-click the **BeatLink** project and choose
   **Publish**.

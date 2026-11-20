# BeatLink

***There you go, you're off the Grid.***

Designed and developed for the [Beat Revival](https://beatrevival.me) project.

## Usage

1. Run the app and click **Log In**.
2. A browser window opens asking you to authenticate. While it waits, the Log In
   button becomes **Cancel**.
3. When the page says you can return to the app, come back to BeatLink.
4. Start *Mirror's Edge™ Catalyst*. **Connect** enables itself once the game is
   detected.
5. Click **Connect**.

Use **Log Out** to delete the stored session.

## Configuration

BeatLink reads an optional `beatlink.json` from `%LOCALAPPDATA%\BeatLink\`. See
[`beatlink.example.json`](beatlink.example.json) for the full set of keys.

| Key | Meaning | Default |
| --- | --- | --- |
| `ClientId` | Discord application (public) client id. Required to log in. | *(empty)* |
| `ServerHost` | Beat Revival redirector hostname the game is redirected to. | `redirector.ploxxy.dev` |
| `Scope` | OAuth scope requested from Discord. | `identify` |
| `RedirectPort` | Loopback port for the OAuth redirect. | `14000` |
| `DisableEncryption` | Also apply the request-encryption bypass. | `false` |

The server can also be overridden for a single run with `--server <host>`, or in
the window's **Server** field.

A Discord application must register the redirect URI
`http://127.0.0.1:14000/callback/` (adjust the port to match `RedirectPort`) and
be configured as a public client.

## Supported game build

The patcher writes to offsets relative to the game's module base and verifies the
original bytes before writing, so it refuses to run against an unexpected or
updated build rather than corrupting it.

Because the expected bytes are specific to a game copy, they are captured on your
machine rather than shipped. With an **unpatched** game running, capture them
once; they are stored in `%LOCALAPPDATA%\BeatLink\expected-bytes.json`. Until they
are captured, Connect reports that the build is unverified instead of writing
anything.

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

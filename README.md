# Claude Code Usage Monitor

A small desktop app that shows your current Claude Code usage limits at a glance. It reads the OAuth credentials that Claude Code already stores on disk, polls Anthropic's usage endpoint once every two minutes, and renders three gauges: the rolling 5-hour window, the 7-day overall window, and the 7-day Sonnet-specific window.

![Claude Code Usage Monitor](media/ClaudeUsageMonitor.png)

## Features

- Live gauges for the 5-hour, weekly, and weekly-Sonnet usage windows
- Countdown showing when each window resets
- Desktop notifications when a gauge crosses 70%, 80%, or 90%
- Optional minimize-to-tray with a tooltip summary of current utilization
- Automatic OAuth token refresh; rate-limit aware (backs off on 429)

## How it works

The app reads `~/.claude/.credentials.json` — the same file Claude Code writes — and calls `https://api.anthropic.com/api/oauth/usage` on your behalf. Refreshed tokens are written back to the same file so Claude Code and the monitor stay in sync. No API key or separate login is required; if you're signed into Claude Code, the monitor works.

App settings (e.g. minimize-to-tray) live in `~/.claude/usage-monitor-settings.json`.

## Building

Built with .NET 10 and [Avalonia](https://avaloniaui.net/) 12. The project targets native AOT.

```bash
dotnet build
dotnet run
```

To publish a trimmed, AOT-compiled binary:

```bash
dotnet publish -c Release
```

A `claude-usage-monitor.desktop` file is included for Linux desktop integration; edit the `Exec`/`Icon` paths to match where you install the binary.

## Requirements

- An active Claude Code session (the monitor reuses its credentials)
- .NET 10 SDK to build from source

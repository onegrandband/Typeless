# Typeless

A Windows auto-typing / OCR assistant built for .NET 8.

## Features

- Keystroke or clipboard-paste auto typing
- Screen reader (OCR) using Windows built-in OCR engine
- Smart prompt parsing: extracts only the word/phrase the game/app asks for
- Topmost overlay so it stays visible over fullscreen games
- Setup wizard with desktop / Start Menu shortcut creation
- Uninstall flow

## Requirements

- .NET 8 SDK / runtime
- Windows 10 19041+ (for WinForms + Windows.Media.Ocr)
- Windows OCR language pack installed (usually present by default for English)

## Build

```bash
dotnet build -c Release
```

## Publish single-file (Windows)

```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

## Cross-platform note

WinForms and the Windows OCR/screen-capture APIs are Windows-only, so this app targets
`net8.0-windows10.0.19041.0`. To make it truly cross-platform for macOS/Linux, I'll
need to rewrite/update the UI and input logic ]with a cross-platform UI framework
(e.g. Avalonia UI or .NET MAUI) and platform-specific OCR/input providers:

- macOS: `AVCaptureScreenInput`/`CGDisplayStream` for capture, `Vision` framework for OCR, `CGEventPost` for input.
- Linux: `scrot`/`grim`/`maim` for capture, `Tesseract` for OCR, `xdotool`/`libevdev` for input.

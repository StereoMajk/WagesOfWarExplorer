# Wages of War Explorer

A file browser for the game data of **Wages of War: The Business of Battle** (1996, New World Computing).

![Explorer screenshot](screenshots/screenshot.png)

## What it does

Browses the extracted installer groups and ISO game files, detecting and rendering each format natively:

## Requirements

- .NET 8 SDK (`net8.0-windows`)
- Windows (WPF)

## Building

```
dotnet build
```

## Data

The game files were extracted from the installer inside the ISO using [unshield](https://github.com/twogood/unshield), which unpacks InstallShield cabinet archives into numbered groups (`Group1`–`Group13`).

Point the app at those extracted groups or the ISO game directory. The extracted data is **not** included in this repository.

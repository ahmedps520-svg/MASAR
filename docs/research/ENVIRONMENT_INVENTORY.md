# MASAR: Development Environment Inventory (Phase 0, Step A)

Inspected on 2026-11-14 by running commands on the development PC. Each entry was checked directly, not assumed.

## Hardware (development PC)

| Item | Found |
|---|---|
| OS | Windows 11 Pro 10.0.26300 |
| CPU | Intel Core i5-14600K (14 cores / 20 threads) |
| RAM | 32 GB |
| GPU | NVIDIA GeForce RTX 4060 Ti (driver 32.0.16.1088) + Intel UHD 770 (iGPU) |
| Disk free | C: 222 GB · D: 916 GB · E: 505 GB |

## Toolchain

| Tool | Status | Version / path |
|---|---|---|
| Git | Installed | 2.55.0 |
| Git LFS | Installed | 3.7.1 |
| GitHub CLI | Installed, **logged in** (scopes: repo, workflow, gist, read:org) | `gh` |
| Visual Studio | Installed | VS Community 2022 17.14 (Game dev with C++ + Desktop C++ workloads) |
| .NET | **Runtime only** (8.0.12). No .NET SDK | — |
| Python | Installed | 3.13.15 (also 3.11) via `py` launcher. The bare `python` command is the Microsoft Store stub |
| Node.js | Installed | 24.19.0 |
| Java | Installed | 17.0.4.1 |
| CMake / clang / gcc | Not found on PATH | — |

## Game engines

| Engine | Status | Notes |
|---|---|---|
| **Unreal Engine 5.8.1** | Installed at `D:\UE_5.8` (Epic Launcher) | Platforms folder has Android, IOS, VisionOS, Windows. **No `Engine/Binaries/IOS`**, so the iOS target-platform binaries are not installed. The engine's Apple SDK config targets Xcode/iOS SDK 26.x |
| Unity | Not installed at inspection time. **Unity Hub 3.22.6 installed on 2026-11-14** (MSIX via winget). Unity **6000.3.28f1 LTS + iOS Build Support** installing to `E:\Unity\Editors` | Chosen engine, see TECHNICAL_ARCHITECTURE.md |
| Godot | **Not installed** | Godot 4.7.2 is available via winget |
| Native Metal | n/a | Needs Apple toolchain. See research |

An unrelated UE C++ project on this machine shows the UE C++ toolchain (MSVC 14.44, Windows SDK 10.0.22621) building green.

## Content tools

| Tool | Status |
|---|---|
| Blender | **5.2.0 LTS** installed |
| Quixel Bridge / Fab plugin | Installed for UE 5.8 (license limits apply outside Unreal; see research) |
| GIMP / Krita / Audacity | Not found |

## Apple / device connectivity

| Item | Status |
|---|---|
| Mac | **None available** (confirmed by the user) |
| Xcode | Not available (macOS only) |
| Apple Devices app (Windows) | Installed (1.1792). Gives Windows the USB driver to talk to an iPad |
| iTunes | Folder present |
| iPad Pro M5 | **Not connected** to the PC during inspection |
| Apple Developer Program membership | **Paid membership: yes** (confirmed by the user on 2026-11-14) |

## Input hardware

| Item | Status |
|---|---|
| Logitech G HUB | Installed (`C:\Program Files\LGHUB`) |
| Logitech G923 | **Not connected** to the PC during inspection. Only a Logitech LIGHTSPEED receiver (VID 046D, PID C53F) was present |

## Build limitations identified

1. **No macOS = no local Xcode.** Every iPadOS build must be compiled and signed on macOS. Options are cloud macOS CI (GitHub Actions is usable: `gh` is authenticated) or a borrowed or rented Mac.
2. **UE 5.8 iOS support isn't installed**, and UE C++ iOS builds need a Mac (verification in the research doc).
3. **The project started inside OneDrive. Resolved on 2026-11-14.** Game-engine caches (Unity `Library/`) hold tens of thousands of churning files, which cause sync locks, sync conflicts and `.git` corruption risk. The repository was moved to **`E:\MASAR`**, a local, unsynced NTFS drive. Unity editors install to `E:\Unity\Editors`.
4. **No .NET SDK.** This only matters for engine-independent unit tests run outside an engine.

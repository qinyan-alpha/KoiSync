# KoiSync v1.0.0 — 6-DOF Hardware Sync, 50Hz Studio Baker & AI Director for Koikatsu (KK) & Koikatsu Sunshine (KKS)

[![Version](https://img.shields.io/badge/release-v1.0.0-8B4661.svg)](#installation)
[![Games](https://img.shields.io/badge/games-Koikatsu%20(KK)%20%7C%20Sunshine%20(KKS)-DE8BA6.svg)](#installation)
[![License](https://img.shields.io/badge/license-MIT-E3BB8E.svg)](LICENSE)
[![Languages](https://img.shields.io/badge/UI-8%20Languages-231F2D.svg)](#key-features)

> **Pure 3D skeletal-geometry 6-DOF hardware sync** (`L0`, `L1`, `L2`, `R0`, `R1`, `R2`) for **SR6 / OSR2 / OSR2+ / SSR1 (TCode v0.3 Serial & UDP)** and **Intiface Central / Buttplug v3 (The Handy & linear devices)**.  
> *All real-time motion and 50Hz Studio `.funscript` baking are computed directly from in-game 3D bone kinematics — never AI video tracking or heuristic guessing.*

📖 **Complete Documentation**:
- **English**: [Markdown Guide](docs/KoiSync_User_Guide_EN.md) · [Illustrated Word Manual (`KoiSync_User_Guide_EN.docx`)](docs/KoiSync_User_Guide_EN.docx)

---

## Key Features

- **Unified KK & KKS Architecture (`MainGame` + `CharaStudio`)**: Ships natively compiled builds for both **Koikatsu (`.NET 3.5` / Unity 5.6)** and **Koikatsu Sunshine (`.NET 4.6` / Unity 2019.4)** sharing one physical solver.
- **True 6-Axis SR6 / OSR2 Kinematics (`L0–R2`)**: Strict **TCode v0.3 / `nb-3d-simulator`** polarity across all 6 axes, built-in SR6 linkage-coupled roll inverse kinematics, C1 soft anti-clipping rotation shoulders, and a 3-tier global Feel Layer (**Raw 1:1** / **Balanced** / **Intense**) with full **`0–99`** L0 travel unlocked by default.
- **WYSIWYG 3D X-Ray Preview & Direct In-Game 3D Drag Calibration**:
  - Translucent **pink wireframe sleeve** (device cup pose) + **cyan fixed reference rod** (shaft centerline) rendered right in the 3D scene.
  - **Direct in-game 3D handle dragging**: Drag **shaft origin / tip** directly in 3D via **Scene Handles** in MainGame, align **vagina / opening target entry** with live 3D Observed / Calibrated / Output crosshairs, and drag all **6 colored 3D handles** (`Penis tip`, `Origin · Axial / L1 / L2`, `L1 ± mm`, `L2 ± mm` + L1/L2 plane rectangle) directly in CharaStudio.
- **CharaStudio Step-Bake & 50Hz 6-Axis Funscript Baker**:
  - Automatic Timeline prescan (`Step-Bake`), per-chapter `BetterPenetration` / actor / opening assignment, Climax chapter vibration modes, and **6D Research Grinding & Skeleton / DynamicBone Coupling**.
  - One-click offline **50Hz (`0.02s`)** export of complete **MultiFunPlayer 6-axis packs** (`.funscript`, `.surge`, `.sway`, `.twist`, `.roll`, `.pitch`).
- **40-Tool AI MCP Server (`koisync-director`) & Optional AI Voice Director**:
  - Localhost-only (`127.0.0.1`) HTTP bridges (`41289` MainGame / `41290` Studio) + zero-dependency Python stdio MCP server (`mcp/koisync_director_mcp.py`) exposing **40 tools** for Antigravity, Claude Desktop, or Cursor (pose/stage/speed control, 60Hz Timeline pose-variant engine, EdgeGuard climax braking, facial expressions, clothing, screenshots, Studio Step-Bake & 50Hz baking).
  - Optional local **AI Voice Director** sidecar (`voice_director/`, `127.0.0.1:41295`, Web Console `/ui`) with real-time voice/text chat, ASR/TTS, lip-sync RMS, long-term memory, and **SR6 6-DOF Math Manifold Lab**.
  - Hard safety guardrails: loopback-only, instant `F9` / UI panic cut, `SixAxisDrive` servo speed caps, and dead-man TTL auto-release.
- **Deep Mod Ecosystem (`BetterPenetration`, `kPlug`, `ksPlug`)**:
  - Automatic BP / BP 4.5 support with vanilla-bone fallback.
  - Pure runtime-reflection bridge for **KK `kPlug`** and **KKS `ksPlug` (v1.5.0)**: auto-discovers 137+ extra H-animations, enables guest invite / swap / masturbate / striptease / dance / dismiss, and automatically pauses `girlThink` / `MgThink + GgThink` conflict arbitration while AI Director is active.
- **Silent Startup & 100% 8-Language UI**: Starts completely hidden (`F8` MainGame / `F7` Studio) with full live translation across **English, Simplified Chinese, Traditional Chinese, Russian, Japanese, Korean, German, and French**.

---

## Supported Hardware & Protocols

| Transport | Protocol & Default | Axes | Compatible Devices |
| :--- | :--- | :---: | :--- |
| **Serial** | **TCode v0.3** ASCII (`0000–9999`), `COM4` @ `115200` baud | **All 6 (`L0–R2`)** | **SR6**, **OSR2 / OSR2+**, **SSR1** & any USB TCode controller (direct Win32 `kernel32.dll` I/O — works on both KK and 64-bit KKS with zero `System.dll` hacks) |
| **UDP** | **TCode v0.3** ASCII over UDP, `127.0.0.1:8000` | **All 6 (`L0–R2`)** | Wi-Fi ESP32 TCode controllers, TCode 3D simulators, UDP relays |
| **Intiface (L0)** | **Buttplug v3 WebSocket**, `ws://127.0.0.1:12345` | **`L0` only** | **The Handy**, Keon, Lovense linear devices via **Intiface Central** |

---

## Installation

Download the archive matching your game from **[Releases (v1.0.0)](../../releases/tag/v1.0.0)** and extract directly into your game root folder:

- **Koikatsu (KK)**: `KoiSync-v1.0.0-KK.zip`
- **Koikatsu Sunshine (KKS)**: `KoiSync-v1.0.0-KKS.zip`

```text
<GameRoot>\
├─ BepInEx\plugins\
│  ├─ KoiSyncMainGame\KoiSync.MainGame.dll
│  └─ KoiSyncStudio\KoiSync.Studio.dll
└─ mods\qinnyann\KoiSync\
   └─ [qinnyann] KoiSync Device Preview Assets v1.0.0.zipmod
```

- **Default Hotkeys**:
  - **MainGame**: `F8` Toggle Panel · `F9` Toggle Device Sync (Kill-Switch) · `F10` Floating Six-Axis Monitor · `V` Voice PTT
  - **CharaStudio**: `F7` Toggle Panel · `Space` Toggle Device Sync

---

## AI MCP Server Quick Setup (`koisync-director`)

Point any MCP-compatible client (Antigravity, Claude Desktop, Cursor) to `mcp/koisync_director_mcp.py` (requires Python 3.9+, zero third-party pip packages):

```json
{
  "mcpServers": {
    "koisync-director": {
      "command": "python",
      "args": ["<path-to-repo>/mcp/koisync_director_mcp.py"]
    }
  }
}
```

See [mcp/README.md](mcp/README.md) and the full [User Guide](docs/KoiSync_User_Guide_EN.md) for all 40 MCP tools and optional Voice Director setup (`voice_director/chat_config.example.json`).

---

## Building from Source

Requires the .NET SDK and a local Koikatsu (KK) and/or Koikatsu Sunshine (KKS) installation with BepInEx 5 (game assemblies are referenced locally and never redistributed in this repository):

```powershell
# Build KK (net35) & run all 12,292 unit / drive / transport checks
powershell -ExecutionPolicy Bypass -File .\build.ps1 -GameRoot "E:\Galgames\kk"

# Build KKS (net46) and optionally deploy to your KKS folder
powershell -ExecutionPolicy Bypass -File .\build-kks.ps1 -KksGameRoot "E:\Galgames\kks" -Deploy
```

---

## License

Released under the [MIT License](LICENSE). Third-party bundled dependency notices are embedded in `src/Libraries/`.

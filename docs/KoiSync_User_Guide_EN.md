# KoiSync — Complete User Guide (KK / KKS · MainGame & CharaStudio)

> **Pure 3D skeletal-geometry 6-DOF hardware sync & 50Hz script baker for Koikatsu (KK) and Koikatsu Sunshine (KKS)**  
> Supports **SR6 / OSR2 / OSR2+ / SSR1 (TCode v0.3 Serial & UDP)** and **Intiface Central / Buttplug v3 (L0 Linear devices such as The Handy)**  
> **Versions**: `KoiSync.MainGame v1.0.0` · `KoiSync.Studio v1.0.0` · **8-Language UI** (`zh`, `zh-Hant`, `en`, `ru`, `ja`, `ko`, `de`, `fr`)

---

## 1. Overview & Key Features

**KoiSync** computes real-time **6-DOF (6-axis)** device motion directly from in-game 3D character bone geometry — **not** video analysis or AI-generated script guessing. Both MainGame and Studio share the exact same physical solver:

- **Full 6-Axis SR6 / OSR2 Geometry Solver (`L0`, `L1`, `L2`, `R0`, `R1`, `R2`)**: Tracks penetration depth (`L0`), front/back surge (`L1`), left/right sway (`L2`), axial twist (`R0`), lateral roll (`R1`), and pitch (`R2`) aligned strictly to the standard **TCode v0.3 / `nb-3d-simulator`** physical frame, including SR6 linkage coupling compensation and C1 soft rotation limits.
- **WYSIWYG 3D X-Ray Device Preview**: Displays a translucent **pink sleeve mesh** (device cup pose) and **cyan reference rod** (shaft centerline) directly in the 3D scene so you can verify depth, entry alignment, and pitch/roll direction at a glance.
- **3-Tier Feel Layer (Full 0–99 Travel)**: Switch seamlessly between **Raw Geometry (1:1)**, **Balanced Feel (Recommended)**, and **Intense Feel**, with full `0–99` L0 travel unlocked by default (no player-length bottom clipping).
- **Studio Step-Bake & 50Hz 6-Axis Funscript Baking**: Automatically scans Studio `Timeline` scenes into chapters, supports interactive 3D drag handles, chapter climax & 6D pelvic grinding / skeleton reverse-coupling, and offline-bakes standard **MultiFunPlayer 6-axis `.funscript` bundles** at 50Hz.
- **Native Ecosystem Integration**: Works seamlessly with `BetterPenetration` (BP / BP 4.5, with automatic vanilla-bone fallback), `Timeline`, `kPlug` (KK), and **`ksPlug` (KKS — including 137+ extra H-animations, character invite/swap/guest actions, and automatic `MgThink`/`GgThink` conflict arbitration)**.
- **Silent Startup & 8-Language UI**: Starts completely hidden without popping up over your game (`F8` in MainGame, `F7` in Studio), and supports live hot-switching across **English, Simplified Chinese, Traditional Chinese, Russian, Japanese, Korean, German, and French**.

---

## 2. Requirements & Supported Hardware

### 2.1 Game & Plugin Dependencies
- **Games**: Koikatsu (`Koikatu.exe`, `.NET 3.5` build) or Koikatsu Sunshine (`KoikatsuSunshine.exe` / `KoikatsuSunshine_VR`, `.NET 4.6` / Unity 2019.4 build). **Use the DLL build matching your game — do not mix KK and KKS DLLs.**
- **Framework**: BepInEx 5 (`x64`).
- **Required for Studio**: `ExtensibleSaveFormat` (`KK_ExtensibleSaveFormat` / `KKS_ExtensibleSaveFormat`).
- **Strongly Recommended**: `BetterPenetration` (`KK_BetterPenetration` / `KKS_BetterPenetration` and Studio counterparts), `Sideloader` (for 3D preview assets), and `Timeline` (for Studio scenes).
- **Optional Integrations**: `kPlug` (KK) or `ksPlug` (KKS) — pure runtime reflection, zero hard dependency.

### 2.2 Hardware & Communication Protocols

| Transport | Protocol & Default Endpoint | Axes | Compatible Devices & Notes |
| :--- | :--- | :---: | :--- |
| **Serial** | **TCode v0.3** ASCII (4-digit `0000–9999`, `I10`/`I20`, stops with `DSTOP\n`) · Default `COM4` @ `115200` baud | **All 6** (`L0–R2`) | **SR6**, **OSR2 / OSR2+**, **SSR1** & any USB TCode firmware controller. Direct Win32 `kernel32.dll` I/O — works out-of-the-box on both KK and 64-bit KKS without replacing `System.dll`. |
| **UDP** | **TCode v0.3** ASCII over UDP · Default `127.0.0.1:8000` | **All 6** (`L0–R2`) | Wi-Fi ESP32 TCode devices (enter LAN IP & port), local TCode 3D simulators, or UDP relays (connectionless protocol). |
| **Intiface (L0)** | **Buttplug v3 WebSocket** (`ws://127.0.0.1:12345`, loopback only, device index `0`) | **L0 only** | **The Handy**, Keon, Lovense linear devices, and any `LinearCmd`-capable hardware connected through **Intiface Central**. |

> **Note on MultiFunPlayer**: You do **not** need MultiFunPlayer for real-time gameplay — KoiSync drives Serial/UDP/Intiface directly. When baking in Studio, KoiSync exports standard **MultiFunPlayer 6-axis files** (`.funscript`, `.surge`, `.sway`, `.twist`, `.roll`, `.pitch`) ready for MultiFunPlayer, HereSphere, XBVR, or ScriptPlayer.

---

## 3. Installation & Folder Structure

Install the matching release bundle into your KK or KKS game root:

```text
<GameRoot>\
├─ BepInEx\
│  └─ plugins\
│     ├─ KoiSyncMainGame\
│     │  └─ KoiSync.MainGame.dll      # MainGame H-scene plugin (KK or KKS build)
│     └─ KoiSyncStudio\
│        └─ KoiSync.Studio.dll        # CharaStudio plugin (KK or KKS build)
└─ mods\
   └─ qinnyann\
      └─ KoiSync\
         └─ [qinnyann] KoiSync Device Preview Assets v1.0.0.zipmod  # Shared KK/KKS 3D preview assets
```

- **Configs** (auto-generated on first run): `BepInEx\config\local.koisync.maingame.cfg` and `local.koisync.studio.cfg`.
- **Saved Calibrations & Baked Scripts**: `UserData\KoiSyncMainGame\` and `UserData\KoiSyncStudio\`.

---

## 4. MainGame Mode (`KoiSync.MainGame`) Guide

### 4.1 Default Hotkeys
| Hotkey | Action | Notes |
| :---: | :--- | :--- |
| **`F8`** | Toggle Main Panel | Hidden on startup; closing also hides floating calibration handles |
| **`F9`** | Toggle Device Sync (Kill-Switch) | Immediately sends `DSTOP` and cuts AI takeover when turned off |
| **`F10`** | Toggle Floating **Six-Axis Monitor** | Compact standalone monitor you can keep open while playing |
| **`V`** | AI Voice Push-to-Talk | Active only when AI Voice Sidecar is enabled; rebindable to `Mouse3`/`Mouse4` |

### 4.2 Pages & Controls
1. **Device Page**:
   - **Device Connection**: Switch between `Serial`, `UDP`, and `Intiface (L0)`. For USB controllers, click **Scan Ports**, choose your controller's COM port (e.g. `COM3`/`COM4`), leave baud at `115200`, and click **Connect**.
   - **Device Sync**: Click **Enable Sync** (or press `F9`) once in an active H-scene; click **Open Six-Axis Monitor** to open the `F10` overlay.
   - **Sync & Output Response**: Tune **Send Delay (ms)** (wireless/Bluetooth compensation, `0` = off), **Live Smoothing (ms)** (default `25 ms`), and **Output Speed Limit (%/s)** (default `450 %/s` servo safety cap).
2. **Workbench → Current Action**:
   - Displays live pose name, stage progress bar (`WLoop` / `SLoop` / `OLoop`), bound **Source / Target / Effector / Target Zone** (clickable to manually override rare third-party mod poses), **Action Catalogue** browser (including KK `kPlug` / KKS `ksPlug` extra animations), and real-time **BP 4.5 channel / Visible Chain / Contact Distance / Insertion % / Depth (mm)** diagnostics.
3. **Workbench → Calibration**:
   - **Source Span & Auto Response**: Displays auto-detected shaft length (`mm`), lets you offset origin/tip in `Axial / L1 / L2 (mm)` or drag directly in 3D via **Scene Handles**, and adjust **L1 ± mm / L2 ± mm** sensitivity (smaller mm = stronger lateral/surge response).
   - **Target Entry Offset & Manual Rotation Plane**: Fine-tune per-zone entry offsets, toggle **Show Entry Markers** in 3D, or enable **Manual Span** to unlock `R0 Twist` / `R1 Roll` / `R2 Pitch` plane offsets.
   - **3D Preview Bar**: Toggle **Preview**, **X-Ray**, **Handles**, and switch between **Geometry** (raw bones) and **Output** (final device pose).
4. **Workbench → Sync & Feel**:
   - **Pose Rotation Centering**: **Soft Anti-Bottoming (Default)** uses C1 soft shoulder compression on steep poses (cowgirl/doggy) to prevent servo binding; **Relative Centering** zeroes rotation around your current posture.
   - **L0 Travel Range (Default 0–99)**: Full `0–99` range active by default with a one-click **Reset full 0–99 range** button.
   - **Feel Layer (Global)**: **Raw Geometry (1:1)**, **Balanced Feel (Recommended)**, or **Intense Feel**, plus non-shaft foreplay behavior (`Gentle Foreplay` / `Full Sync` / `Standby`) and **Outer L0** ceiling (`99%`).
   - **Local AI Director Bridge**: Optional `127.0.0.1:41289` loopback HTTP/MCP bridge with emergency **Cut AI Takeover** button.
5. **Workbench → Save / Record**: Persist per-action calibrations and record live 6-axis `.funscript` files during gameplay.
6. **AI Hub (`Real-time Chat` / `Manual Director` / `Autonomy & SR6 Math Lab`)**:
   - **Live Chat**: Connect to the optional local Voice Director sidecar (`127.0.0.1:41295`, Web Console `/ui`) with LLM switching, push-to-talk/free-mic ASR/TTS, lip-sync RMS, and persistent preference memory.
   - **Manual Director (100% Offline)**: Trigger 60Hz pose-variant choreography (`Organic`, `Deep grind`, `9S 1D`, `Tip rub`, `1S 1D`, `Circle / Fig-8 / F/B / L/R`) with live sliders, quick pose switches, single-piece half/full undress, 7 outfit slots, expression presets, and **`kPlug` (KK) / `ksPlug` (KKS) character invite, swap & guest action controls**.
   - **SR6 6-DOF Math Manifold Lab & Conflict Arbitration**: Layer 5 live 6-DOF mathematical trajectories (`Pivot Wave`, `Dual Helix`, `6D Lissajous`, `Beat Flutter`, `Smooth Chaos`) with dual-actor pelvis & DynamicBone soft-tissue coupling, while automatically pausing `kPlug.girlThink` or KKS `ksPlug.Settings.MgThink + GgThink` whenever AI Director is active.
7. **Direct In-Game 3D Handle Dragging**:
   - **MainGame**: Enable **Manual span** on the Calibration tab and click **Scene handles** to collapse the window into a slim top bar and left-drag **`Origin`** and **`Tip`** directly on the 3D shaft, while live 3D **`Observed` / `Calibrated` / `Output`** crosshairs let you align the vagina/opening entry millimeter-by-millimeter.
   - **CharaStudio**: Enable **Show drag handles** on the Calibration tab to drag all **6 colored 3D handles** (`Penis tip`, `Origin · Axial / L1 / L2`, `L1 ± mm`, `L2 ± mm` + L1/L2 plane rectangle) directly in the viewport.
8. **40-Tool AI MCP Server (`mcp/koisync_director_mcp.py`)**:
   - Zero-dependency Python stdio MCP server bridging localhost-only `127.0.0.1:41289` (MainGame) and `127.0.0.1:41290` (Studio) to Antigravity, Claude Desktop, or Cursor — covering pose/stage/speed/expression/clothes/guest controls, 60Hz Timeline variants, EdgeGuard braking, live screenshots, and Studio Step-Bake + 50Hz batch baking with 4 C# safety guardrails (loopback-only, `F9` panic cut, `SixAxisDrive` speed caps, dead-man TTL).
9. **Floating Six-Axis Monitor (`F10`)**:
   - Shows **Pink (geometry input)** vs **Gold (final output)** bars for `L0–R2`. Drag upper/lower limit bars to restrict travel, toggle per-axis checkboxes (e.g., disable `L1/L2/R0` on an OSR2), and set per-axis gain (`× 1`).

---

## 5. CharaStudio Mode (`KoiSync.Studio`) Guide

### 5.1 Default Hotkeys
- **`F7`**: Toggle KoiSync Studio panel (silent on startup; first press opens immediately).
- **`Space`**: Toggle live hardware sync output (ignored while typing in text boxes; rebindable to `Mouse3`/`Mouse4` in **Setup**).

### 5.2 Pages & 50Hz Baking Workflow
1. **◈ Device**: Same Serial / UDP / Intiface connection and response sliders (Studio defaults: `60 ms` send delay, `25 ms` smoothing, `200 %/s` speed cap). *Send delay affects real-time playback only — never baked `.funscript` timestamps.*
2. **❖ Workbench (Top Collapsible Timeline Bar)**: Play / Pause / Stop / ±1s step / Seek / Zoom (`Ctrl + Wheel`), left-click chapters to select & jump, right-click to rename or delete.
3. **Chapter Editing Tab**:
   - Click **Fast Prescan (Step-Bake)** first! It scans the entire Timeline at high speed (`20Hz`), detects camera/pose transitions, and records real depth bounds per chapter.
   - Split at playhead, merge, undo/redo, or override per-chapter **Source / Target / Opening** (Vaginal, Anal, Oral, Hands, Chest, or auto-follow BP), and mark cutscenes as **No Motion**.
4. **Calibration Tab**:
   - Fine-tune shaft length, origin offsets, `L1 / L2 ± mm` mapping ranges, and enable **Show Drag Handles** to adjust origin, tip, and L1/L2 bounds directly in the 3D viewport.
5. **Sync & Feel Tab**:
   - Pick global Feel preset (**Raw 1:1** / **Balanced Recommended** / **Intense**), configure **Climax Chapters** (`Smart` / `Fixed Frequency` / `Simulated` / `Follow L0 Cycle`), or layer **6D Research Grinding & Skeleton Coupling** (Circle, Figure-8, 6D Lissajous, Deep F/B Rock, Corkscrew) with DynamicBone soft-tissue weights onto direct-insertion chapters.
6. **Save / Record Tab**:
   - Click **✦ Save all chapter data** (writes `<scene>.koisync.xml` so chapters reload automatically next time).
   - Click **✦ Fast bake 6-axis** to offline-bake all 6 axes at **50Hz (`0.02s` step)** into `UserData\KoiSyncStudio\<scene folder>\`:
     - `<scene>.funscript` (`L0`), `.surge.funscript` (`L1`), `.sway.funscript` (`L2`), `.twist.funscript` (`R0`), `.roll.funscript` (`R1`), `.pitch.funscript` (`R2`).
   - Data source automatically switches to **Auto (prefer scripts)** (`Scripts · 6 / 6`) so you can immediately preview the baked result in 3D or play back on hardware. Pair the 6 files with a matching-name video in **MultiFunPlayer / HereSphere / ScriptPlayer**.

---

## 6. Standard TCode v0.3 Six-Axis Reference & FAQ

| Axis | Motion | Neutral | Toward `999` | Toward `000` | Visual Check |
| :---: | :--- | :---: | :--- | :--- | :--- |
| **`L0`** | Stroke | `999` (out) | **Up / Out** (away from base) | **Down / Deep** (toward base) | `000` = deepest, `999` = withdrawn |
| **`L1`** | Surge | `500` | **Forward** | **Backward** | Doggy `>500`, Missionary `<500` |
| **`L2`** | Sway | `500` | **Left** (user's left) | **Right** (user's right) | Symmetrical pose = `500` |
| **`R0`** | Twist | `500` | Counter-clockwise | Clockwise | Soft limits accurate within ±48° |
| **`R1`** | Roll | `500` | Top tilts **Right** | Top tilts **Left** | SR6 linkage-coupled inverse kinematics |
| **`R2`** | Pitch | `500` | Top pitches **Forward** (+) | Top pitches **Backward** (−) | Doggy `>500`, Missionary `<500` |

### Troubleshooting Quick-Check
- **Panel doesn't appear on startup?** By design — press **`F8`** (MainGame) or **`F7`** (Studio). If nothing opens, verify `BepInEx\LogOutput.log` shows `Loading [KoiSync ...]` and check you installed the right KK vs KKS DLL build.
- **No pink 3D preview sleeve?** Make sure `[qinnyann] KoiSync Device Preview Assets v1.0.0.zipmod` is placed under `mods\qinnyann\KoiSync\`, Sideloader is installed, and **Preview** + **X-Ray** are checked.
- **Serial port fails (`Win32 5 / 32 / 2`)?** Close any other app holding the COM port (MultiFunPlayer, XToys, Arduino IDE), click **Scan Ports** to verify the real COM number (`CH340`/`CP2102`), and reconnect.
- **One axis moves opposite on your hardware?** KoiSync follows strict TCode v0.3 conventions. Calibrate axis polarity in your **SR6 / OSR2 firmware web UI** so both KoiSync and all community MultiFunPlayer scripts match seamlessly.

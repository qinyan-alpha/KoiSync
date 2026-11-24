# KoiSync AI Director — Local Loopback Bridge & MCP Server

`koisync-director` splits AI interaction cleanly into two tiers:
- **60 Hz Real-Time Cerebellum (`KoiSync.MainGame`)**: Runs inside Unity 5.6 (`DirectorModulator` + `DirectorBridge` on `http://127.0.0.1:41289`), handling smooth parameter ramping, timed choreography sequences, 60 Hz real-time `EdgeGuard` gauge braking, `SixAxisDrive` safety rate caps, and `F9` / UI instant panic-release.
- **High-Level Director Brain (`mcp/koisync_director_mcp.py`)**: Zero-dependency Python stdio JSON-RPC 2.0 MCP server exposing 12 tools to Antigravity (and sharing `DirectorClient` for future real-time voice / Gemini Live sidecars).

## Registered MCP Tools (`koisync-director`)
| Tool | Endpoint | Purpose |
| :--- | :--- | :--- |
| `koisync_status` | `GET /state` | Live pose, stage, gauges, animation Hz, 6-axis telemetry, FeelTranslator band, and Director/EdgeGuard status |
| `koisync_list_poses` | `GET /poses` | List compatible native + kPlug H poses in the current scene |
| `koisync_set_feel` | `POST /feel` | Switch `feelPreset` (`Raw`/`Balanced`/`Intense`) and `nonShaftMode` (`Foreplay`/`FullSync`/`Silent`) |
| `koisync_set_stage` | `POST /game/stage` | Switch native H stage via `HActionBase.SetPlay` (`WLoop`/`SLoop`/`OLoop`/`Idle`/`A_WLoop`/`A_SLoop`) |
| `koisync_set_speed` | `POST /game/speed` | Set & smoothly maintain Koikatsu H animation speed (`0.0..1.0`) or release speed latch |
| `koisync_switch_pose` | `POST /game/pose` | Switch to another compatible native or kPlug pose by `key` |
| `koisync_game_action` | `POST /game/action` | Native clicks (`insert`, `insert_anal`, `pull`, `finish_inside`, `finish_outside`, `finish_same`, `orgasm_weak`, `orgasm_strong`, `lock_gauges`, `set_gauges`, `aibu_attach`) |
| `koisync_director_override` | `POST /director/override` | Smooth 60 Hz `L0` envelope override or teasing hold with optional sine flutter and dead-man `ttlSeconds` |
| `koisync_director_sequence` | `POST /director/sequence` | Multi-step 60 Hz timed choreography combining game stage/speed + SR6 L0/hold/flutter per step |
| `koisync_director_edge_guard` | `POST /director/edge_guard` | Arm/disarm real-time 60 Hz gauge brake (auto-slows & pulls to outer tease window on threshold) |
| `koisync_director_timeline` | `POST /director/timeline` | **Timeline-Curve Pose Variant Engine**: scans exact 1-cycle phase→depth profile and scrubs ALL actors' Layer-0 Animator at 60 Hz (`tip_rub`, `deep_grind`, `nine_shallow_one_deep`, `tease_plunge`, `edge_hover`, `turbo_sprint`, `custom`) with paired SR6 `L0` + `R0` twist + `R1/R2/L1/L2` circular waist-grind |
| `koisync_director_release` | `POST /director/release` | Smooth fade-out (`immediate=false`) or instant abort (`immediate=true`) + restores native Animator speed |
| `koisync_screenshot` | `POST /game/screenshot` | Capture a 640×360 PNG snapshot from `Camera.main` (saved locally + optional inline MCP image block) |

## Hard Safety Guardrails (Enforced in C#)
1. **Loopback-only (`127.0.0.1`)**: Never listens on LAN/external interfaces; no wildcard CORS.
2. **Physical Panic Override (`F9` or UI "Cut AI takeover now")**: Immediately aborts any AI override, sequence, speed latch, or EdgeGuard brake and stops device output when sync is turned off.
3. **Hardware Rate & Travel Caps**: Every AI-modulated command still passes through `SixAxisDrive` smoothing, `MaximumChangePerSecond`, and user-configured travel limits.
4. **Dead-Man Watchdog**: Every override/sequence automatically fades smoothly back to native game sync once `ttlSeconds` / duration expires.

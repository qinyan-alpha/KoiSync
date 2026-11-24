# Main-game source boundary

This project is the standalone main-game implementation. It targets Koikatu.exe and has its own BepInEx GUID, config, action profiles, recordings and deployment script.

The main-game project and Studio compile their common six-axis solver from `src/Shared` and their KK model landmarks from `src/SharedUnity`. Both use the same device, serial and funscript-reader sources. The main-game adapter owns native action identities, interaction objects, animation stages and per-action profiles.

The build embeds these sources and the libraries from `src/Libraries` in `KoiSync.MainGame.dll`. Game and BepInEx references come from `GameRoot`. The Studio assembly has its own plugin entry point and loading context.

The main-game deployment script stages and replaces the DLL in `BepInEx/plugins/KoiSyncMainGame` and backs up the prior version.

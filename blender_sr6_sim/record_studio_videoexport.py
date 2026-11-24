#!/usr/bin/env python3
"""
Exports 1920x1080 @ 60fps MP4 directly from CharaStudio.exe using its built-in
VideoExport.dll (Unity GPU framebuffer capture — zero GDI dependency, locked 60fps!).
Optionally opens Timeline UI (Ctrl+T) so the real scrolling Timeline bar is visible!
Always backs up and restores all modified BepInEx configs via try/finally.
"""
from __future__ import annotations

import ctypes
import ctypes.wintypes as wt
import pathlib
import shutil
import subprocess
import sys
import time

user32 = ctypes.windll.user32

KK_DIR = pathlib.Path(r"E:\Galgames\kk")
CFG_DIR = KK_DIR / "BepInEx" / "config"
DEFAULT_SCENE_CFG = CFG_DIR / "keelhauled.defaultstudioscene.cfg"
TIMELINE_CFG = CFG_DIR / "com.joan6694.illusionplugins.timeline.cfg"
VIDEOEXPORT_CFG = CFG_DIR / "com.joan6694.illusionplugins.videoexport.cfg"
VE_OUT_DIR = KK_DIR / "UserData" / "VideoExport" / "Output"
VE_FRAMES_DIR = KK_DIR / "UserData" / "VideoExport" / "Frames"


class RECT(ctypes.Structure):
    _fields_ = [("left", wt.LONG), ("top", wt.LONG), ("right", wt.LONG), ("bottom", wt.LONG)]


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [
        ("wVk", wt.WORD), ("wScan", wt.WORD), ("dwFlags", wt.DWORD),
        ("time", wt.DWORD), ("dwExtraInfo", ctypes.c_void_p),
    ]


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [
        ("dx", wt.LONG), ("dy", wt.LONG), ("mouseData", wt.DWORD),
        ("dwFlags", wt.DWORD), ("time", wt.DWORD), ("dwExtraInfo", ctypes.c_void_p),
    ]


class _INPUTUNION(ctypes.Union):
    _fields_ = [("ki", KEYBDINPUT), ("mi", MOUSEINPUT)]


class INPUT(ctypes.Structure):
    _fields_ = [("type", wt.DWORD), ("union", _INPUTUNION)]


KEYEVENTF_KEYUP = 0x0002
KEYEVENTF_SCANCODE = 0x0008
MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004


def send_vk(vk: int, down: bool):
    scan = user32.MapVirtualKeyW(vk, 0)
    flags = KEYEVENTF_SCANCODE | (0 if down else KEYEVENTF_KEYUP)
    inp = INPUT(type=1, union=_INPUTUNION(ki=KEYBDINPUT(vk, scan, flags, 0, None)))
    user32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(INPUT))


def tap_keys(*vks: int, hold: float = 0.12):
    for vk in vks:
        send_vk(vk, True)
        time.sleep(0.04)
    time.sleep(hold)
    for vk in reversed(vks):
        send_vk(vk, False)
        time.sleep(0.04)


def find_main_window(pid: int) -> int:
    found = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(hwnd, lparam):
        if not user32.IsWindowVisible(hwnd):
            return True
        wpid = wt.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(wpid))
        if wpid.value == pid:
            r = RECT()
            user32.GetClientRect(hwnd, ctypes.byref(r))
            if (r.right - r.left) >= 600 and (r.bottom - r.top) >= 300:
                found.append(hwnd)
        return True

    user32.EnumWindows(cb, 0)
    return found[0] if found else 0


def focus_and_click_center(hwnd: int):
    user32.ShowWindow(hwnd, 9)
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.25)
    r = RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(r))
    cx = (r.left + r.right) // 2
    cy = (r.top + r.bottom) // 2
    user32.SetCursorPos(cx, cy)
    time.sleep(0.06)
    d = INPUT(type=0, union=_INPUTUNION(mi=MOUSEINPUT(0, 0, 0, MOUSEEVENTF_LEFTDOWN, 0, None)))
    u = INPUT(type=0, union=_INPUTUNION(mi=MOUSEINPUT(0, 0, 0, MOUSEEVENTF_LEFTUP, 0, None)))
    user32.SendInput(1, ctypes.byref(d), ctypes.sizeof(INPUT))
    time.sleep(0.05)
    user32.SendInput(1, ctypes.byref(u), ctypes.sizeof(INPUT))
    time.sleep(0.15)


def set_ini_key(path: pathlib.Path, key: str, value: str):
    lines = path.read_text(encoding="utf-8").splitlines()
    out = []
    replaced = False
    for ln in lines:
        s = ln.strip()
        if s.startswith(key + " =") or s == key + "=":
            out.append(f"{key} = {value}")
            replaced = True
        else:
            out.append(ln)
    if not replaced:
        out.append(f"{key} = {value}")
    path.write_text("\n".join(out) + "\n", encoding="utf-8")


def export_studio_card(scene_path: str, dest_mp4: pathlib.Path, duration_s: float = 14.0, show_timeline: bool = True, load_wait_s: float = 35.0):
    bak_scene = DEFAULT_SCENE_CFG.read_text(encoding="utf-8")
    bak_tl = TIMELINE_CFG.read_text(encoding="utf-8")
    bak_ve = VIDEOEXPORT_CFG.read_text(encoding="utf-8")

    VE_OUT_DIR.mkdir(parents=True, exist_ok=True)
    before_mp4s = {p.name for p in VE_OUT_DIR.glob("*.mp4")}
    before_frame_dirs = {p.name for p in VE_FRAMES_DIR.iterdir()} if VE_FRAMES_DIR.is_dir() else set()
    proc = None
    try:
        set_ini_key(DEFAULT_SCENE_CFG, "Default Scene Path", scene_path)
        set_ini_key(DEFAULT_SCENE_CFG, "DefaultScenePath", scene_path)
        set_ini_key(TIMELINE_CFG, "Autoplay", "Yes")

        # Configure VideoExport: Built-in Unity capture (captures Timeline UI cleanly!), 60fps, fixed seconds, 1080p MP4!
        set_ini_key(VIDEOEXPORT_CFG, "selectedScreenshotPlugin", "0")
        set_ini_key(VIDEOEXPORT_CFG, "builtInCaptureMode", "0")
        set_ini_key(VIDEOEXPORT_CFG, "builtInSizeMultiplier", "1")
        set_ini_key(VIDEOEXPORT_CFG, "framerate", "60")
        set_ini_key(VIDEOEXPORT_CFG, "exportFramerate", "60")
        set_ini_key(VIDEOEXPORT_CFG, "autoGenerateVideo", "true")
        set_ini_key(VIDEOEXPORT_CFG, "autoDeleteImages", "true")
        set_ini_key(VIDEOEXPORT_CFG, "limitDuration", "true")
        set_ini_key(VIDEOEXPORT_CFG, "selectedLimitDurationType", "1")  # 1 = Seconds
        set_ini_key(VIDEOEXPORT_CFG, "limitDurationNumber", str(duration_s))
        set_ini_key(VIDEOEXPORT_CFG, "selectedExtension", "0")          # 0 = MP4
        set_ini_key(VIDEOEXPORT_CFG, "resize", "true")
        set_ini_key(VIDEOEXPORT_CFG, "resizeX", "1920")
        set_ini_key(VIDEOEXPORT_CFG, "resizeY", "1080")

        print(f"[VE] Launching CharaStudio (1920x1080) with: {scene_path}", flush=True)
        proc = subprocess.Popen(
            [str(KK_DIR / "CharaStudio.exe"), "-screen-width", "1920", "-screen-height", "1080", "-screen-fullscreen", "0"],
            cwd=str(KK_DIR),
        )

        hwnd = 0
        for _ in range(120):
            hwnd = find_main_window(proc.pid)
            if hwnd:
                break
            time.sleep(0.5)
        if not hwnd:
            raise RuntimeError("CharaStudio window did not appear")

        print(f"[VE] Window ready (hwnd={hwnd}), waiting {load_wait_s}s for full scene load...", flush=True)
        time.sleep(load_wait_s)

        focus_and_click_center(hwnd)
        if show_timeline:
            print("[VE] Sending LeftCtrl + T to open Timeline UI...", flush=True)
            tap_keys(0xA2, 0x54)  # VK_LCONTROL + T
            time.sleep(1.0)

        # Start VideoExport recording: LeftCtrl + LeftShift + E
        print(f"[VE] Sending LeftCtrl + LeftShift + E to start {duration_s}s @ 60fps VideoExport...", flush=True)
        tap_keys(0xA2, 0xA0, 0x45)

        # Wait up to 300s for new frames folder and then final MP4
        started = False
        new_mp4 = None
        t0 = time.time()
        while time.time() - t0 < 360:
            if not started and VE_FRAMES_DIR.is_dir():
                cur_dirs = {p.name for p in VE_FRAMES_DIR.iterdir()} - before_frame_dirs
                if cur_dirs:
                    started = True
                    print(f"[VE] Frame capture STARTED in {cur_dirs}!", flush=True)
            cur_mp4s = {p.name for p in VE_OUT_DIR.glob("*.mp4")} - before_mp4s
            if cur_mp4s:
                cand = VE_OUT_DIR / sorted(cur_mp4s)[0]
                s1 = cand.stat().st_size
                time.sleep(2.5)
                s2 = cand.stat().st_size
                if s2 > 50_000 and s1 == s2:
                    new_mp4 = cand
                    break
            # Retry hotkey once at 18s if frame dir hasn't appeared yet
            if not started and 18.0 < (time.time() - t0) < 20.5:
                print("[VE] No frame folder yet; refocusing and retrying LeftCtrl+LeftShift+E...", flush=True)
                focus_and_click_center(hwnd)
                tap_keys(0xA2, 0xA0, 0x45)
                time.sleep(2.5)
            time.sleep(1.5)

        if not new_mp4:
            raise RuntimeError("VideoExport did not produce an MP4 within timeout")

        dest_mp4.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(new_mp4), str(dest_mp4))
        print(f"[VE] SUCCESS! Saved {dest_mp4} ({dest_mp4.stat().st_size / 1e6:.2f} MB)", flush=True)
    finally:
        if proc is not None and proc.poll() is None:
            proc.terminate()
            try:
                proc.wait(timeout=8)
            except subprocess.TimeoutExpired:
                proc.kill()
        DEFAULT_SCENE_CFG.write_text(bak_scene, encoding="utf-8")
        TIMELINE_CFG.write_text(bak_tl, encoding="utf-8")
        VIDEOEXPORT_CFG.write_text(bak_ve, encoding="utf-8")
        print("[VE] Closed CharaStudio and restored all 3 BepInEx configs cleanly.", flush=True)


if __name__ == "__main__":
    scene = sys.argv[1]
    out = pathlib.Path(sys.argv[2])
    dur = float(sys.argv[3]) if len(sys.argv) >= 4 else 14.0
    wait = float(sys.argv[4]) if len(sys.argv) >= 5 else 35.0
    export_studio_card(scene, out, duration_s=dur, show_timeline=True, load_wait_s=wait)

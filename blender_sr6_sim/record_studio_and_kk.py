#!/usr/bin/env python3
"""
Clean Win32 + FFmpeg NVENC 60fps screen-capture helper for recording
CharaStudio.exe (with DefaultStudioScene + Timeline Autoplay) and Koikatu.exe.

Always backs up and restores BepInEx config files via try/finally.
"""
from __future__ import annotations

import ctypes
import ctypes.wintypes as wt
import pathlib
import shutil
import subprocess
import sys
import time

# Enable physical-pixel DPI awareness so 2560x1440 / 1920x1080 coords are exact
try:
    ctypes.windll.user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
except Exception:
    try:
        ctypes.windll.shcore.SetProcessDpiAwareness(2)
    except Exception:
        pass

user32 = ctypes.windll.user32

KK_DIR = pathlib.Path(r"E:\Galgames\kk")
CFG_DIR = KK_DIR / "BepInEx" / "config"
DEFAULT_SCENE_CFG = CFG_DIR / "keelhauled.defaultstudioscene.cfg"
TIMELINE_CFG = CFG_DIR / "com.joan6694.illusionplugins.timeline.cfg"
RENDERS_DIR = pathlib.Path(r"E:\project\c#\KoiSync\blender_sr6_sim\renders")
RENDERS_DIR.mkdir(parents=True, exist_ok=True)


class RECT(ctypes.Structure):
    _fields_ = [("left", wt.LONG), ("top", wt.LONG), ("right", wt.LONG), ("bottom", wt.LONG)]


class POINT(ctypes.Structure):
    _fields_ = [("x", wt.LONG), ("y", wt.LONG)]


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [
        ("wVk", wt.WORD),
        ("wScan", wt.WORD),
        ("dwFlags", wt.DWORD),
        ("time", wt.DWORD),
        ("dwExtraInfo", ctypes.c_void_p),
    ]


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [
        ("dx", wt.LONG),
        ("dy", wt.LONG),
        ("mouseData", wt.DWORD),
        ("dwFlags", wt.DWORD),
        ("time", wt.DWORD),
        ("dwExtraInfo", ctypes.c_void_p),
    ]


class _INPUTUNION(ctypes.Union):
    _fields_ = [("ki", KEYBDINPUT), ("mi", MOUSEINPUT)]


class INPUT(ctypes.Structure):
    _fields_ = [("type", wt.DWORD), ("union", _INPUTUNION)]


INPUT_MOUSE = 0
INPUT_KEYBOARD = 1
KEYEVENTF_KEYUP = 0x0002
KEYEVENTF_SCANCODE = 0x0008
MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004


def send_key(vk: int, down: bool = True):
    scan = user32.MapVirtualKeyW(vk, 0)
    flags = KEYEVENTF_SCANCODE | (0 if down else KEYEVENTF_KEYUP)
    inp = INPUT(type=INPUT_KEYBOARD, union=_INPUTUNION(ki=KEYBDINPUT(vk, scan, flags, 0, None)))
    user32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(INPUT))


def tap_combo(*vks: int, hold: float = 0.08):
    for vk in vks:
        send_key(vk, True)
        time.sleep(0.03)
    time.sleep(hold)
    for vk in reversed(vks):
        send_key(vk, False)
        time.sleep(0.03)


def click_client_norm(hwnd: int, u: float, v: float):
    l, t, w, h = get_client_box(hwnd)
    x = int(l + w * u)
    y = int(t + h * v)
    user32.SetCursorPos(x, y)
    time.sleep(0.06)
    d = INPUT(type=INPUT_MOUSE, union=_INPUTUNION(mi=MOUSEINPUT(0, 0, 0, MOUSEEVENTF_LEFTDOWN, 0, None)))
    u_ev = INPUT(type=INPUT_MOUSE, union=_INPUTUNION(mi=MOUSEINPUT(0, 0, 0, MOUSEEVENTF_LEFTUP, 0, None)))
    user32.SendInput(1, ctypes.byref(d), ctypes.sizeof(INPUT))
    time.sleep(0.06)
    user32.SendInput(1, ctypes.byref(u_ev), ctypes.sizeof(INPUT))


def find_window_by_pid(pid: int) -> int:
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
            if (r.right - r.left) >= 640 and (r.bottom - r.top) >= 360:
                found.append(hwnd)
        return True

    user32.EnumWindows(cb, 0)
    return found[0] if found else 0


def find_window_by_title_substr(substr: str) -> int:
    found = []
    low = substr.lower()

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def cb(hwnd, lparam):
        if not user32.IsWindowVisible(hwnd):
            return True
        buf = ctypes.create_unicode_buffer(256)
        user32.GetWindowTextW(hwnd, buf, 256)
        if low in buf.value.lower():
            r = RECT()
            user32.GetClientRect(hwnd, ctypes.byref(r))
            if (r.right - r.left) >= 640 and (r.bottom - r.top) >= 360:
                found.append(hwnd)
        return True

    user32.EnumWindows(cb, 0)
    return found[0] if found else 0


def get_client_box(hwnd: int):
    r = RECT()
    user32.GetClientRect(hwnd, ctypes.byref(r))
    pt = POINT(0, 0)
    user32.ClientToScreen(hwnd, ctypes.byref(pt))
    w = max(2, (r.right - r.left) & ~1)
    h = max(2, (r.bottom - r.top) & ~1)
    return pt.x, pt.y, w, h


def position_and_focus_window(hwnd: int, x: int = 80, y: int = 60):
    user32.ShowWindow(hwnd, 9)  # SW_RESTORE
    wr = RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(wr))
    ww = wr.right - wr.left
    wh = wr.bottom - wr.top
    # Place nicely inside 2560x1440 screen so entire client area is unclipped
    user32.SetWindowPos(hwnd, 0, x, y, ww, wh, 0x0040)
    # Bring cleanly to foreground
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.25)


def grab_window_png(hwnd: int, out_png: pathlib.Path):
    l, t, w, h = get_client_box(hwnd)
    cmd = [
        "ffmpeg", "-y", "-v", "error",
        "-f", "gdigrab", "-offset_x", str(l), "-offset_y", str(t),
        "-video_size", f"{w}x{h}", "-draw_mouse", "0", "-i", "desktop",
        "-frames:v", "1", str(out_png),
    ]
    subprocess.run(cmd, check=True)
    return l, t, w, h


def record_window_mp4(hwnd: int, out_mp4: pathlib.Path, duration_s: float, fps: int = 60):
    l, t, w, h = get_client_box(hwnd)
    cmd = [
        "ffmpeg", "-y", "-v", "warning",
        "-f", "gdigrab", "-framerate", str(fps),
        "-offset_x", str(l), "-offset_y", str(t),
        "-video_size", f"{w}x{h}", "-draw_mouse", "0", "-i", "desktop",
        "-t", f"{duration_s:.2f}",
        "-vf", "scale=1920:1080:flags=lanczos",
        "-c:v", "h264_nvenc", "-preset", "p4", "-cq", "18",
        "-pix_fmt", "yuv420p", "-movflags", "+faststart",
        str(out_mp4),
    ]
    print(f"[REC] Recording {out_mp4.name} ({w}x{h} @ {fps}fps, {duration_s}s) from ({l},{t})...", flush=True)
    subprocess.run(cmd, check=True)
    print(f"[REC] DONE {out_mp4.name} ({out_mp4.stat().st_size / 1e6:.2f} MB)", flush=True)


def set_ini_key(path: pathlib.Path, key: str, value: str):
    lines = path.read_text(encoding="utf-8").splitlines()
    out = []
    replaced = False
    prefix = key + " ="
    for ln in lines:
        if ln.strip().startswith(prefix) or ln.strip() == key + "=":
            out.append(f"{key} = {value}")
            replaced = True
        else:
            out.append(ln)
    if not replaced:
        out.append(f"{key} = {value}")
    path.write_text("\n".join(out) + "\n", encoding="utf-8")


def record_studio_scene_card(scene_path: str, out_mp4: pathlib.Path, duration_s: float = 14.0, show_timeline: bool = True, load_wait_s: float = 28.0):
    bak_scene = DEFAULT_SCENE_CFG.read_text(encoding="utf-8")
    bak_tl = TIMELINE_CFG.read_text(encoding="utf-8")
    proc = None
    try:
        set_ini_key(DEFAULT_SCENE_CFG, "Default Scene Path", scene_path)
        set_ini_key(DEFAULT_SCENE_CFG, "DefaultScenePath", scene_path)
        set_ini_key(TIMELINE_CFG, "Autoplay", "Yes")

        print(f"[STUDIO] Launching CharaStudio.exe with scene: {scene_path}", flush=True)
        proc = subprocess.Popen([str(KK_DIR / "CharaStudio.exe")], cwd=str(KK_DIR))

        # Wait for window
        hwnd = 0
        for _ in range(90):
            hwnd = find_window_by_pid(proc.pid)
            if hwnd:
                break
            time.sleep(0.5)
        if not hwnd:
            raise RuntimeError("CharaStudio window did not appear within 45s")

        position_and_focus_window(hwnd, 80, 60)
        print(f"[STUDIO] Window ready (hwnd={hwnd}); waiting {load_wait_s}s for scene + textures to load...", flush=True)
        time.sleep(load_wait_s)

        position_and_focus_window(hwnd, 80, 60)
        # Toggle Timeline UI open with Ctrl+T if requested
        if show_timeline:
            # Click center safely to ensure keyboard focus inside Unity
            click_client_norm(hwnd, 0.50, 0.50)
            time.sleep(0.25)
            tap_combo(0x11, 0x54)  # VK_CONTROL + 'T'
            time.sleep(0.6)
            # Ensure Timeline is playing (if Autoplay already started it, wait — check via two snapshots!)
            snap1 = out_mp4.with_suffix(".check1.png")
            snap2 = out_mp4.with_suffix(".check2.png")
            grab_window_png(hwnd, snap1)
            time.sleep(0.8)
            grab_window_png(hwnd, snap2)
            # If the two screenshots are nearly identical (meaning Timeline paused/didn't autoplay), press Shift+T!
            if snap1.read_bytes() == snap2.read_bytes():
                print("[STUDIO] Scene static -> sending Shift+T to start Timeline playback!", flush=True)
                tap_combo(0x10, 0x54)  # VK_SHIFT + 'T'
                time.sleep(0.5)
            snap1.unlink(missing_ok=True)
            snap2.unlink(missing_ok=True)

        # Save one verification frame right before capture
        grab_window_png(hwnd, out_mp4.with_suffix(".preview.png"))
        record_window_mp4(hwnd, out_mp4, duration_s=duration_s, fps=60)
    finally:
        if proc is not None and proc.poll() is None:
            proc.terminate()
            try:
                proc.wait(timeout=8)
            except subprocess.TimeoutExpired:
                proc.kill()
        DEFAULT_SCENE_CFG.write_text(bak_scene, encoding="utf-8")
        TIMELINE_CFG.write_text(bak_tl, encoding="utf-8")
        print("[STUDIO] Cleaned up CharaStudio.exe and restored BepInEx configs.", flush=True)


if __name__ == "__main__":
    if len(sys.argv) >= 4 and sys.argv[1] == "studio":
        scene = sys.argv[2]
        out = pathlib.Path(sys.argv[3])
        dur = float(sys.argv[4]) if len(sys.argv) >= 5 else 14.0
        wait = float(sys.argv[5]) if len(sys.argv) >= 6 else 28.0
        record_studio_scene_card(scene, out, duration_s=dur, show_timeline=True, load_wait_s=wait)
    else:
        print("Usage: record_studio_and_kk.py studio <scene.png> <out.mp4> [duration_s] [load_wait_s]")

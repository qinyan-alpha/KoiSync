using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    // One page that lists every global shortcut. Desktop rows bind keyboard / mouse input, VR rows bind
    // controller buttons, and every row opens a small confirmation window before the binding is written.
    public sealed partial class KoiSyncPanel
    {
        private sealed class ShortcutRow
        {
            internal int Index;
            internal string Label="";
            internal ConfigEntry<KeyboardShortcut> Key;
            internal ConfigEntry<string> VrBind;
            internal ConfigEntry<bool> Switch;
            internal KeyboardShortcut KeyDefault;
            internal string VrDefault="";
            internal bool SwitchDefault;
            internal Text Value;
            internal bool VrBound{get{return VrBind!=null;}}
        }
        private readonly List<ShortcutRow> shortcutRows=new List<ShortcutRow>();
        private GameObject shortcutsPage,shortcutModal;
        private Text shortcutsNotice,shortcutModalTitle,shortcutModalValue;
        private RectTransform shortcutDesktopColumn,shortcutVrColumn;
        private ShortcutRow pending;
        private bool pendingVr;
        private KeyboardShortcut candidateKey;
        private string candidateVr="";
        private ulong captureLeft,captureRight;
        private static readonly KeyCode[] CaptureKeys=(KeyCode[])Enum.GetValues(typeof(KeyCode));
        private void ShowShortcuts()
        {
            if(shortcutsPage!=null){ActivatePage(WorkspacePage.Shortcuts);RefreshShortcuts();return;}
            var page=CreatePage(WorkspacePage.Shortcuts);shortcutsPage=page.gameObject;
            Label(page,"快捷键",24,18,700,38,24,Accent,FontStyle.Bold);
            Label(page,"点“重绑”后在弹出的小窗口里输入，确认才写入配置；Esc 或“取消”放弃。",24,58,948,26,13,Muted);
            RectTransform left=Rect(page,"Desktop shortcuts",24,92,462,520);shortcutDesktopColumn=left;
            RectTransform right=Rect(page,"VR shortcuts",512,92,462,520);shortcutVrColumn=right;
            Label(left,"桌面快捷键配置 · 键盘 / 鼠标 · 影响整体",0,0,462,26,15,Accent);
            Label(right,"VR 快捷键配置 · 手柄 · 仅头显内生效",0,0,462,26,15,Accent);
            if(shortcutRows.Count==0)
            {
                RegisterKey(left,row=>row.Key=host.Config.Bind("UI","PanelKey",new KeyboardShortcut(KeyCode.F9),"Toggle panel"),"面板开关");
                RegisterKey(left,row=>row.Key=host.Config.Bind("UI","DebugKey",new KeyboardShortcut(KeyCode.F8),"Toggle 3D wireframe"),"3D 线框总开关");
                RegisterKey(left,row=>row.Key=host.Config.Bind("UI","TimelineKey",new KeyboardShortcut(KeyCode.Space),"Timeline play / pause"),"时间轴播放 / 暂停");
                RegisterKey(left,row=>row.Key=host.Config.Bind("UI","DeviceMenuKey",new KeyboardShortcut(KeyCode.Mouse3),"Toggle the last device"),"设备同步开关");
                RegisterKey(left,row=>row.Key=host.Config.Bind("UI","DeviceListKey",new KeyboardShortcut(KeyCode.Mouse2),"Open the device list"),"设备列表");
                RegisterKey(left,row=>row.Key=host.Config.Bind("UI","ClimaxKey",new KeyboardShortcut(KeyCode.None),"Climax animation (None disables)"),"高潮动画");
                RegisterKey(left,row=>row.Key=host.Config.Bind("UI","RotateKey",new KeyboardShortcut(KeyCode.R),"Alignment editor rotate"),"3D 对齐 · 旋转");
                RegisterKey(left,row=>row.Key=host.Config.Bind("UI","HideKey",new KeyboardShortcut(KeyCode.H),"Alignment editor hide"),"3D 对齐 · 隐藏");
                RegisterKey(left,row=>row.Key=host.Config.Bind("VR","ToggleKey",new KeyboardShortcut(KeyCode.F7,KeyCode.LeftControl),"Toggle panel in VR (keyboard)"),"VR 面板 · 键盘备用");
                RegisterKey(left,row=>row.Key=host.Config.Bind("VR","RecenterKey",new KeyboardShortcut(KeyCode.F7,KeyCode.LeftControl,KeyCode.LeftShift),"Recenter panel in VR (keyboard)"),"VR 重定位 · 键盘备用");
                RegisterSwitch(left,row=>row.Switch=host.Config.Bind("UI","ClimaxMouseChord",true,"Both mouse buttons trigger the climax"),"高潮 · 左右键同时");
                RegisterVr(right,row=>row.VrBind=host.Config.Bind("VR","PanelButton","B:4","VR controller button that toggles the panel"),"VR 面板开关");
                RegisterVr(right,row=>row.VrBind=host.Config.Bind("VR","RecenterButton","None","VR controller button that recenters the panel"),"VR 重定位");
                RegisterSwitch(right,row=>row.Switch=host.Config.Bind("VR","ClimaxBothTriggers",true,"Both VR triggers trigger the climax"),"高潮 · 双扳机");
            }
            else
            {
                for(int i=0;i<shortcutRows.Count;i++)
                {
                    var row=shortcutRows[i];
                    BuildShortcutRow(row.VrBound?shortcutVrColumn:shortcutDesktopColumn,row);
                }
            }
            Button(page,"恢复全部默认",24,624,200,34,ResetShortcuts);
            shortcutsNotice=Label(page,"",240,630,734,24,13,Muted);
            BuildShortcutModal(page);
            shortcutModal.SetActive(false);
            RefreshShortcuts();
        }
        private void RegisterKey(RectTransform column,Action<ShortcutRow> bind,string label)
        {
            var row=new ShortcutRow{Label=label};bind(row);
            row.KeyDefault=row.Key.Value;row.Index=CountColumn(column);shortcutRows.Add(row);BuildShortcutRow(column,row);
        }
        private void RegisterVr(RectTransform column,Action<ShortcutRow> bind,string label)
        {
            var row=new ShortcutRow{Label=label};bind(row);
            row.VrDefault=row.VrBind.Value;row.Index=CountColumn(column);shortcutRows.Add(row);BuildShortcutRow(column,row);
        }
        private void RegisterSwitch(RectTransform column,Action<ShortcutRow> bind,string label)
        {
            var row=new ShortcutRow{Label=label};bind(row);
            row.SwitchDefault=row.Switch.Value;row.Index=CountColumn(column);shortcutRows.Add(row);BuildShortcutRow(column,row);
        }
        private int CountColumn(RectTransform column)
        {
            int count=0;for(int i=0;i<shortcutRows.Count;i++)if((shortcutRows[i].VrBound?shortcutVrColumn:shortcutDesktopColumn)==column)count++;
            return count;
        }
        private void BuildShortcutRow(RectTransform column,ShortcutRow row)
        {
            float y=32+row.Index*36;
            Label(column,row.Label,0,y+6,214,26,14,Color.white);
            if(row.Key!=null)
            {
                row.Value=Label(column,row.Key.Value.ToString(),218,y+6,100,26,14,Accent);
                var captured=row;Button(column,"重绑",322,y,64,28,()=>BeginShortcutCapture(captured));
            }
            else if(row.VrBind!=null)
            {
                row.Value=Label(column,VrButton.Text(row.VrBind.Value),218,y+6,100,26,14,Accent);
                var captured=row;Button(column,"重绑",322,y,64,28,()=>BeginShortcutCapture(captured));
            }
            else
            {
                var toggle=row.Switch;Toggle(column,"",218,y+4,60,toggle.Value,v=>{toggle.Value=v;host.Config.Save();});
            }
            var restored=row;Button(column,"恢复",392,y,70,28,()=>RestoreShortcut(restored));
        }
        private void BuildShortcutModal(RectTransform page)
        {
            shortcutModal=Panel(page,200,150,560,220,Card).gameObject;
            Label(shortcutModal.transform,"绑定快捷键",20,14,520,28,18,Accent,FontStyle.Bold);
            shortcutModalTitle=Label(shortcutModal.transform,"",20,46,520,24,14,Color.white);
            shortcutModalValue=Label(shortcutModal.transform,"",20,76,520,58,15,Accent);
            Label(shortcutModal.transform,"只有点“确认”才会写入 BepInEx 配置。",20,140,520,22,12,Muted);
            Button(shortcutModal.transform,"确认",300,170,120,34,ConfirmShortcutCapture,new Color32(37,108,97,255));
            Button(shortcutModal.transform,"取消",430,170,110,34,CancelShortcutCapture);
        }
        private void BeginShortcutCapture(ShortcutRow row)
        {
            pending=row;pendingVr=row.VrBound;candidateVr="";candidateKey=new KeyboardShortcut(KeyCode.None);
            if(shortcutModal==null)return;
            shortcutModal.SetActive(true);shortcutModal.transform.SetAsLastSibling();
            shortcutModalTitle.text="正在绑定："+row.Label;
            shortcutModalValue.text=pendingVr?"请按下手柄上的按键（松开后显示识别结果）":"请按下新的按键；需要组合键时先按住 Ctrl / Shift / Alt。";
            captureLeft=host.Vr.Frame.LeftHeld;captureRight=host.Vr.Frame.RightHeld;
            if(host.VrOptions!=null)host.VrOptions.Capture=pendingVr;
        }
        private void CancelShortcutCapture()
        {
            pending=null;pendingVr=false;candidateVr="";
            if(host.VrOptions!=null)host.VrOptions.Capture=false;
            if(shortcutModal!=null)shortcutModal.SetActive(false);
            if(shortcutsNotice!=null)shortcutsNotice.text="已取消绑定。";
        }
        private void ConfirmShortcutCapture()
        {
            if(pending==null){CancelShortcutCapture();return;}
            if(pendingVr)
            {
                if(candidateVr.Length==0){shortcutModalValue.text="还没有识别到手柄按键。";return;}
                pending.VrBind.Value=candidateVr;
                // A free controller binding replaces the two classic chords of the VR page.
                host.VrOptions.ControllerShortcut.Value=VrControllerShortcut.Disabled;
            }
            else
            {
                if(candidateKey.MainKey==KeyCode.None){shortcutModalValue.text="还没有识别到按键。";return;}
                pending.Key.Value=candidateKey;
            }
            host.Config.Save();
            string label=pending.Label;
            pending=null;pendingVr=false;
            if(host.VrOptions!=null)host.VrOptions.Capture=false;
            if(shortcutModal!=null)shortcutModal.SetActive(false);
            RefreshShortcuts();
            if(shortcutsNotice!=null)shortcutsNotice.text="已更新“"+label+"”。";
        }
        private void RestoreShortcut(ShortcutRow row)
        {
            if(row.Key!=null)row.Key.Value=row.KeyDefault;else if(row.VrBind!=null)row.VrBind.Value=row.VrDefault;else if(row.Switch!=null)row.Switch.Value=row.SwitchDefault;
            host.Config.Save();RefreshShortcuts();
            if(shortcutsNotice!=null)shortcutsNotice.text="已恢复“"+row.Label+"”的默认设置。";
        }
        private void ResetShortcuts()
        {
            for(int i=0;i<shortcutRows.Count;i++)RestoreShortcutSilent(shortcutRows[i]);
            host.Config.Save();RefreshShortcuts();
            if(shortcutsNotice!=null)shortcutsNotice.text="已恢复全部默认快捷键。";
        }
        private static void RestoreShortcutSilent(ShortcutRow row)
        {
            if(row.Key!=null)row.Key.Value=row.KeyDefault;else if(row.VrBind!=null)row.VrBind.Value=row.VrDefault;else if(row.Switch!=null)row.Switch.Value=row.SwitchDefault;
        }
        internal void PollShortcutCapture()
        {
            if(pending==null){if(host.VrOptions!=null)host.VrOptions.Capture=false;return;}
            if(UnityEngine.Input.GetKeyDown(KeyCode.Escape)){CancelShortcutCapture();return;}
            if(pendingVr)
            {
                ulong left=host.Vr.Frame.LeftHeld,right=host.Vr.Frame.RightHeld;
                ulong newLeft=left&~captureLeft,newRight=right&~captureRight;
                if(newLeft!=0||newRight!=0)
                {
                    int leftBit=FirstBit(newLeft),rightBit=FirstBit(newRight);
                    if(leftBit>=0&&rightBit>=0&&leftBit==rightBit)candidateVr="B:"+leftBit;
                    else if(leftBit>=0)candidateVr="L:"+leftBit;
                    else if(rightBit>=0)candidateVr="R:"+rightBit;
                }
                captureLeft=left;captureRight=right;
                if(candidateVr.Length>0&&shortcutModalValue!=null)shortcutModalValue.text="已识别："+VrButton.Text(candidateVr)+"（"+candidateVr+"）\n点“确认”写入配置。";
                return;
            }
            for(int i=0;i<CaptureKeys.Length;i++)
            {
                KeyCode key=CaptureKeys[i];
                if(key==KeyCode.None||!UnityEngine.Input.GetKeyDown(key))continue;
                candidateKey=Compose(key);
                if(shortcutModalValue!=null)shortcutModalValue.text="已识别："+candidateKey+"\n点“确认”写入配置。";
                break;
            }
        }
        private static int FirstBit(ulong mask)
        {
            for(int bit=0;bit<64;bit++)if((mask&(1UL<<bit))!=0)return bit;
            return -1;
        }
        // A chord is captured as the pressed key plus every modifier that is held at that moment.
        private static KeyboardShortcut Compose(KeyCode main)
        {
            var modifiers=new List<KeyCode>();
            KeyCode[] held={KeyCode.LeftControl,KeyCode.RightControl,KeyCode.LeftShift,KeyCode.RightShift,KeyCode.LeftAlt,KeyCode.RightAlt};
            for(int i=0;i<held.Length;i++)if(held[i]!=main&&UnityEngine.Input.GetKey(held[i]))modifiers.Add(held[i]);
            return new KeyboardShortcut(main,modifiers.ToArray());
        }
        internal void RefreshShortcuts()
        {
            if(shortcutsPage==null||!shortcutsPage.activeInHierarchy)return;
            for(int i=0;i<shortcutRows.Count;i++)
            {
                var row=shortcutRows[i];
                if(row.Value==null)continue;
                if(row.Key!=null)row.Value.text=row.Key.Value.ToString();
                else if(row.VrBind!=null)row.Value.text=VrButton.Text(row.VrBind.Value);
            }
        }
    }
}

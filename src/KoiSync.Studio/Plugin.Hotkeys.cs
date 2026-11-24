using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private ConfigEntry<KeyboardShortcut> syncKey;
        private int capturingKey=-1;
        private bool textEditing;
        private float hotkeyResumeAt;
        private string hotkeyStatus="";
        private static readonly KeyCode[] ModifierKeys={KeyCode.LeftControl,KeyCode.RightControl,KeyCode.LeftShift,KeyCode.RightShift,KeyCode.LeftAlt,KeyCode.RightAlt};
        private void InitializeHotkeys()
        {
            panelKey=Config.Bind("UI","PanelKey",new KeyboardShortcut(KeyCode.F7));
            syncKey=Config.Bind("UI","SyncKey",new KeyboardShortcut(KeyCode.Space),"Toggle device synchronization on/off. Supports keyboard modifiers and Mouse3 (first side button).");
        }
        private void SetSyncEnabled(bool enabled)
        {
            if(sendOutput==enabled)return;
            sendOutput=enabled;if(!enabled&&Devices!=null)Devices.Stop();
            Logger.LogInfo(enabled?L("设备开始同步","Device synchronization started"):L("设备停止同步","Device synchronization stopped"));
        }
        private void UpdateHotkeys()
        {
            if(capturingKey>=0||Time.unscaledTime<hotkeyResumeAt)return;
            if(panelKey.Value.IsDown())
            {
                // After the main window was closed with ×, the key reopens it
                // instead of first hiding an otherwise empty interface.
                if(guiVisible&&!showWindow)showWindow=true;else{guiVisible=!guiVisible;if(guiVisible)showWindow=true;}
                ClosePopups();textEditing=false;
            }
            // A typing key belongs to the focused text field. Side buttons and
            // function keys remain usable there, including the local Mouse3 key.
            var key=syncKey.Value.MainKey;
            bool typing=textEditing&&guiVisible&&showWindow&&key>=KeyCode.Space&&key<=KeyCode.Delete;
            if(!typing&&syncKey.Value.IsDown())SetSyncEnabled(!sendOutput);
        }
        private void CaptureHotkey()
        {
            var e=Event.current;if(capturingKey<0)return;
            if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape){capturingKey=-1;hotkeyResumeAt=Time.unscaledTime+.2f;e.Use();return;}
            KeyCode key=KeyCode.None;
            if(e.type==EventType.MouseDown)key=(KeyCode)((int)KeyCode.Mouse0+e.button);
            else if(e.type==EventType.KeyDown)key=e.keyCode;
            if(key==KeyCode.None||Array.IndexOf(ModifierKeys,key)>=0)return;
            var modifiers=new List<KeyCode>();foreach(var modifier in ModifierKeys)if(Input.GetKey(modifier))modifiers.Add(modifier);
            var shortcut=new KeyboardShortcut(key,modifiers.ToArray());
            var other=capturingKey==0?syncKey.Value:panelKey.Value;
            if(shortcut.MainKey==other.MainKey)
            {hotkeyStatus=L("此按键已用于另一个操作，请换一个按键。","This key is assigned to the other action. Choose another key.");e.Use();return;}
            if(capturingKey==0)panelKey.Value=shortcut;else syncKey.Value=shortcut;
            Config.Save();capturingKey=-1;hotkeyResumeAt=Time.unscaledTime+.25f;textEditing=false;
            hotkeyStatus=L("快捷键已保存","Shortcut saved");e.Use();
        }
        private static string ShortcutText(KeyboardShortcut key)
        {
            string text=key.ToString();
            return text.Replace("Mouse3",L("鼠标侧键 1","Mouse side button 1")).Replace("Mouse4",L("鼠标侧键 2","Mouse side button 2"));
        }
        private string Text(Rect rect,string value,int max)
        {
            GUI.SetNextControlName("ks-text-"+rect.x+"-"+rect.y);
            if(Event.current.type==EventType.MouseDown&&rect.Contains(Event.current.mousePosition))textEditing=true;
            return GUI.TextField(rect,value,max,textField);
        }
        private void DrawSettingsPage()
        {
            float top=Card(new Rect(20,54,974,246),L("快捷键设置","Keyboard and mouse shortcuts"));
            var names=new[]{L("显示 / 隐藏界面","Show / hide interface"),L("设备同步开关","Toggle device synchronization")};
            for(int i=0;i<2;i++)
            {
                float y=top+i*42;GUI.Label(new Rect(32,y+4,300,28),names[i],label);
                if(BTip(new Rect(340,y,642,34),ShortcutText(i==0?panelKey.Value:syncKey.Value),L("点击按键后，按下新的键盘按键或鼠标按键；Esc 取消。","Click a shortcut, then press a keyboard or mouse button. Esc cancels.")))
                {capturingKey=i;hotkeyStatus="";GUI.FocusControl(null);}
            }
            Divider(32,top+90,950);
            if(B(new Rect(32,top+102,300,32),L("恢复默认","Restore defaults")))
            {panelKey.Value=new KeyboardShortcut(KeyCode.F7);syncKey.Value=new KeyboardShortcut(KeyCode.Space);Config.Save();hotkeyStatus=L("快捷键已保存","Shortcut saved");}
            GUI.Label(new Rect(340,top+106,642,26),hotkeyStatus,small);
            GUI.Label(new Rect(32,top+144,950,22),L("同步为持续开关：按一次开始，再按一次停止。","Synchronization is a toggle: press once to start, again to stop."),small);
            GUI.Label(new Rect(32,top+168,950,22),L("输入文字时，文字按键留在输入框；鼠标侧键仍可切换同步。","While typing, text keys stay in the input field. Side buttons can still toggle synchronization."),small);
            DrawChapterTransitionSettings(new Rect(20,310,974,200));
        }
        private void DrawKeyCaptureOverlay()
        {
            Popup(new Rect(264,105,490,175));
            GUI.Label(new Rect(282,121,450,32),L("请按下新的快捷键","Press the new shortcut"),title);
            GUI.Label(new Rect(282,161,450,48),L("支持鼠标侧键和 Ctrl / Shift / Alt 组合键。按 Esc 取消。","Mouse side buttons and Ctrl / Shift / Alt combinations are supported. Press Esc to cancel."),new GUIStyle(label){wordWrap=true});
            GUI.Label(new Rect(282,224,450,42),hotkeyStatus,new GUIStyle(small){wordWrap=true});
            CaptureHotkey();
        }
    }
}

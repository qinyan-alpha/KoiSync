using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    // Device synchronization shortcuts, both configurable on the shortcut page:
    // the side button toggles the endpoint that was last in use, the middle button opens the list.
    public sealed partial class KoiSyncPanel
    {
        private ConfigEntry<KeyboardShortcut> deviceMenuKey;
        private GameObject deviceMenu;
        internal void PollDeviceShortcut()
        {
            if(deviceMenuKey==null)
                deviceMenuKey=host.Config.Bind("UI","DeviceMenuKey",new KeyboardShortcut(KeyCode.Mouse3),"Toggle synchronization with the device that was last in use (Shift opens the device list)");
            if(!deviceMenuKey.Value.IsDown())return;
            if(UnityEngine.Input.GetKey(KeyCode.LeftShift)||UnityEngine.Input.GetKey(KeyCode.RightShift))OpenDeviceMenu();else ToggleLastDevice();
        }
        // Reconnect the endpoint that was in use, or disconnect when a device is already connected.
        private void ToggleLastDevice()
        {
            try
            {
                if(host.Devices.Connected||host.Devices.Connecting){host.Stop("已停止输出");host.Devices.Disconnect();autoPaused=true;return;}
                int index=savedTransport==null?0:savedTransport.Value;
                if(transport.value!=index)transport.value=index;else TransportChanged(index);
                autoPaused=false;nextAutoAttempt=0;connectionError="";
                Connect();
            }
            catch(Exception ex){connectionError=ex.Message;}
        }
        private void DismissDeviceMenu()
        {
            if(deviceMenu==null)return;
            Destroy(deviceMenu);deviceMenu=null;
        }
        internal void OpenDeviceMenu()
        {
            if(deviceMenu!=null){DismissDeviceMenu();return;}
            var entries=new List<KeyValuePair<string,Action>>();
            entries.Add(new KeyValuePair<string,Action>("不同步设备"+StateSuffix(),()=>{DismissDeviceMenu();host.Stop("已停止输出");host.Devices.Disconnect();autoPaused=true;}));
            for(int i=0;i<detectedPorts.Length;i++)
            {
                string port=detectedPorts[i];
                entries.Add(new KeyValuePair<string,Action>("同步 · TCode "+port+" @ "+arguments[0],()=>{DismissDeviceMenu();SwitchDevice(0,port);}));
            }
            if(!string.IsNullOrEmpty(addresses[2]))entries.Add(new KeyValuePair<string,Action>("同步 · Intiface "+addresses[2],()=>{DismissDeviceMenu();SwitchDevice(2,null);}));
            if(!string.IsNullOrEmpty(addresses[1]))entries.Add(new KeyValuePair<string,Action>("同步 · UDP "+addresses[1]+":"+arguments[1],()=>{DismissDeviceMenu();SwitchDevice(1,null);}));
            entries.Add(new KeyValuePair<string,Action>("打开连接设置…",()=>{DismissDeviceMenu();SetVisible(true);ShowOverview();}));
            SetVisible(true);
            var canvas=canvasObject.transform;
            var root=Rect(canvas,"KoiSync device menu",0,0,340,48+entries.Count*38);
            Vector2 pointer=new Vector2(UnityEngine.Input.mousePosition.x/Screen.width,UnityEngine.Input.mousePosition.y/Screen.height);
            root.anchorMin=root.anchorMax=pointer;
            root.pivot=new Vector2(0,1);
            root.anchoredPosition=new Vector2(12,-12);
            var background=Panel(root,0,0,340,48+entries.Count*38,Card);
            background.GetComponent<Image>().raycastTarget=true;
            Label(background,"设备同步 · "+(host.Devices.Connected?"已连接":"未连接"),14,6,312,24,14,Accent);
            for(int i=0;i<entries.Count;i++)
            {
                var entry=entries[i];
                var action=entry.Value;
                Button(background,entry.Key,10,34+i*38,320,32,()=>action());
            }
            root.SetAsLastSibling();
            var dismiss=Rect(canvas,"KoiSync device menu dismiss",0,0,0,0);
            Image(dismiss,Color.clear,false).raycastTarget=true;
            dismiss.anchorMin=Vector2.zero;dismiss.anchorMax=Vector2.one;dismiss.offsetMin=Vector2.zero;dismiss.offsetMax=Vector2.zero;
            dismiss.gameObject.AddComponent<PopupDismiss>().Dismiss=DismissDeviceMenu;
            dismiss.SetAsFirstSibling();
            deviceMenu=root.gameObject;
            Refresh();
        }
        private string StateSuffix()
        {
            if(host.Devices.Connected)return "（当前已连接）";
            if(host.Devices.Connecting)return "（正在连接）";
            return "";
        }
        private void SwitchDevice(int index,string target)
        {
            try
            {
                if(transport.value!=index)transport.value=index;else TransportChanged(index);
                if(!string.IsNullOrEmpty(target))endpoint.text=target;
                autoPaused=false;nextAutoAttempt=0;connectionError="";
                Connect();
            }
            catch(Exception ex){connectionError=ex.Message;}
        }
    }
}

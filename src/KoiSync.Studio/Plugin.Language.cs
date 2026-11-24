using System;
using BepInEx.Configuration;
using System.Globalization;
using UnityEngine;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private ConfigEntry<string> languageSetting;
        private int languagePicker;
        private static string L(string chinese,string english){return LocaleCatalog.Text(chinese,english,UiText.Language);}
        private void InitializeLanguage()
        {languageSetting=Config.Bind("UI","Language",LocaleCatalog.Resolve(CultureInfo.CurrentUICulture.Name),"zh, zh-Hant, en, ru, ja, ko, de, fr. Independent of player length and reference region.");UiText.Language=LocaleCatalog.Resolve(languageSetting.Value);status=L("加载场景后，读取角色并绑定。","Load a scene, then read and bind actors.");}
        private void ChangeLanguage()
        {SetLanguage(LocaleCatalog.Codes[(Array.IndexOf(LocaleCatalog.Codes,UiText.Language)+1)%LocaleCatalog.Codes.Length]);}
        private void SetLanguage(string code)
        {
            UiText.Language=LocaleCatalog.Resolve(code);languageSetting.Value=UiText.Language;languagePicker=0;ClearUiTextFocus();Config.Save();
            for(int i=0;i<actors.Count;i++)actorNames[i]=ActorDisplayName(actors[i]);
            cameraEvidence="";nextPairCheck=0;
            status=Binding!=null?Binding.Description:L("选择角色后点击绑定。","Select actors, then click Bind.");
            ApplyFontSizes();
        }
        private void LanguageButton(Rect area,int owner)
        {
            if(!B(area,LocaleCatalog.Names[Array.IndexOf(LocaleCatalog.Codes,UiText.Language)]+" ▾"))return;
            bool open=languagePicker!=owner;ClosePopups();ClearUiTextFocus();languagePicker=open?owner:0;
        }
        private void LanguageMenu(int owner,float x)
        {
            if(languagePicker!=owner)return;
            var bounds=new Rect(x,43,160,LocaleCatalog.Codes.Length*30+8);
            if(Event.current.type==EventType.MouseDown&&!bounds.Contains(Event.current.mousePosition)||Event.current.type==EventType.KeyDown&&Event.current.keyCode==KeyCode.Escape)
            {languagePicker=0;ClearUiTextFocus();Event.current.Use();return;}
            GUI.enabled=true;Popup(bounds);
            for(int i=0;i<LocaleCatalog.Codes.Length;i++)if(B(new Rect(x+4,47+i*30,152,28),LocaleCatalog.Names[i]))
            {SetLanguage(LocaleCatalog.Codes[i]);if(Event.current.type!=EventType.Used)Event.current.Use();return;}
        }
        private static string LocalError(string text)
        {
            switch(text)
            {
                case "Invalid chapter bounds":return L("章节起止范围无效","Invalid chapter start/end range");
                case "First chapter start is fixed":return L("首章起点不能移动","The first chapter start is fixed");
                case "Last chapter end follows scene duration":return L("末章终点随场景总时长","The last chapter ends with the scene");
                case "Chapter start overlaps previous boundary":return L("起点不能越过上一章边界","Start overlaps the previous boundary");
                case "Chapter end overlaps next boundary":return L("终点不能越过下一章边界","End overlaps the next boundary");
                default:return UiText.T(text);
            }
        }
        private string DeviceStatus()
        {
            string value=Devices.Status;if(UiText.Language=="zh")return value;
            if(value=="未连接")return L("未连接","Not connected");
            if(value=="已断开")return L("已断开","Disconnected");
            if(value=="已取消连接")return L("已取消连接","Connection cancelled");
            return value
                .Replace("正在连接 ",L("正在连接 ","Connecting "))
                .Replace("已打开 ",L("已打开 ","Opened "))
                .Replace("连接错误: ",L("连接错误: ","Connection error: "))
                .Replace("通信线程错误：",L("通信线程错误：","Device worker error: "))
                .Replace("连接超时：",L("连接超时：","Connection timed out: "))
                .Replace("，请检查端口及设备占用",L("，请检查端口及设备占用","; check the port and device ownership"))
                .Replace("Intiface 已连接 · L0 位置通道",L("Intiface 已连接 · L0 位置通道","Intiface connected · L0 position"))
                .Replace("UDP 无设备应答",L("UDP 无设备应答","UDP has no device acknowledgement"))
                .Replace("Intiface 已断开",L("Intiface 已断开","Intiface disconnected"))
                .Replace("Intiface 通信错误",L("Intiface 通信错误","Intiface transport error"))
                .Replace("Intiface 连接超时",L("Intiface 连接超时","Intiface connection timed out"))
                .Replace("设备已从 Intiface 断开",L("设备已从 Intiface 断开","Device disconnected from Intiface"))
                .Replace("Handy 请使用 Intiface Central",L("Handy 请使用 Intiface Central","Use Intiface Central for The Handy"));
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace KoiSync.MainGame
{
    public sealed partial class Plugin
    {
        private ConfigEntry<string> languageSetting;
        private bool languagePickerOpen;
        private readonly Dictionary<GUIStyle,GUIStyle> compactStyles=new Dictionary<GUIStyle,GUIStyle>();

        private static string L(string chinese,string english)
        {
            return UiText.L(chinese,english);
        }

        private void InitializeLanguage()
        {
            languageSetting=Config.Bind("UI","Language",LocaleCatalog.Resolve(CultureInfo.CurrentUICulture.Name),"zh, zh-Hant, en, ru, ja, ko, de, fr.");
            UiText.Language=LocaleCatalog.Resolve(languageSetting.Value);
            Status=L("等待本体 H 场景","Waiting for Main Game H-scene");
        }

        private void SetLanguage(string code)
        {
            UiText.Language=LocaleCatalog.Resolve(code);
            if(languageSetting!=null)languageSetting.Value=UiText.Language;
            languagePickerOpen=false;
            GUI.FocusControl(null);
            GUIUtility.keyboardControl=0;
            Config.Save();
            if(Binding==null&&Profile==null)Status=L("等待本体 H 场景","Waiting for Main Game H-scene");
            ApplyFontSizes();
        }

        private void ApplyFontSizes()
        {
            if(button==null)return;
            int btnSize=UiText.English?11:13;
            foreach(var style in new[]{button,activeButton,primaryButton,dangerButton})
                if(style!=null)style.fontSize=btnSize;
            if(label!=null)label.fontSize=UiText.English?13:14;
            if(toggle!=null)toggle.fontSize=UiText.English?11:13;
            if(small!=null)small.fontSize=UiText.English?11:12;
            if(wrapSmall!=null)wrapSmall.fontSize=UiText.English?11:12;
            compactStyles.Clear();
        }

        private GUIStyle FitStyle(GUIStyle style,string text,float width)
        {
            if(style==null||string.IsNullOrEmpty(text))return style;
            if(style.CalcSize(new GUIContent(text)).x<=width-6)return style;
            GUIStyle compact;
            if(!compactStyles.TryGetValue(style,out compact))
            {
                compact=new GUIStyle(style){fontSize=Mathf.Max(9,style.fontSize-2)};
                compactStyles[style]=compact;
            }
            return compact;
        }

        private void DrawLanguageButton(Rect area)
        {
            int idx=Array.IndexOf(LocaleCatalog.Codes,UiText.Language);
            string caption=(idx>=0?LocaleCatalog.Names[idx]:"English")+" ▾";
            if(!GUI.Button(area,caption,FitStyle(button,caption,area.width)))return;
            languagePickerOpen=!languagePickerOpen;
            choice=-1;
            GUI.FocusControl(null);
        }

        private void DrawLanguageMenu(float x)
        {
            if(!languagePickerOpen)return;
            float w=150f,h=LocaleCatalog.Codes.Length*28f+8f;
            var bounds=new Rect(x,40f,w,h);
            var e=Event.current;
            if(e.type==EventType.MouseDown&&!bounds.Contains(e.mousePosition)||e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape)
            {
                languagePickerOpen=false;
                e.Use();
                return;
            }
            GUI.enabled=true;
            Box(new Rect(bounds.x-1,bounds.y-1,bounds.width+2,bounds.height+2),Accent);
            Box(bounds,new Color(.115f,.076f,.145f,.995f));
            for(int i=0;i<LocaleCatalog.Codes.Length;i++)
            {
                bool active=LocaleCatalog.Codes[i]==UiText.Language;
                if(GUI.Button(new Rect(bounds.x+4,bounds.y+4+i*28f,bounds.width-8,26f),LocaleCatalog.Names[i],active?activeButton:button))
                {
                    SetLanguage(LocaleCatalog.Codes[i]);
                    if(e.type!=EventType.Used)e.Use();
                    return;
                }
            }
        }

        private string DeviceStatus()
        {
            return Devices==null?L("未连接","Not connected"):UiText.TranslateComposite(Devices.Status);
        }

        private string FeelStatus()
        {
            return UiText.TranslateComposite(feel.Status);
        }

        private string DirectorStatusText()
        {
            return UiText.TranslateComposite(DirectorUiSummary());
        }
    }
}

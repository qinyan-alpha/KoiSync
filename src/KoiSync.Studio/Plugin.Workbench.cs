using System;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private int mainPage,workbenchTab;
        private bool workbenchCollapsed,guiVisible=false,focusChannelWindow;
        private bool serialPortPicker;
        private Vector2 serialPortScroll;
        private string hoverTip="",deviceNote="";
        private bool showCalibration {get{return guiVisible&&showWindow&&mainPage==1&&!workbenchCollapsed&&workbenchTab==1;}}
        private bool PopupOpen {get{return languagePicker!=0||serialPortPicker||modelPreviewPicker||regionPicker||actorPicker!=0||openingPicker||chapterPicker!=0||chapterEffectPicker||chapterFrequencyPicker||chapterGrindPicker||chapterMenu||renamingChapter||capturingKey>=0;}}
        private void ClosePopups()
        {languagePicker=0;serialPortPicker=modelPreviewPicker=regionPicker=false;actorPicker=chapterPicker=0;openingPicker=chapterEffectPicker=chapterFrequencyPicker=chapterGrindPicker=chapterMenu=renamingChapter=false;effectPopupChapter=null;capturingKey=-1;}
        private void ClearUiTextFocus()
        {
            textEditing=false;
            GUI.FocusControl(null);
            GUIUtility.keyboardControl=0;
        }
        private void SetMainPage(int page)
        {mainPage=page;ClosePopups();textEditing=false;GUI.FocusControl(null);}
        private void SetWorkbenchPage(int page)
        {workbenchTab=page;workbenchCollapsed=false;ClosePopups();textEditing=false;GUI.FocusControl(null);}
        private void ToggleChannelWindow()
        {showChannels=!showChannels;focusChannelWindow=showChannels;}
        private void DrawWindow(int id)
        {
            if(!popupLayer.AllowBackground)return;
            Box(new Rect(1,1,window.width-2,44),HeaderFill);Divider(1,45,window.width-2);
            GUI.Label(new Rect(20,10,165,27),"✦ KoiSync Studio",title);
            GUI.Label(new Rect(188,16,80,20),"v1.0.0",small);
            LanguageButton(new Rect(828,10,120,27),1);
            if(B(new Rect(960,10,34,27),"×")){showWindow=false;ClosePopups();}
            Box(new Rect(1010,46,window.width-1011,window.height-47),SidebarFill);Box(new Rect(1010,46,1,window.height-47),Edge);
            var pages=new[]{L("◈ 设备","◈ Device"),L("❖ 工作台","❖ Desk"),L("◇ 设置","◇ Setup")};
            for(int i=0;i<3;i++)if(Tab(new Rect(1022,58+i*46,78,38),pages[i],i==mainPage))SetMainPage(i);
            Divider(1022,198,78);
            if(BTip(new Rect(1022,210,78,34),(showChannels?"● ":"○ ")+L("六轴","6-Axis"),L("显示 / 隐藏六轴数据窗","Show / hide the six-axis display"),showChannels?tabSelected:button))ToggleChannelWindow();
            if(BTip(new Rect(1022,252,78,34),(sendOutput?"● ":"○ ")+L("同步","Sync"),OutputStatus(),sendOutput?primaryButton:button))SetSyncEnabled(!sendOutput);
            if(mainPage==0)DrawDevicePage();
            else if(mainPage==1)DrawWorkbench();
            else DrawSettingsPage();
            DrawHoverTip();
            if(!PopupOpen)GUI.DragWindow(new Rect(12,4,794,38));
        }
        private void DrawPopupContents()
        {
            GUI.enabled=true;
            if(languagePicker!=0)LanguageMenu(1,828);
            if(serialPortPicker)DrawSerialPortPicker();
            if(regionPicker)DrawRegionPicker();
            if(actorPicker!=0)DrawActorPicker();
            if(openingPicker)DrawOpeningPicker();
            if(chapterPicker!=0)DrawChapterPicker();
            if(chapterEffectPicker)DrawChapterEffectPicker();
            if(chapterFrequencyPicker)DrawChapterFrequencyPicker();
            if(chapterGrindPicker)DrawChapterGrindPicker();
            if(modelPreviewPicker)DrawModelPreviewPicker();
            if(chapterMenu||renamingChapter)DrawChapterMenu();
            if(capturingKey>=0)DrawKeyCaptureOverlay();
        }
        private void DrawDevicePage()
        {
            float top=Card(new Rect(20,54,478,262),L("连接","Connection"));
            int mode=GUI.Toolbar(new Rect(32,top,454,30),transport,new[]{UiText.T("串口"),"UDP","Intiface"},button);if(mode!=transport)ChangeTransport(mode);
            if(transport==0)
            {
                string caption=string.IsNullOrEmpty(endpoint)?L("选择串口","Select serial port"):endpoint;
                if(B(new Rect(32,top+38,300,30),caption+"  ▾"))
                {
                    bool open=!serialPortPicker;ClosePopups();ClearUiTextFocus();serialPortPicker=open;serialPortScroll=Vector2.zero;
                    if(open&&ports.Length==0&&!scanning)ScanDevices();
                }
            }
            else endpoint=Text(new Rect(32,top+38,300,30),endpoint,180);
            argument=Text(new Rect(340,top+38,146,30),argument,20);
            GUI.Label(new Rect(32,top+72,454,22),transport==0?UiText.T("端口 / 波特率 · ")+(scanning?UiText.T("扫描中…"):ports.Length==0?UiText.T("点击扫描查看可用串口"):L("请选择已发现的串口","Select a discovered serial port")):transport==1?UiText.T("设备 IP / UDP 端口"):UiText.T("本机 WebSocket / 设备索引 · Intiface 仅支持 L0"),small);
            GUI.enabled=transport==0&&!scanning;
            if(B(new Rect(32,top+100,223,32),scanning?UiText.T("扫描中…"):UiText.T("扫描串口")))ScanDevices();
            GUI.enabled=true;
            bool linked=Devices.Connected||Devices.Connecting;
            if(B(new Rect(263,top+100,223,32),Devices.Connecting?UiText.T("取消"):Devices.Connected?UiText.T("断开"):UiText.T("连接"),linked?button:primaryButton))ConnectDevice();
            DrawDot(32,top+145,10,Devices.Connected?Accent:Devices.Connecting?Warm:new Color(.45f,.40f,.52f));
            GUI.Label(new Rect(48,top+138,438,24),DeviceStatus(),label);
            GUI.Label(new Rect(32,top+164,454,58),deviceNote,wrapSmall);

            top=Card(new Rect(516,54,478,262),L("同步","Sync"));
            if(B(new Rect(528,top,454,44),sendOutput?L("■ 停止设备同步","■ Stop synchronization"):L("▶ 开启设备同步","▶ Enable synchronization"),sendOutput?dangerButton:primaryButton))SetSyncEnabled(!sendOutput);
            bool sending=sendOutput&&Devices.Connected&&Drive.Ready;
            DrawDot(528,top+61,10,sending?Accent:sendOutput?Warm:new Color(.45f,.40f,.52f));
            GUI.Label(new Rect(544,top+54,438,24),OutputStatus(),label);
            GUI.Label(new Rect(528,top+82,454,24),L("切换快捷键：","Toggle shortcut: ")+ShortcutText(syncKey.Value),small);
            GUI.Label(new Rect(528,top+108,454,44),L("暂停时保持当前姿态；等待连接或场景数据时保留同步开关。","Holds the current pose while paused; keeps sync enabled while waiting for a connection or scene data."),wrapSmall);
            if(B(new Rect(528,top+186,454,30),L("显示 / 隐藏六轴数据窗","Show / hide the six-axis display")))ToggleChannelWindow();

            DrawSyncDelay(new Rect(20,326,974,178));
        }
        private void DrawSerialPortPicker()
        {
            var area=new Rect(32,156,300,122);
            if(Event.current.type==EventType.MouseDown&&!area.Contains(Event.current.mousePosition)||Event.current.type==EventType.KeyDown&&Event.current.keyCode==KeyCode.Escape)
            {serialPortPicker=false;ClearUiTextFocus();Event.current.Use();return;}
            Popup(area);
            if(scanning){GUI.Label(new Rect(area.x+12,area.y+12,area.width-24,26),UiText.T("扫描中…"),label);return;}
            if(ports.Length==0)
            {
                GUI.Label(new Rect(area.x+12,area.y+10,area.width-24,26),L("没有发现串口设备","No serial ports found"),label);
                if(B(new Rect(area.x+12,area.y+48,area.width-24,30),L("重新扫描","Scan again")))ScanDevices();
                return;
            }
            serialPortScroll=GUI.BeginScrollView(new Rect(area.x+4,area.y+4,area.width-8,area.height-8),serialPortScroll,new Rect(0,0,area.width-30,Mathf.Max(area.height-16,ports.Length*30)));
            for(int i=0;i<ports.Length&&serialPortPicker;i++)if(B(new Rect(0,i*30,area.width-32,28),(string.Equals(endpoint,ports[i],StringComparison.OrdinalIgnoreCase)?"●  ":"")+ports[i]))
            {
                endpoint=ports[i];savedEndpoint[0].Value=endpoint;Config.Save();serialPortPicker=false;ClearUiTextFocus();
                if(Event.current.type!=EventType.Used)Event.current.Use();
            }
            GUI.EndScrollView();
        }
        private string[] MappingModeTips {get{return new[]{
            L("跟随画面的实际动作幅度。超出设备行程时会到达边界，余下的动作变化可能无法完整表现。","Follows the actual motion size in the scene. Motion beyond device travel reaches the limit, so some changes may not be fully reproduced."),
            L("动作更平顺，大幅动作较少持续顶到行程边界。进退幅度也会适配你设置的长度，适合连续播放。","Smoother motion with less time pressed against travel limits. In-and-out motion adapts to your length settings for continuous playback."),
            L("尽量保留本章动作的变化幅度，减少长期偏向一侧。小动作不会主动放大，大动作仍保持在设备行程内。","Preserves this chapter's changes in motion as closely as possible, with less persistent bias to one side. Small motions are not amplified; large motions stay within device travel."),
            L("手动调节请选择这个：起点与末端之间映射完整 L0 行程，缩短距离可放大小幅运动；玩家长度自动取两点距离，入口跟随游戏绑定点。应用于当前播放章节，并作为未单独设置章节的默认模式。使用实时计算并关闭自动分析，可直接录制。","For manual editing: the endpoints span the full L0 travel; a shorter span amplifies small motions. Player length follows their distance; entry follows the game binding. Applies to the playing chapter and becomes the default for chapters without an explicit mode. Uses live input with analysis off; recording remains available.")};}}
        private void DrawSyncPreferencePage()
        {
            float top=Card(new Rect(20,0,478,448),L("体感模式 · 全局三档（默认 0–99 全幅）","Feel mode · 3-tier presets (default 0–99)"));
            var playing=RotationChapter();
            GUI.Label(new Rect(32,top,454,22),playing==null?L("未绑定播放中章节 · 选择将作为全局与烘焙默认值","No bound playing chapter · applies globally and to script bake"):
                L("播放中：","Playing: ")+"#"+(Book.Chapters.IndexOf(playing)+1)+"  "+ChapterTitle(playing),small);
            int current=(int)MappingMode;
            bool manualMode=current==(int)AxisMappingMode.Manual;
            var feelNames=new[]{L("绝对真实","Raw (1:1)"),L("平衡推荐","Balanced"),L("高刺激","Intense")};
            var feelTips=new[]{
                L("1:1 保留骨骼原始几何幅度，0–99 无底限截断，不做行程放大。","1:1 raw skeletal geometry across 0–99 with no stroke amplification."),
                L("结合预扫描章节行程自动归一化到 0–99 黄金动态区间，高速冲刺自动拉满节奏，并叠加柔和次轴增强。","Normalizes chapter travel onto the 0–99 golden window with responsive sprint speed and gentle secondary axes."),
                L("0–99 全幅满行程动态拉伸并向深端强化，最大化抽送冲击感与次轴摆动。","Full 0–99 dynamic expansion biased toward deep strokes with maximum speed and secondary-axis drive.")};
            bool old=GUI.enabled;GUI.enabled=old&&!UseScripts;
            for(int i=0;i<3;i++)
            {
                bool active=!manualMode&&(int)Feel.Preset==i;
                if(BTip(new Rect(32+i*154,top+30,146,36),feelNames[i],UseScripts?L("脚本使用已录制的最终数值","Scripts use recorded final values"):feelTips[i],active?tabSelected:button))
                {
                    if(manualMode)SetMappingMode((int)AxisMappingMode.Soft);
                    SetFeelPreset((FeelPreset)i);
                }
            }
            GUI.enabled=old;
            Divider(32,top+78,454);
            string desc=UseScripts?L("当前正在播放已烘焙脚本（使用脚本最终数值）。","Playing back recorded scripts (uses final baked values)."):
                manualMode?MappingModeTips[3]:feelTips[(int)Feel.Preset];
            GUI.Label(new Rect(32,top+88,454,66),desc,wrapLabel);
            string liveMeta=!UseScripts&&!manualMode
                ?(Feel.Preset==FeelPreset.Raw?L("当前：绝对真实 1:1 几何直出 · L0 范围 0–","Current: Raw 1:1 geometry · L0 0–")+Math.Round(Feel.OuterCeiling*99.0).ToString("0",System.Globalization.CultureInfo.InvariantCulture)
                    :(Feel.EstimatedFreqHz>0.05?Feel.EstimatedFreqHz.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+" Hz · ":"")+Feel.Status)
                :L("手动标尺模式：由起点到末端直接映射 0–99 行程。","Manual span mode: origin-to-tip directly maps to 0–99 travel.");
            GUI.Label(new Rect(32,top+160,454,32),liveMeta,wrapSmall);
            Divider(32,top+200,454);
            GUI.Label(new Rect(32,top+210,454,22),L("L0 行程外限（默认 0–99 全幅）","L0 outer limit (default 0–99 full travel)"),small);
            float curCeil=(float)Math.Round(Feel.OuterCeiling*100.0,1);
            float nextCeil=GUI.HorizontalSlider(new Rect(32,top+242,340,20),curCeil,55f,99f);
            GUI.Label(new Rect(378,top+236,48,24),curCeil.ToString("0",System.Globalization.CultureInfo.InvariantCulture)+"%",label);
            bool resetCeil=B(new Rect(430,top+234,56,26),"99");
            if(resetCeil||Math.Abs(nextCeil-curCeil)>=0.5f)
                SetFeelOuterCeiling((resetCeil?99.0:Math.Round(nextCeil))/100.0);
            GUI.Label(new Rect(32,top+268,454,34),L("已移除玩家长度底限截断；如需缩短深度请拖动滑块或调整六轴限位。","Player-length bottom cap removed; adjust the slider or axis limits only if needed."),wrapSmall);
            Divider(32,top+312,454);
            GUI.Label(new Rect(32,top+322,454,22),L("高级手调（可选）","Advanced manual override (optional)"),small);
            if(BTip(new Rect(32,top+348,454,34),manualMode?L("退出直观调节（返回三档体感）","Exit manual span (return to 3-tier Feel)"):L("直观调节（手动拖动起末标尺）","Direct adjustment (manual endpoints)"),MappingModeTips[3],manualMode?tabSelected:button))
            {
                if(manualMode)SetMappingMode((int)AxisMappingMode.Soft);
                else SetMappingMode((int)AxisMappingMode.Manual);
            }
            DrawChapterEffectSettings(new Rect(516,0,478,448));
        }
        private void DrawWorkbench()
        {
            if(selectedChapter!=null&&!Book.Chapters.Contains(selectedChapter)){SelectChapter(null);chapterBoundaryDrag=false;}
            DrawTimelineTransport();
            GUI.Label(new Rect(20,220,924,22),L("左键选章并定位 · 右键重命名 / 删除 · 拖动刻度定位 · Ctrl + 滚轮缩放","Select and seek · Right-click: rename / delete · Drag ruler to seek · Ctrl + wheel: zoom"),small);
            if(BTip(new Rect(954,218,40,24),workbenchCollapsed?"▾":"▴",workbenchCollapsed?L("展开工作台","Expand workbench"):L("折叠工作台","Collapse workbench")))
            {workbenchCollapsed=!workbenchCollapsed;ClosePopups();}
            if(workbenchCollapsed)return;
            var tabs=new[]{L("章节编辑","Chapter editor"),L("校准","Calibration"),L("同步效果","Sync preference"),L("保存 / 录制","Save / Record")};
            for(int i=0;i<tabs.Length;i++)if(Tab(new Rect(20+i*245.5f,250,237.5f,32),tabs[i],i==workbenchTab))SetWorkbenchPage(i);
            GUI.BeginGroup(new Rect(0,ContentTop,1010,448));
            if(workbenchTab==0)DrawChapterEditor();
            else if(workbenchTab==1)DrawCalibrationPage();
            else if(workbenchTab==2)DrawSyncPreferencePage();
            else DrawScriptsPage();
            GUI.EndGroup();
            if(pillStatusStyle!=null)GUI.Box(new Rect(12,748,990,26),GUIContent.none,pillStatusStyle);
            else Box(new Rect(12,748,990,26),new Color(.088f,.078f,.115f,.95f));
            GUI.Label(new Rect(22,750,970,22),status,small);
        }
        private bool BTip(Rect rect,string caption,string tip){return BTip(rect,caption,tip,button);}
        private bool BTip(Rect rect,string caption,string tip,GUIStyle style)
        {
            if(rect.Contains(Event.current.mousePosition))hoverTip=tip;
            return B(rect,caption,style);
        }
        private void DrawHoverTip()
        {
            if(hoverTip.Length==0||PopupOpen||Event.current.type!=EventType.Repaint)return;
            float width=420,height=wrapLabel.CalcHeight(new GUIContent(hoverTip),width-24)+24;
            var pos=Event.current.mousePosition;
            float x=Mathf.Clamp(pos.x+18,12,window.width-width-12),y=Mathf.Clamp(pos.y+22,12,window.height-height-12);
            Popup(new Rect(x,y,width,height));GUI.Label(new Rect(x+12,y+12,width-24,height-24),hoverTip,wrapLabel);
        }
    }
}

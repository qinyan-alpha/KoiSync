using System;
using System.Globalization;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        internal static readonly string[] AxisNames={"L0","L1","L2","R0","R1","R2"};
        private static string[] OpeningNames {get{return new[]{UiText.T("阴道"),UiText.T("肛门"),UiText.T("口部"),UiText.T("左手"),UiText.T("右手"),L("胸部","Chest"),L("左手→阴部","Left hand → vulva"),L("右手→阴部","Right hand → vulva")};}}
        private readonly string[] gainText={"1","1","1","1","1","1"};
        private Rect window=new Rect(24,40,1120,292);
        private Rect channelWindow=new Rect(24,540,636,280);
        private bool openingPicker;
        private bool showChannels;
        private GUIStyle label,small,title,button,textField,toggle;
        private Font uiFont;
        private float uiScale=1;
        private float nextSeekPreview;
        private int actorPicker,dragAxis=-1;
        private bool dragUpper;
        private Vector2 actorScroll;
        private string seekText="0";
        private double pendingSeek;
        private bool seekDirty;
        private readonly ModalGuiLayer popupLayer=new ModalGuiLayer();
        private bool PointerInside
        {get{var point=new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y);return guiVisible&&(PopupOpen||popupLayer.Capturing)||draggingRod||ModelHandleContains(point)||InsideWindows(point);}}
        private bool InsideWindows(Vector2 screenPoint)
        {var point=screenPoint/uiScale;return guiVisible&&(showWindow&&window.Contains(point)||showChannels&&channelWindow.Contains(point));}

        private void Styles()
        {
            if(label!=null)return;
            uiFont=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Microsoft JhengHei","Yu Gothic UI","Yu Gothic","Meiryo","Malgun Gothic","Segoe UI","Arial"},15);
            label=new GUIStyle(GUI.skin.label){font=uiFont,fontSize=14,wordWrap=false};label.normal.textColor=new Color(.937f,.918f,.953f);
            small=new GUIStyle(label){fontSize=12};small.normal.textColor=new Color(.702f,.655f,.753f);
            title=new GUIStyle(label){fontSize=17,fontStyle=FontStyle.Bold};title.normal.textColor=new Color(.94f,.64f,.75f);
            BuildTheme();
        }
        private void OnGUI()
        {
            if(!guiVisible){popupLayer.Reset();draggingRod=false;ClearModelHandles();return;}
            Styles();hoverTip="";
            if(Event.current.type==EventType.MouseDown)textEditing=false;
            popupLayer.Begin(PopupOpen);
            if(!popupLayer.Capturing)DrawRodHandle();
            if(!showWindow&&!showChannels)return;
            window.height=mainPage==1?(workbenchCollapsed?256:784):mainPage==0?512:522;
            if(languagePicker!=0)window.height=Mathf.Max(window.height,306);
            uiScale=Mathf.Min(1,Mathf.Min(Screen.width/1136f,Screen.height/Mathf.Max(window.height+16,showChannels?290:0)));
            var matrix=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(uiScale,uiScale,1));
            var prevSkin=GUI.skin;if(customSkin!=null)GUI.skin=customSkin;
            if(showWindow){window=ClampWindow(window);window=GUI.Window(731920,window,DrawWindow,"",windowStyle);}
            if(showChannels){channelWindow=ClampWindow(channelWindow);channelWindow=GUI.Window(731921,channelWindow,DrawChannelWindow,"",windowStyle);}
            if(focusChannelWindow){GUI.BringWindowToFront(731921);focusChannelWindow=false;}
            popupLayer.Draw(731922,PopupOpen,window,Screen.width/uiScale,Screen.height/uiScale,DrawPopupContents);
            GUI.skin=prevSkin;
            GUI.matrix=matrix;
        }
        private Rect ClampWindow(Rect rect){rect.x=Mathf.Clamp(rect.x,0,Mathf.Max(0,Screen.width/uiScale-rect.width));rect.y=Mathf.Clamp(rect.y,0,Mathf.Max(0,Screen.height/uiScale-rect.height));return rect;}
        private void DrawChannelWindow(int id)
        {
            if(!popupLayer.AllowBackground)return;
            Box(new Rect(1,1,channelWindow.width-2,44),HeaderFill);Divider(1,45,channelWindow.width-2);
            GUI.Label(new Rect(20,10,530,26),L("六轴行程 · 粉：输入 / 金：输出","Six-axis travel · Pink: input / Gold: output"),title);
            if(B(new Rect(574,12,40,26),"×"))showChannels=false;
            GUI.Label(new Rect(20,48,594,21),ClimaxEffectActive&&!UseScripts?
                L("高潮章节 · L0 粉色为画面输入，金色为章节输出","Climax chapter · L0 pink is scene input; gold is chapter output"):
                UseScripts?L("脚本已包含倍率 · 可限制行程和勾选发送通道","Scripts include gain · Adjust travel limits and enabled channels"):UiText.T("拖动上下手柄限制行程 · × 调整倍率 · 勾选发送通道"),small);
            DrawChannels(70);
            GUI.Label(new Rect(20,247,594,24),OutputStatus(),small);
            GUI.DragWindow(new Rect(12,8,548,30));
        }
        private void DrawChannels(float top)
        {
            GUI.BeginGroup(new Rect(0,top-397,636,580));
            var e=Event.current;
            var telemetry=Devices.GetTelemetry();
            var output=sendOutput&&telemetry.OutputRequested&&telemetry.LastMovementValues!=null?telemetry.LastMovementValues:outputValues;
            for(int i=0;i<6;i++)
            {
                // The selected chapter effect is the current intended L0
                // output. Do not let delayed transport telemetry make the
                // gold rail appear to keep following the geometry input.
                double displayedOutput=i==0&&ClimaxEffectActive&&!UseScripts&&Drive.Ready?Drive.Values[0]:output[i];
                float x=22+i*99;
                GUI.Label(new Rect(x,397,90,23),AxisNames[i]+"  "+(Drive.Ready?Position(displayedOutput):"—"),label);
                var track=new Rect(x+20,424,42,106);float low=track.yMax-(float)Drive.Minimum[i]*track.height,high=track.yMax-(float)Drive.Maximum[i]*track.height;
                Box(new Rect(track.x,track.y,17,track.height),new Color(.19f,.16f,.24f));Box(new Rect(track.x+25,track.y,17,track.height),new Color(.19f,.16f,.24f));
                Box(new Rect(track.x-4,high,50,low-high),new Color(.48f,.27f,.38f,.26f));
                if(Drive.Ready)
                {
                    FillRail(new Rect(track.x,track.y,17,track.height),Drive.Input[i],new Color(.87f,.55f,.65f));
                    FillRail(new Rect(track.x+25,track.y,17,track.height),displayedOutput,new Color(.89f,.74f,.56f));
                }
                Box(new Rect(track.x-6,low-2,54,4),new Color(.91f,.62f,.72f));
                Box(new Rect(track.x-6,high-2,54,4),new Color(.92f,.77f,.58f));
                GUI.Label(new Rect(x+70,track.y-5,28,20),Math.Round(Drive.Maximum[i]*999).ToString("000"),small);
                GUI.Label(new Rect(x+70,track.yMax-15,28,20),Math.Round(Drive.Minimum[i]*999).ToString("000"),small);
                var hit=new Rect(track.x-10,track.y-6,62,track.height+12);
                if(e.type==EventType.MouseDown&&e.button==0&&hit.Contains(e.mousePosition))
                {dragAxis=i;dragUpper=Mathf.Abs(e.mousePosition.y-high)<Mathf.Abs(e.mousePosition.y-low);Devices.Stop();e.Use();}
                if(dragAxis==i&&(e.type==EventType.MouseDrag||e.type==EventType.MouseDown))
                {ApplyLimit(i,track,e.mousePosition.y);e.Use();}
                if(dragAxis==i&&e.rawType==EventType.MouseUp){ApplyLimit(i,track,e.mousePosition.y);dragAxis=-1;SaveAxes();}
                bool enabled=GUI.Toggle(new Rect(x,539,23,23),Drive.Enabled[i],"",toggle);if(enabled!=Drive.Enabled[i]){Drive.Enabled[i]=enabled;SaveAxes();}
                GUI.Label(new Rect(x+25,537,17,23),"×",small);
                bool previousEnabled=GUI.enabled;GUI.enabled=previousEnabled&&!UseScripts;
                string updated=Text(new Rect(x+42,536,50,25),gainText[i],6);GUI.enabled=previousEnabled;
                if(updated!=gainText[i])
                {
                    gainText[i]=updated;double gain;
                    if(double.TryParse(updated,NumberStyles.Float,CultureInfo.InvariantCulture,out gain)&&Numbers.Finite(gain)&&gain>=0&&gain<=20){Drive.Gain[i]=gain;SaveAxes();}
                }
            }
            GUI.EndGroup();
        }
        private void ApplyLimit(int index,Rect track,float y)
        {double value=Mathf.Clamp01((track.yMax-y)/track.height);if(dragUpper)Drive.Maximum[index]=Math.Max(Drive.Minimum[index],value);else Drive.Minimum[index]=Math.Min(Drive.Maximum[index],value);}
        private static void FillRail(Rect track,double value,Color color)
        {float height=track.height*(float)Numbers.Clamp(value,0,1);Box(new Rect(track.x,track.yMax-height,track.width,height),color);}
        private static string Position(double value){return Math.Round(Numbers.Clamp(value,0,1)*999).ToString("000",CultureInfo.InvariantCulture);}

        private void TimelineAction(Action action,bool preserveRecording=false)
        {try{chapterSelection.Reset();if(!preserveRecording)liveL0Cycle.Reset();if(!preserveRecording&&recording)FinishRecording();Devices.Stop();action();if(Binding!=null)Binding.ResetTransientMotion();Drive.Reset();nextPairCheck=0;}catch(Exception error){Devices.Stop();status=error.GetBaseException().Message;}}
        private void DrawOpeningPicker()
        {
            var area=new Rect(20,426,340,(OpeningNames.Length+1)*28+8);Popup(area);
            if(Event.current.type==EventType.MouseDown&&!area.Contains(Event.current.mousePosition)){openingPicker=false;Event.current.Use();return;}
            for(int i=-1;i<OpeningNames.Length;i++)
                if(B(new Rect(area.x+4,area.y+4+(i+1)*28,area.width-8,26),i<0?L("自动跟随 BP","Follow BP automatically"):OpeningNames[i]))
                {openingPicker=false;SelectOpening(i);}
        }
        private string OutputStatus()
        {
            if(!sendOutput)return UiText.T("预览 / 设备停止");
            if(!Devices.Connected)return L("同步已启用 · 等待设备连接","Sync enabled · Waiting for device connection");
            if(scenePending||!Drive.Ready)return UiText.T("同步已启用 · 等待有效姿态");
            bool any=false;foreach(bool enabled in Drive.Enabled)any|=enabled;
            if(!any||Devices.LinearOnly&&!Drive.Enabled[0])return UiText.T("同步已启用 · 未勾选可发送通道");
            var telemetry=Devices.GetTelemetry();
            return telemetry.OutputRequested&&telemetry.SecondsSinceMovement<.5?UiText.T("同步发送中"):UiText.T("同步已启用 · 等待发送");
        }
        private void DrawActorPicker()
        {
            var area=new Rect(212,382,782,262);Popup(area);
            if(Event.current.type==EventType.MouseDown&&!area.Contains(Event.current.mousePosition)){actorPicker=0;Event.current.Use();return;}
            if(actorNames.Length==0){GUI.Label(new Rect(222,390,750,25),UiText.T("先点击读取当前角色"),label);return;}
            actorScroll=GUI.BeginScrollView(area,actorScroll,new Rect(0,0,760,actorNames.Length*30));
            for(int i=0;i<actorNames.Length;i++)if(B(new Rect(4,i*30,750,28),actorNames[i]))
            {SelectActor(i,actorPicker==1);}
            GUI.EndScrollView();
        }
        private string ActorLabel(int index){return index>=0&&index<actorNames.Length?actorNames[index]:UiText.T("未读取");}
        private bool B(Rect rect,string text){return B(rect,text,button);}
        private static string TimeText(double value){return ((int)value/60).ToString("00")+":"+(value%60).ToString("00.00",CultureInfo.InvariantCulture);}
        private static void Box(Rect rect,Color color){var old=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old;}
        private static void Line(float y){Box(new Rect(20,y,594,1),new Color(.265f,.185f,.315f));}
    }
}

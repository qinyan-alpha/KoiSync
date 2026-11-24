using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.MainGame
{
    public sealed partial class Plugin
    {
        private const float MainWidth=1040,MainHeight=640,Sidebar=910;
        private static readonly Color WindowFill=new Color(.106f,.094f,.133f,.97f),HeaderFill=new Color(.137f,.122f,.176f);
        private static readonly Color CardFill=new Color(.149f,.129f,.188f),Edge=new Color(.247f,.212f,.306f);
        private static readonly Color Accent=new Color(.870f,.545f,.651f),Warm=new Color(.890f,.735f,.555f),CalibratedColor=new Color(.91f,.70f,.77f);
        private static readonly Color Muted=new Color(.702f,.655f,.753f),Ink=new Color(.937f,.918f,.953f);
        private static string[] Pages {get{return new[]{L("设备","Device"),L("工作台","Workbench"),L("AI 互动","AI Hub"),L("设置","Settings")};}}
        private static string[] WorkbenchTabs {get{return new[]{L("当前动作","Current action"),L("入口校准","Calibration"),L("同步偏好","Sync & feel"),L("保存 / 录制","Save / Record")};}}
        private static string[] SlotNames {get{return new[]{L("男方 1","Male 1"),L("男方 2","Male 2"),L("女方 1","Female 1"),L("女方 2","Female 2")};}}
        private static readonly ActionSource[] Effectors={ActionSource.Shaft,ActionSource.LeftFinger,ActionSource.RightFinger,ActionSource.SceneAnchor,ActionSource.Pelvis,ActionSource.Head,ActionSource.InteractionItem,ActionSource.LeftHand,ActionSource.RightHand,ActionSource.AnimatedProp,ActionSource.NoMotion};
        private static readonly NativeTarget[] Contacts={NativeTarget.VAGINAL,NativeTarget.ANAL,NativeTarget.ORAL,NativeTarget.BREASTS,NativeTarget.LEFTHAND,NativeTarget.RIGHTHAND,NativeTarget.LEFTFOOT,NativeTarget.RIGHTFOOT,NativeTarget.BOTH_FEET,NativeTarget.INTERCRURAL};
        private static string[] PoseViews {get{return new[]{L("几何模型","Geometry"),L("输出模型","Output")};}}

        private Rect window=new Rect(24,38,MainWidth,MainHeight),channelWindow=new Rect(48,260,650,304);
        private float uiScale=1;
        private int mainPage=1,workbenchTab,choice=-1;
        private bool showWindow=false,showChannels,raiseChannels,actionsOpen,worldCalibration;
        private Font uiFont;
        private GUIStyle windowStyle,cardStyle,label,small,title,heading,button,activeButton,primaryButton,dangerButton,textField,toggle,wrapSmall;
        private GUISkin customSkin;
        private Texture2D pillAccentTex;
        private readonly List<Texture2D> themeTextures=new List<Texture2D>();
        private Vector2 catalogueScroll;
        private string search="",uiNotice="";
        private readonly List<HSceneProc.AnimationListInfo> actions=new List<HSceneProc.AnimationListInfo>();
        private ConfigEntry<KeyboardShortcut> panelKey,syncKey,axesKey;
        private bool editingDrive,calibrationDirty,axesDirty,responseDirty;
        private bool EditingDrive {get{return editingDrive;}}

        private void InitializeUi()
        {
            InitializePreview();
            panelKey=Config.Bind("UI","PanelKey",new KeyboardShortcut(KeyCode.F8));
            syncKey=Config.Bind("UI","SyncKey",new KeyboardShortcut(KeyCode.F9));
            axesKey=Config.Bind("UI","AxesKey",new KeyboardShortcut(KeyCode.F10));
            InitializeDeviceUi();
            InitializeVoiceHud();
        }

        private void UpdateUi()
        {
            if(panelKey.Value.IsDown())
            {
                if(showWindow){showChannels=false;worldCalibration=false;languagePickerOpen=false;EndEndpointDrag();FinishDriveEdit();}
                showWindow=!showWindow;choice=-1;
            }
            if(syncKey.Value.IsDown())SetSync(!SyncRequested);
            if(axesKey.Value.IsDown())ToggleChannels();
            if(!Input.GetMouseButton(0)&&draggedBinding==null)FinishDriveEdit();
            UpdateVoiceHud();
        }

        private void SetSync(bool requested)
        {
            SyncRequested=requested;
            if(!requested){AbortDirector(L("同步关闭 · 已切断 AI 接管","Sync disabled · AI takeover cut"));Devices.Stop();}
        }
        private void ToggleChannels(){showChannels=!showChannels;raiseChannels=showChannels;}

        private void Styles()
        {
            if(label!=null)return;
            uiFont=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Microsoft JhengHei","Yu Gothic UI","Yu Gothic","Meiryo","Malgun Gothic","Segoe UI","Arial"},15);
            label=new GUIStyle(GUI.skin.label){font=uiFont,fontSize=14};label.normal.textColor=Ink;
            small=new GUIStyle(label){fontSize=12};small.normal.textColor=Muted;
            wrapSmall=new GUIStyle(small){wordWrap=true};
            title=new GUIStyle(label){fontSize=17,fontStyle=FontStyle.Bold};title.normal.textColor=new Color(.94f,.64f,.75f);
            heading=new GUIStyle(label){fontStyle=FontStyle.Bold};heading.normal.textColor=new Color(.91f,.70f,.77f);
            button=ButtonStyle(new Color(.196f,.172f,.244f),new Color(.315f,.272f,.385f),6);
            activeButton=ButtonStyle(new Color(.255f,.165f,.235f),new Color(.72f,.45f,.56f),6);
            primaryButton=ButtonStyle(new Color(.545f,.275f,.380f),new Color(.78f,.49f,.60f),7);
            dangerButton=ButtonStyle(new Color(.49f,.22f,.27f),new Color(.74f,.40f,.46f),7);
            textField=new GUIStyle(GUI.skin.textField){font=uiFont,fontSize=14,border=new RectOffset(7,7,7,7),padding=new RectOffset(9,9,4,4),alignment=TextAnchor.MiddleLeft};
            textField.normal.background=RoundedTexture(new Color(.095f,.084f,.122f),new Color(.29f,.25f,.36f),5,false);
            textField.focused.background=RoundedTexture(new Color(.11f,.095f,.14f),Accent,5,false);
            textField.normal.textColor=textField.focused.textColor=Ink;
            toggle=new GUIStyle(GUI.skin.toggle){font=uiFont,fontSize=13};toggle.normal.textColor=Ink;toggle.onNormal.textColor=new Color(.94f,.69f,.78f);
            windowStyle=new GUIStyle(GUIStyle.none){border=new RectOffset(12,12,12,12)};
            windowStyle.normal.background=windowStyle.onNormal.background=RoundedTexture(WindowFill,new Color(.36f,.29f,.43f),10,false);
            cardStyle=new GUIStyle(GUIStyle.none){border=new RectOffset(10,10,10,10)};
            cardStyle.normal.background=RoundedTexture(CardFill,Edge,8,true);
            pillAccentTex=RoundedTexture(Accent,Accent,2,false);
            customSkin=ScriptableObject.Instantiate(GUI.skin);customSkin.hideFlags=HideFlags.HideAndDontSave;
            var hTrack=new GUIStyle(customSkin.horizontalSlider){border=new RectOffset(5,5,3,3),fixedHeight=8,margin=new RectOffset(4,4,6,6)};
            hTrack.normal.background=RoundedTexture(new Color(.19f,.16f,.24f),new Color(.32f,.27f,.39f),3,false);
            var hThumb=new GUIStyle(customSkin.horizontalSliderThumb){fixedWidth=14,fixedHeight=14,border=new RectOffset(0,0,0,0),margin=new RectOffset(0,0,0,0),overflow=new RectOffset(0,0,3,3)};
            hThumb.normal.background=CircleTexture(new Color(.91f,.61f,.72f),new Color(.98f,.84f,.89f),16);
            hThumb.hover.background=CircleTexture(new Color(.96f,.70f,.80f),Color.white,16);
            hThumb.active.background=CircleTexture(Warm,Color.white,16);
            customSkin.horizontalSlider=hTrack;customSkin.horizontalSliderThumb=hThumb;
            ApplyFontSizes();
        }
        private Texture2D RoundedTexture(Color fill,Color border,int radius,bool verticalSheen=true)
        {
            int size=Mathf.Max(12,radius*2+8);
            var texture=new Texture2D(size,size,TextureFormat.ARGB32,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            float half=size*.5f,inner=half-radius-.5f;
            for(int y=0;y<size;y++)
            {
                float vNorm=(y+.5f)/size;
                Color body=verticalSheen?Color.Lerp(new Color(fill.r*.94f,fill.g*.94f,fill.b*.95f,fill.a),Color.Lerp(fill,Color.white,.045f),vNorm):fill;
                body.a=fill.a;
                for(int x=0;x<size;x++)
                {
                    float dx=Mathf.Max(Mathf.Abs(x+.5f-half)-inner,0f);
                    float dy=Mathf.Max(Mathf.Abs(y+.5f-half)-inner,0f);
                    float dist=Mathf.Sqrt(dx*dx+dy*dy)-radius;
                    float outerA=Mathf.Clamp01(.55f-dist);
                    float borderT=Mathf.Clamp01(dist+1.25f);
                    Color c=Color.Lerp(body,border,borderT);c.a*=outerA;
                    texture.SetPixel(x,y,c);
                }
            }
            texture.Apply();themeTextures.Add(texture);return texture;
        }
        private Texture2D CircleTexture(Color fill,Color border,int size)
        {
            var texture=new Texture2D(size,size,TextureFormat.ARGB32,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            float half=size*.5f,r=half-1f;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                float dx=x+.5f-half,dy=y+.5f-half,dist=Mathf.Sqrt(dx*dx+dy*dy)-r;
                Color body=Color.Lerp(fill,Color.white,.10f*(y/(float)size));
                Color c=Color.Lerp(body,border,Mathf.Clamp01(dist+1.1f));c.a=fill.a*Mathf.Clamp01(.55f-dist);
                texture.SetPixel(x,y,c);
            }
            texture.Apply();themeTextures.Add(texture);return texture;
        }
        private GUIStyle ButtonStyle(Color fill,Color border,int radius=6)
        {
            int b=radius+2;
            var style=new GUIStyle(GUI.skin.button){font=uiFont,fontSize=13,border=new RectOffset(b,b,b,b),padding=new RectOffset(8,8,2,2)};
            style.normal.background=style.focused.background=RoundedTexture(fill,border,radius);
            style.hover.background=RoundedTexture(Color.Lerp(fill,Color.white,.08f),Color.Lerp(border,Accent,.4f),radius);
            style.active.background=style.onNormal.background=RoundedTexture(Color.Lerp(fill,Accent,.22f),Accent,radius);
            style.normal.textColor=style.focused.textColor=Ink;
            style.hover.textColor=style.active.textColor=style.onNormal.textColor=new Color(.99f,.97f,.99f);
            return style;
        }
        private void DestroyUi()
        {
            if(customSkin!=null){Destroy(customSkin);customSkin=null;}
            if(uiFont!=null)Destroy(uiFont);
            foreach(var texture in themeTextures)if(texture!=null)Destroy(texture);
            themeTextures.Clear();
        }

        private void OnGUI()
        {
            bool drawDock=ShouldDrawQuickDock();
            if(!showWindow&&!showChannels&&!drawDock)return;
            Styles();
            window.width=worldCalibration?575:MainWidth;window.height=worldCalibration?96:MainHeight;
            float width=Mathf.Max(showWindow?window.width:MainWidth,showChannels?channelWindow.width:0);
            float height=Mathf.Max(showWindow?window.height:MainHeight,showChannels?channelWindow.height:0);
            uiScale=Mathf.Min(1,Mathf.Min(Screen.width/(width+12),Screen.height/(height+12)));
            if(showWindow||showChannels)DrawWorldPreview();
            var matrix=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(uiScale,uiScale,1));
            var prevSkin=GUI.skin;if(customSkin!=null)GUI.skin=customSkin;
            if(showWindow)window=GUI.Window(731930,ClampWindow(window),DrawWindow,"",windowStyle);
            if(showChannels)channelWindow=GUI.Window(731931,ClampWindow(channelWindow),DrawChannelWindow,"",windowStyle);
            if(raiseChannels){GUI.BringWindowToFront(731931);raiseChannels=false;}
            if(drawDock)DrawHQuickDockOverlay();
            GUI.skin=prevSkin;
            GUI.matrix=matrix;
            if(Event.current.rawType==EventType.MouseUp&&draggedBinding==null){dragAxis=-1;FinishDriveEdit();}
        }

        private Rect ClampWindow(Rect bounds)
        {
            bounds.x=Mathf.Clamp(bounds.x,0,Mathf.Max(0,Screen.width/uiScale-bounds.width));
            bounds.y=Mathf.Clamp(bounds.y,0,Mathf.Max(0,Screen.height/uiScale-bounds.height));
            return bounds;
        }
        private bool InsideWindows(Vector2 point)
        {point/=uiScale;return showWindow&&window.Contains(point)||showChannels&&channelWindow.Contains(point)||InsideQuickDock(point);}
        private bool PointerCaptured()
        {
            var point=new Vector2(Input.mousePosition.x,Screen.height-Input.mousePosition.y);
            if(InsideWindows(point)||draggedBinding!=null)return true;
            return showWindow&&mainPage==1&&workbenchTab==1&&(endpointHandles[0].Contains(point)||endpointHandles[1].Contains(point));
        }

        private void DrawWindow(int id)
        {
            Box(new Rect(1,1,window.width-2,44),HeaderFill);
            GUI.Label(new Rect(18,10,152,26),L("KoiSync 本体","KoiSync Main"),title);
            GUI.Label(new Rect(172,14,82,24),"v"+MainGameVersion.Number,small);
            if(worldCalibration)
            {
                if(Button(402,10,156,28,L("返回入口校准","Back to calibration"))){worldCalibration=false;EndEndpointDrag();FinishDriveEdit();}
                GUI.Label(new Rect(18,55,536,27),Binding==null?L("等待有效姿态","Waiting for valid pose"):L("拖动场景中的起点或末端  ·  ","Drag origin or tip in scene  ·  ")+OutputStatus(),label);
                GUI.DragWindow(new Rect(12,5,380,36));return;
            }
            GUI.Label(new Rect(260,13,452,24),OutputStatus(),small);
            DrawLanguageButton(new Rect(720,10,134,26));
            if(Button(862,10,30,26,"×")){showWindow=false;choice=-1;languagePickerOpen=false;EndEndpointDrag();FinishDriveEdit();}
            if(!languagePickerOpen)GUI.DragWindow(new Rect(0,0,714,44));

            Box(new Rect(Sidebar,45,MainWidth-Sidebar-1,MainHeight-46),new Color(.090f,.078f,.118f));
            Box(new Rect(Sidebar,45,1,MainHeight-46),Edge);
            var pages=Pages;
            for(int i=0;i<pages.Length;i++)
            {
                if(Button(922,54+i*38,104,32,pages[i],mainPage==i?activeButton:button))
                {EndEndpointDrag();FinishDriveEdit();mainPage=i;choice=-1;languagePickerOpen=false;uiNotice="";GUI.FocusControl(null);}
            }
            Box(new Rect(922,214,104,1),Edge);
            if(Button(922,226,104,34,L("六轴行程","Six axes"),showChannels?activeButton:button))ToggleChannels();
            if(Button(922,268,104,36,(SyncRequested?"● ":"○ ")+L("同步","Sync"),SyncRequested?primaryButton:button))SetSync(!SyncRequested);
            if(Button(922,312,104,32,AiTakeoverBlocked?L("恢复 AI","Resume AI"):L("断开 AI","Cut AI"),AiTakeoverBlocked?button:(AnyDirectorActive?dangerButton:button)))
            {
                if(AiTakeoverBlocked){ResumeAiTakeover();uiNotice=L("已恢复允许 AI 接管","AI takeover re-enabled");}
                else DisconnectAiTakeover(L("已一键断开 AI 接管并交还游戏控制","AI takeover cut; returned control to game"));
            }
            GUI.Label(new Rect(925,356,100,80),axesKey.Value+" "+L("六轴","Axes")+"\n"+syncKey.Value+" "+L("同步","Sync")+"\n"+(voicePttKey!=null?voicePttKey.Value+" "+L("说话","Talk"):""),wrapSmall);

            if(mainPage==0)DrawDevicePage();
            else if(mainPage==1)DrawWorkbench();
            else if(mainPage==2)DrawAiHubPage();
            else DrawSettingsPage();
            Box(new Rect(18,602,874,1),Edge);
            GUI.Label(new Rect(18,610,874,23),UiText.TranslateComposite(uiNotice.Length>0?uiNotice:Status),small);
            DrawLanguageMenu(704);
        }

        private void DrawWorkbench()
        {
            Card(18,55,874,86,L("当前动作","Current action"));
            GUI.Label(new Rect(32,82,556,25),Profile==null?L("等待本体 H 场景","Waiting for Main Game H-scene"):Profile.Name+"   ·   "+Profile.Key,label);
            string time=phaseTime.ToString("F2",CultureInfo.InvariantCulture)+" / "+phaseDuration.ToString("F2",CultureInfo.InvariantCulture)+" s";
            GUI.Label(new Rect(594,81,280,24),Profile==null?"":ActionLabels.Stage(Stage)+"   "+time,small);
            Box(new Rect(32,122,846,3),Edge);
            if(Profile!=null&&phaseDuration>0)Box(new Rect(32,122,(float)Numbers.Clamp(phaseTime/phaseDuration,0,1)*846,3),Accent);
            var tabs=WorkbenchTabs;
            for(int i=0;i<tabs.Length;i++)
            {
                if(Button(18+i*220,153,214,32,tabs[i],workbenchTab==i?activeButton:button))
                {EndEndpointDrag();FinishDriveEdit();workbenchTab=i;choice=-1;languagePickerOpen=false;uiNotice="";GUI.FocusControl(null);}
            }
            switch(workbenchTab)
            {
                case 0:DrawCurrentAction();break;
                case 1:DrawCalibrationPage();break;
                case 2:DrawMappingPage();break;
                case 3:DrawRecordPage();break;
            }
        }

        private void DrawCurrentAction()
        {
            float y=Card(18,198,874,122,L("本动作绑定","Action binding"));
            bool active=Profile!=null;
            string[] captions=active?new[]{
                L("来源 · ","Source · ")+ActionLabels.Actor(ActiveSource),L("目标 · ","Target · ")+ActionLabels.Actor(Profile.Target),
                L("驱动 · ","Driver · ")+ActionLabels.Source(Profile.Effector),L("部位 · ","Zone · ")+ActionLabels.Contact(Binding==null?Profile.Contact:Binding.Contact,Profile.Landmark)
            }:new[]{L("来源","Source"),L("目标","Target"),L("驱动","Driver"),L("部位","Zone")};
            GUI.enabled=active;
            for(int i=0;i<4;i++)if(Button(32+i*213,y,205,32,captions[i],choice==i?activeButton:button)){choice=choice==i?-1:i;actionsOpen=false;}
            GUI.enabled=true;
            GUI.Label(new Rect(32,y+44,670,28),active&&Profile.ManualBinding?L("当前使用手动绑定","Using manual binding"):L("动作和阶段由游戏控制","Action and stage follow the game"),small);
            if(Button(734,y+43,144,28,actionsOpen?L("关闭动作目录","Close catalogue"):L("动作目录","Action catalogue")))
            {choice=-1;actionsOpen=!actionsOpen;if(actionsOpen)RefreshCatalogue();}
            if(actionsOpen){DrawActions();return;}
            if(choice>=0&&Profile!=null){DrawChoices();return;}
            DrawEntryDiagnostics(18,332,874,257);
        }

        private void DrawChoices()
        {
            var slots=SlotNames;
            int count=choice<2?slots.Length:choice==2?Effectors.Length:Contacts.Length+3;
            string[] headers={L("为当前动作选择来源","Select source for this action"),L("为当前动作选择目标","Select target for this action"),L("为当前动作选择驱动","Select driver for this action"),L("为当前动作选择部位","Select zone for this action")};
            float y=Card(18,332,874,249,headers[choice]);
            for(int i=0;i<count;i++)
            {
                string caption=choice<2?slots[i]:choice==2?ActionLabels.Source(Effectors[i]):i<Contacts.Length?ActionLabels.Contact(Contacts[i]):
                    ActionLabels.Contact(Profile.Contact,(ActionLandmark)(i-Contacts.Length+1));
                if(Button(32+(i%4)*213,y+(i/4)*40,205,32,caption)){ChooseBinding(choice,i);return;}
            }
        }

        private void ChooseBinding(int category,int value)
        {
            var source=category==0?(ActorSlot)value:Profile.Source;
            var target=category==1?(ActorSlot)value:Profile.Target;
            var effector=category==2?Effectors[value]:Profile.Effector;
            var contact=Profile.Contact;
            var landmark=Profile.Landmark;
            if(category==3)
            {
                if(value<Contacts.Length){contact=Contacts[value];landmark=ActionLandmark.Anatomical;}
                else landmark=(ActionLandmark)(value-Contacts.Length+1);
            }
            choice=-1;
            bool targetChanged=target!=Profile.Target||contact!=Profile.Contact||landmark!=Profile.Landmark;
            if(!targetChanged&&source==Profile.Source&&effector==Profile.Effector)return;
            FinishDriveEdit();
            RestartCapture("manual binding changed");
            if(targetChanged)Profile.ClearEntryOffsets();
            Profile.Source=source;Profile.Target=target;Profile.Effector=effector;Profile.Contact=contact;Profile.Landmark=landmark;
            Profile.ClearCalibrations();Profile.ManualBinding=true;Profile.CenteredMotion=false;
            Profile.Save(DataRoot);ReleaseBinding();bindingIdentity="";nextBind=0;uiNotice=L("已保存本动作绑定","Action binding saved");
        }

        private void DrawEntryDiagnostics(float x,float y,float width,float height)
        {
            float top=Card(x,y,width,height,L("入口与输出","Entry & output"));
            if(Binding==null||geometryPose==null){GUI.Label(new Rect(x+14,top,width-28,25),L("等待当前动作的有效观测","Waiting for a valid observation of this action"),small);return;}
            var entry=Binding.Entry;
            float left=x+14,half=(width-30)*.5f;
            GUI.Label(new Rect(left,top,half-6,25),entry.Source,label);
            GUI.Label(new Rect(left,top+30,half-6,24),L("观测入口 XYZ   ","Observed entry XYZ   ")+MmVector(entry.Observed.Origin),small);
            GUI.Label(new Rect(left,top+54,half-6,24),L("校准入口 XYZ   ","Calibrated entry XYZ   ")+MmVector(entry.Calibrated.Origin),small);
            double anchorGap=(entry.Observed.Origin-entry.Landmark.Origin).Length;
            if(anchorGap>.0001)
                GUI.Label(new Rect(left,top+78,half-6,24),L("动作锚点与解剖点相差 ","Anchor-to-landmark gap ")+Millimetres(anchorGap)+" mm",small);
            if(Binding.Shaft!=null)
            {
                GUI.Label(new Rect(left,top+106,half-6,24),L("可见链 ","Visible chain ")+Millimetres(Measured.VisibleLength)+L(" mm  ·  到入口 "," mm  ·  To entry ")+Millimetres(visibleContactDistance)+" mm",small);
                GUI.Label(new Rect(left,top+130,half-6,24),L("材料跨面 ","Insertion ")+(visibleInsertion*100).ToString("F1",CultureInfo.InvariantCulture)+L("%  ·  深度 ","%  ·  Depth ")+Millimetres(Measured.TargetDepth)+" mm",small);
            }
            else GUI.Label(new Rect(left,top+106,half-6,24),L("固定来源标尺 ","Reference span ")+Millimetres(Measured.Length)+" mm",small);
            float right=left+half+8;
            if(devicePose!=null&&outputValid)
            {
                var offset=devicePose.Sleeve.Origin-entry.Calibrated.Origin;
                var frame=StudioShaftMotion.TwistFrame(geometryPose.Sleeve,Sample.ShaftTwistHalfTurn);
                GUI.Label(new Rect(right,top,half-10,25),outputFromScript?L("录制脚本 · 最终输出","Recorded script · final output"):L("实时计算 · 最终输出","Live calculation · final output"),label);
                GUI.Label(new Rect(right,top+30,half-10,24),L("输出入口 XYZ   ","Output entry XYZ   ")+MmVector(devicePose.Sleeve.Origin),small);
                GUI.Label(new Rect(right,top+54,half-10,24),L("与校准入口相差 ","Offset from calibrated ")+Millimetres(offset.Length)+" mm  ·  "+ActionEntry.AngleBetween(frame,devicePose.Sleeve).ToString("F1",CultureInfo.InvariantCulture)+"°",small);
                var components=new MotionVector(MotionVector.Dot(offset,Measured.X),MotionVector.Dot(offset,Measured.Y),MotionVector.Dot(offset,Measured.Z));
                GUI.Label(new Rect(right,top+78,half-10,24),L("轴向 / L1 / L2   ","Axial / L1 / L2   ")+MmVector(components),small);
                GUI.Label(new Rect(right,top+106,half-10,24),"L0  "+Position(outputValues[0])+"   ·   "+OutputStatus(),small);
            }
            else GUI.Label(new Rect(right,top,half-10,25),L("等待有效六轴输出","Waiting for valid six-axis output"),small);
            GUI.Label(new Rect(left,top+176,width-28,42),UiText.TranslateComposite(preview.Status)+L("   ·   坐标与距离以 mm 显示","   ·   Coordinates & distances in mm"),wrapSmall);
        }

        private void DrawActions()
        {
            float y=Card(18,332,874,257,L("动作目录","Action catalogue"));
            search=Edit(32,y,846,28,search);
            var visible=actions.FindAll(a=>search.Length==0||(a.mode+" "+a.id+" "+a.nameAnimation+" "+a.paramFemale.path.file+" "+ResolveActionDefinition(a,false).Name).IndexOf(search,StringComparison.OrdinalIgnoreCase)>=0);
            catalogueScroll=GUI.BeginScrollView(new Rect(32,y+38,846,150),catalogueScroll,new Rect(0,0,820,visible.Count*27));
            for(int i=0;i<visible.Count;i++)
            {
                var a=visible[i];var d=ResolveActionDefinition(a,false);
                GUI.Label(new Rect(0,i*27,805,26),d.Name+"  ·  "+ActionLabels.Source(d.Effector)+" / "+ActionLabels.Contact(d.Contact,d.Landmark),label);
            }
            GUI.EndScrollView();
            GUI.Label(new Rect(32,y+191,830,23),visible.Count+L(" 个动作 · 使用游戏菜单选择"," actions · select from the game menu"),small);
        }

        private void DrawRecordPage()
        {
            float y=Card(18,198,874,224,L("动作配置与录制","Action profile & recording"));
            GUI.enabled=Profile!=null||RecordRequested;
            if(Button(32,y,414,36,RecordRequested?L("停止录制并保存","Stop recording & save"):L("开始录制当前及后续动作","Record current & subsequent actions"),RecordRequested?dangerButton:primaryButton))SetRecording(!RecordRequested);
            GUI.enabled=Profile!=null;
            if(Button(462,y,416,36,replayRequested?L("来源 · 当前阶段脚本","Source · stage script"):L("来源 · 实时计算","Source · live calculation")))
            {
                if(RecordRequested)SetRecording(false);
                replayRequested=!replayRequested;playback.Clear();ResetMotionHistory();
                recordStatus=replayRequested?L("查找当前动作阶段的完整录制","Looking for a full take of the current stage"):L("使用实时计算","Using live calculation");
            }
            if(Button(32,y+48,414,32,L("保存当前动作配置","Save current action profile"))){SaveProfile();uiNotice=L("当前动作配置已保存","Current action profile saved");}
            GUI.enabled=true;
            if(Button(462,y+48,416,32,L("打开本体保存目录","Open data folder")))
                System.Diagnostics.Process.Start("explorer.exe","\""+DataRoot+"\"");
            GUI.Label(new Rect(32,y+96,840,46),recordStatus,wrapSmall);
            GUI.Label(new Rect(32,y+144,840,42),L("随原生阶段记录完整循环，保留中途片段。发送延迟独立于录制时间。","Records full loops per native stage and keeps partial takes. Send delay is independent of recording time."),wrapSmall);
            if(lastTake.Length>0)
            {
                y=Card(18,436,874,144,L("最近保存","Last saved"));
                GUI.Label(new Rect(32,y,844,78),lastTake,wrapSmall);
            }
        }

        private float Card(float x,float y,float w,float h,string caption)
        {
            if(cardStyle!=null)GUI.Box(new Rect(x,y,w,h),GUIContent.none,cardStyle);
            else{Box(new Rect(x,y,w,h),Edge);Box(new Rect(x+1,y+1,w-2,h-2),CardFill);}
            if(caption.Length==0)return y+36;
            if(pillAccentTex!=null)GUI.DrawTexture(new Rect(x+8,y+9,3,16),pillAccentTex);else Box(new Rect(x+1,y+9,3,17),Accent);
            GUI.Label(new Rect(x+17,y+4,w-28,26),caption,heading);
            return y+36;
        }
        private bool Button(float x,float y,float w,float h,string text,GUIStyle style=null)
        {var s=style??button;return GUI.Button(new Rect(x,y,w,h),text,FitStyle(s,text,w));}
        private string Edit(float x,float y,float w,float h,string value)
        {return GUI.TextField(new Rect(x,y,w,h),value,180,textField);}
        private static void Box(Rect rect,Color color)
        {var old=GUI.color;GUI.color=color;GUI.DrawTexture(rect,Texture2D.whiteTexture);GUI.color=old;}
        private static string Position(double value)
        {return Math.Round(Numbers.Clamp(value,0,1)*999).ToString("000",CultureInfo.InvariantCulture);}
        private static string Millimetres(double metres){return (metres*1000).ToString("0.0",CultureInfo.InvariantCulture);}
        private static string MmVector(MotionVector value)
        {return Millimetres(value.X)+" / "+Millimetres(value.Y)+" / "+Millimetres(value.Z);}

        private void RefreshCatalogue()
        {
            actions.Clear();if(Scene==null)return;var seen=new HashSet<string>();AddActions(RuntimeAccess.Read(Scene,"lstAnimInfo"),seen,0);AddActions(RuntimeAccess.Read(Scene,"lstUseAnimInfo"),seen,0);
            RefreshKPlugExtraAnimations(seen);
            actions.Sort((a,b)=>string.Compare(ResourceIdentity(a),ResourceIdentity(b),StringComparison.Ordinal));
        }
        private void AddActions(object value,HashSet<string> seen,int depth)
        {
            if(value==null||depth>5)return;var info=value as HSceneProc.AnimationListInfo;
            if(info!=null){if(seen.Add(ResourceIdentity(info)))actions.Add(info);return;}
            var dict=value as IDictionary;if(dict!=null){foreach(DictionaryEntry row in dict)AddActions(row.Value,seen,depth+1);return;}
            var list=value as IEnumerable;if(list!=null&&!(value is string))foreach(var row in list)AddActions(row,seen,depth+1);
        }
        private bool CanSelect(HSceneProc.AnimationListInfo info)
        {
            if(Scene==null||Flags==null)return false;string m=info.mode.ToString();
            if(m=="masturbation"||m=="peeping"||m=="lesbian")return Flags.mode==info.mode;
            if(m.IndexOf("MMF",StringComparison.Ordinal)>=0)return Actor(ActorSlot.Male)!=null&&Actor(ActorSlot.Male1)!=null&&Actor(ActorSlot.Female0)!=null;
            if(m.IndexOf("3P",StringComparison.Ordinal)>=0)return Actor(ActorSlot.Male)!=null&&Actor(ActorSlot.Female1)!=null;
            return Actor(ActorSlot.Male)!=null&&Actor(ActorSlot.Female0)!=null;
        }
        internal void SelectAction(HSceneProc.AnimationListInfo info)
        {if(CanSelect(info)){FinishTake(false,"action selected");Devices.Stop();Flags.selectAnimationListInfo=info;}}
    }
}

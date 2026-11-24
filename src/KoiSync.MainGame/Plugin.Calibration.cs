using System;
using System.Globalization;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.MainGame
{
    public sealed partial class Plugin
    {
        private string modelText="0",l1Text="80",l2Text="80",playerText="",strokeText="",gameText="";
        private readonly string[] originText={"0","0","0"},endText={"0","0","0"},entryText={"0","0","0"};

        private void DrawCalibrationPage()
        {
            bool bound=Binding!=null;
            bool endpoints=bound&&Binding.UsesEndpoints(Profile);
            float y=Card(18,198,430,241,L("来源标尺","Source span"));
            GUI.Label(new Rect(32,y,402,24),endpoints?L("端点距离 ","Endpoint span ")+Millimetres(Binding.Calibration.Span(Binding.Length))+" mm":
                bound?L("模型参考 ","Model ref ")+Millimetres(Binding.ModelLength)+L(" mm  ·  求解标尺 "," mm  ·  Active span ")+Millimetres(Binding.Length)+" mm":L("等待来源绑定","Waiting for source binding"),label);
            GUI.enabled=bound;
            DrawVector(32,y+28,L("起点 · 轴向 / L1 / L2（mm）","Origin · Axial / L1 / L2 (mm)"),originText);
            if(Button(350,y+52,84,28,L("应用起点","Apply")))EditOrigin(false);
            GUI.enabled=endpoints;
            DrawVector(32,y+88,L("末端 · 轴向 / L1 / L2（mm）","Tip · Axial / L1 / L2 (mm)"),endText);
            if(Button(350,y+112,84,28,L("应用末端","Apply")))EditEnd(false);
            GUI.enabled=bound;
            if(Button(32,y+155,130,28,L("恢复模型起点","Reset origin")))EditOrigin(true);
            if(Button(168,y+155,132,28,endpoints?L("对齐模型末端","Reset tip"):L("恢复参考长度","Reset length")))
            {
                if(endpoints)EditEnd(true);
                else EditModelLength(true);
            }
            GUI.enabled=endpoints;
            if(Button(306,y+155,128,28,L("场景手柄","Scene handles")))
            {showHandles.Value=true;showChannels=false;worldCalibration=true;}
            GUI.enabled=true;

            y=Card(18,449,430,140,endpoints?L("L1 / L2 响应","L1 / L2 response"):L("自动模式标尺与响应","Auto span & response"));
            GUI.enabled=bound;
            if(endpoints)
            {
                bool linked=GUI.Toggle(new Rect(32,y,402,23),Binding.Calibration.Linked,L("端点距离联动 L1 / L2 单侧范围","Link L1 / L2 ranges to endpoint span"),toggle);
                if(linked!=Binding.Calibration.Linked)
                {
                    var next=Binding.Calibration.Copy();next.Linked=linked;
                    ApplyCalibration(next,false);
                }
            }
            else
            {
                GUI.Label(new Rect(32,y+1,184,24),L("模型长度 mm · 0 自动","Model length mm · 0 auto"),small);
                modelText=Edit(210,y-2,94,27,modelText);
                if(Button(316,y-2,118,27,L("应用模型长度","Apply length")))EditModelLength(false);
            }
            DrawRange(1,y+30);DrawRange(2,y+60);
            GUI.enabled=true;

            y=Card(462,198,430,192,L("目标入口微调","Target entry offset"));
            GUI.Label(new Rect(476,y,402,23),geometryPose==null?L("等待入口观测","Waiting for entry observation"):Binding.Entry.Source,small);
            GUI.Label(new Rect(476,y+24,402,22),geometryPose==null?"":L("观测 XYZ（mm）   ","Observed XYZ (mm)   ")+MmVector(Binding.Entry.Observed.Origin),small);
            GUI.enabled=bound;
            DrawVector(476,y+49,L("目标局部偏移 · 入内 / L1 / L2（mm）","Local offset · Inward / L1 / L2 (mm)"),entryText);
            if(Button(794,y+73,84,28,L("应用偏移","Apply")))EditEntry(false);
            if(Button(476,y+110,192,28,L("入口偏移归零","Reset entry offset")))EditEntry(true);
            GUI.enabled=true;
            if(Button(674,y+110,204,28,showEntryMarkers.Value?L("隐藏场景入口标记","Hide entry markers"):L("显示场景入口标记","Show entry markers")))
                showEntryMarkers.Value=!showEntryMarkers.Value;

            y=Card(462,400,430,132,L("手动标尺与入口平面","Manual span & entry plane"));
            GUI.enabled=bound;
            bool manualMode=Profile!=null&&Profile.Mode==AxisMappingMode.Manual;
            bool nextManual=GUI.Toggle(new Rect(716,y-31,164,23),manualMode,L("启用手动标尺","Manual span"),toggle);
            if(bound&&nextManual!=manualMode)SetMappingMode(nextManual?AxisMappingMode.Manual:AxisMappingMode.Soft);
            GUI.enabled=bound&&Profile.Mode==AxisMappingMode.Manual;
            for(int i=0;i<3;i++)DrawPlane(i,y+i*28);
            GUI.enabled=true;

            y=Card(462,542,430,47,"");
            previewEnabled.Value=GUI.Toggle(new Rect(474,y-26,74,24),previewEnabled.Value,L("预览","Model"),toggle);
            xray.Value=GUI.Toggle(new Rect(548,y-26,74,24),xray.Value,L("透视","X-ray"),toggle);
            showHandles.Value=GUI.Toggle(new Rect(622,y-26,76,24),showHandles.Value,L("手柄","Handles"),toggle);
            int view=GUI.Toolbar(new Rect(700,y-27,181,27),showDevicePose.Value?1:0,PoseViews,button);
            showDevicePose.Value=view==1;
        }

        private void DrawVector(float x,float y,string caption,string[] input)
        {
            GUI.Label(new Rect(x,y,305,21),caption,small);
            for(int i=0;i<3;i++)input[i]=Edit(x+i*103,y+24,94,28,input[i]);
        }

        private void DrawRange(int axis,float y)
        {
            string text=axis==1?l1Text:l2Text;
            GUI.Label(new Rect(32,y+2,76,24),"L"+axis+"  ± mm",small);
            text=Edit(109,y,70,25,text);
            if(axis==1)l1Text=text;else l2Text=text;
            double current=Binding==null?.08:axis==1?Binding.Calibration.L1:Binding.Calibration.L2;
            double slider=GUI.HorizontalSlider(new Rect(190,y+7,148,20),(float)(current*1000),1,500);
            if(Math.Abs(slider-current*1000)>.05)EditRange(axis,Math.Round(slider,1)/1000,true);
            if(Button(350,y,84,25,L("应用范围","Apply")))
                UiEdit(()=>EditRange(axis,ReadMillimetres(text,1,500),false));
        }

        private void DrawPlane(int axis,float y)
        {
            var plane=Binding==null?new MotionVector():Binding.Calibration.PlaneDegrees;
            double previous=axis==0?plane.X:axis==1?plane.Y:plane.Z;
            var captions=new[]{L("R0 扭转","R0 twist"),L("R1 侧倾","R1 roll"),L("R2 俯仰","R2 pitch")};
            GUI.Label(new Rect(476,y,74,24),captions[axis],small);
            float limit=axis==0?180:90;
            double angle=GUI.HorizontalSlider(new Rect(552,y+5,216,20),(float)previous,-limit,limit);
            GUI.Label(new Rect(777,y,61,24),previous.ToString("0.0",CultureInfo.InvariantCulture)+"°",small);
            bool reset=Button(832,y,46,24,L("归零","Reset"));
            if(!reset&&Math.Abs(angle-previous)<.049)return;
            var next=Binding.Calibration.Copy();
            double value=reset?0:Math.Round(angle,1);
            if(axis==0)plane.X=value;else if(axis==1)plane.Y=value;else plane.Z=value;
            next.PlaneDegrees=plane;ApplyCalibration(next,!reset);
        }

        private void DrawMappingPage()
        {
            float y=Card(18,198,430,126,L("姿态旋转居中 · 防顶死","Rotation centering · anti-clipping"));
            bool isRel=Profile!=null&&Profile.Mode==AxisMappingMode.Relative;
            bool isMan=Profile!=null&&Profile.Mode==AxisMappingMode.Manual;
            GUI.enabled=Profile!=null;
            if(Button(32,y,195,30,L("柔顺防顶死 (默认)","Soft anti-clip (default)"),!isRel&&!isMan?activeButton:button))SetMappingMode(AxisMappingMode.Soft);
            if(Button(235,y,195,30,L("相对姿态居中","Relative centering"),isRel?activeButton:button))SetMappingMode(AxisMappingMode.Relative);
            GUI.enabled=Profile!=null&&isRel&&!outputFromScript&&ActionRotationReference.CanCapture(Measured);
            if(Button(32,y+38,162,28,L("重取当前旋转基准","Recapture rotation ref")))
            {
                BeginDriveEdit("rotation reference changed");
                Profile.Rotation=ActionRotationReference.Capture(Measured,phaseTime);calibrationDirty=true;
                ResetMotionHistory();FinishDriveEdit();uiNotice=L("已保存当前来源的旋转基准","Saved rotation reference for current source");
            }
            GUI.enabled=true;
            string rotHint=isMan?L("当前在「入口校准」页启用了手动标尺","Manual span is currently active on the Calibration tab"):
                isRel?(Profile.Rotation!=null?L("已保存相对旋转基准，大倾角体位居中活动","Relative rotation ref saved; steep poses stay centered"):L("充分接触时自动捕获本动作旋转基准","Captures rotation ref automatically on full contact")):
                L("大仰角自动软肩压缩，防止 R0/R1/R2 顶死","Soft shoulder compression prevents R0/R1/R2 clipping");
            GUI.Label(new Rect(202,y+40,236,38),rotHint,wrapSmall);

            y=Card(18,334,430,142,L("AI 导演接口 · 本地 MCP","AI Director API · Local MCP"));
            bool dirOn=directorEnabled!=null&&directorEnabled.Value;
            bool nextDirOn=GUI.Toggle(new Rect(32,y,398,23),dirOn,L("允许本地 AI 导演接入 (127.0.0.1:","Allow local AI Director API (127.0.0.1:")+DirectorActivePort+")",toggle);
            if(nextDirOn!=dirOn)SetDirectorEnabled(nextDirOn);
            string bridgeState=DirectorListening?L("监听就绪 · 支持连招/有机随机/高潮射精","Listening · sequences / organic / climax ready"):(dirOn?L("端口占用：","Port busy: ")+(directorBridge!=null?directorBridge.LastError:""):L("已关闭","Disabled"));
            GUI.Label(new Rect(32,y+28,402,38),bridgeState+"  ·  "+DirectorStatusText(),wrapSmall);
            GUI.enabled=AnyDirectorActive;
            if(Button(32,y+68,195,28,L("立刻切断 AI 接管","Cut AI takeover now"),dangerButton))
            {
                AbortDirector(L("用户手动切断","Cut manually by user"));
                uiNotice=L("已立刻切断 AI 接管，恢复正常游戏同步","AI takeover cut; restored normal game sync");
            }
            GUI.enabled=Director.EdgeGuardEnabled;
            if(Button(235,y+68,195,28,L("关闭防射守护","Disable edge guard")))
            {
                var eg=Director.SnapshotEdgeGuard();eg.Enabled=false;Director.ConfigureEdgeGuard(eg);
                uiNotice=L("防射守护已关闭","Edge guard disabled");
            }
            GUI.enabled=true;

            y=Card(462,198,430,278,L("L0 行程范围（默认 0–99）","L0 travel range (default 0–99)"));
            bool endpoints=Binding!=null&&Binding.UsesEndpoints(Profile);
            if(endpoints)
            {
                GUI.Label(new Rect(476,y,402,27),L("有效标尺   ","Active span   ")+Millimetres(Binding.Calibration.Span(Binding.Length))+" mm",label);
                GUI.Label(new Rect(476,y+38,402,54),L("在入口校准页编辑起末点。L0 以两点距离归一化，六轴行程与响应仍可调整。","Edit origin and tip on the Calibration tab. L0 normalizes to their distance; six-axis limits still apply."),wrapSmall);
            }
            else
            {
                GUI.Label(new Rect(476,y,402,42),L("已取消玩家长度底限截断，默认释放完整 L0 行程（0–99）。如需限制深度，请在六轴面板调节 L0 Min / Max 或下方外限滑块。","Player-length bottom clipping removed; full L0 travel (0–99) is active by default. Adjust L0 Min/Max or Outer L0 below if needed."),wrapSmall);
                GUI.Label(new Rect(476,y+52,402,24),L("当前 L0 范围：","Current L0 range: ")+Math.Round(Drive.Minimum[0]*99.0).ToString("0",CultureInfo.InvariantCulture)+" – "+Math.Round(Math.Min(Drive.Maximum[0],ActiveOuterCeilingL0)*99.0).ToString("0",CultureInfo.InvariantCulture)+" / 99",label);
                if(Button(476,y+86,196,32,L("恢复默认 0–99 满行程","Reset full 0–99 range"),primaryButton))
                {
                    BeginDriveEdit("reset L0 0-99");
                    lengthAdaptation.Value=false;
                    Drive.Minimum[0]=0;Drive.Maximum[0]=1;
                    min[0].Value=0;max[0].Value=1;
                    ActiveOuterCeilingL0=FeelTranslator.DefaultOuterCeiling;
                    ResetMotionHistory();Config.Save();FinishDriveEdit();
                    uiNotice=L("L0 已恢复 0–99 全幅行程","L0 restored to full 0–99 travel");
                }
            }
            GUI.Label(new Rect(476,y+134,402,54),
                ActiveFeelPreset!=FeelPreset.Raw
                    ?L("当前由下方体感翻译层驱动 L0 黄金全幅行程（0–99），自动适配各体位与高速冲刺。","Feel Layer below drives full 0–99 L0 travel across all poses and fast sprints.")
                    :L("当前为「原味几何」1:1 直出，无底限截断。","Raw Geometry mode outputs 1:1 skeletal travel without length clipping."),wrapSmall);

            y=Card(18,486,874,103,L("体感翻译层 · MultiFunPlayer 节拍映射（全局）","Feel Translation Layer · Rhythmic Mapping (Global)"));
            var presetNames=new[]{L("原味几何 (1:1)","Raw (1:1)"),L("均衡体感 (推荐)","Balanced (Rec.)"),L("强爽激进 (高潮感)","Intense")};
            int curPreset=(int)ActiveFeelPreset;
            for(int i=0;i<3;i++)
            {
                if(Button(32+i*150,y,142,28,presetNames[i],i==curPreset?activeButton:button))
                {
                    BeginDriveEdit("feel preset changed");
                    ActiveFeelPreset=(FeelPreset)i;
                    if(i!=0&&outputRate.Value<(float)(FeelTranslator.RecommendedRateCap((FeelPreset)i)*100.0))
                    {outputRate.Value=(float)(FeelTranslator.RecommendedRateCap((FeelPreset)i)*100.0);ApplyResponseSettings();}
                    ResetMotionHistory();Config.Save();FinishDriveEdit();
                    uiNotice=L("已切换体感档位：","Feel preset switched: ")+presetNames[i];
                }
            }
            GUI.Label(new Rect(492,y+3,386,25),FeelStatus(),label);
            GUI.Label(new Rect(32,y+35,104,24),L("非插入体感：","Non-shaft:"),small);
            var nonShaftNames=new[]{L("柔和前戏 (默认)","Foreplay (Def.)"),L("全幅同步","Full sync"),L("静默待机","Silent park")};
            int curNonShaft=(int)ActiveNonShaftFeelMode;
            for(int i=0;i<3;i++)
            {
                if(Button(134+i*128,y+32,120,26,nonShaftNames[i],i==curNonShaft?activeButton:button))
                {
                    BeginDriveEdit("non-shaft feel mode changed");
                    ActiveNonShaftFeelMode=(NonShaftFeelMode)i;
                    ResetMotionHistory();Config.Save();FinishDriveEdit();
                    uiNotice=L("非插入体感已切换：","Non-shaft mode switched: ")+nonShaftNames[i];
                }
            }
            GUI.Label(new Rect(522,y+35,84,24),L("L0 外限","Outer L0"),small);
            float curCeil=(float)Math.Round(ActiveOuterCeilingL0*100.0,1);
            float nextCeil=GUI.HorizontalSlider(new Rect(606,y+40,174,20),curCeil,55f,99f);
            GUI.Label(new Rect(786,y+35,48,24),curCeil.ToString("0",CultureInfo.InvariantCulture)+"%",small);
            bool resetCeil=Button(836,y+33,42,24,"99");
            if(resetCeil||Math.Abs(nextCeil-curCeil)>=0.5f)
            {
                ActiveOuterCeilingL0=(resetCeil?99.0:Math.Round(nextCeil))/100.0;
                Config.Save();
                uiNotice=L("L0 外限已设为 ","Outer L0 limit set to ")+Math.Round(ActiveOuterCeilingL0*100.0).ToString("0",CultureInfo.InvariantCulture)+"%";
            }
        }

        private void StrokeField(float x,float y,string caption,ref string input)
        {
            GUI.Label(new Rect(x,y+2,217,25),caption,small);
            input=Edit(x+218,y,184,28,input);
        }

        private void SetMappingMode(AxisMappingMode mode)
        {
            if(Profile.Mode==mode&&!replayRequested)return;
            BeginDriveEdit("mapping changed");Profile.Mode=mode;ClearPlayback();
            if(Binding!=null&&Binding.UsesEndpoints(Profile))Binding.EnsureEndpoints();
            calibrationDirty=true;ResetMotionHistory();FinishDriveEdit();RefreshCalibrationText();
        }
        private void EditOrigin(bool restore)
        {
            UiEdit(()=>
            {
                var next=Binding.Calibration.Copy();
                var origin=restore?new MotionVector():ReadVector(originText,3000);
                if(Binding.UsesEndpoints(Profile))
                    next.SetEndpoints(origin,next.ManualEnd.Value,next.Span(Binding.Length));
                else next.Origin=origin;
                ApplyCalibration(next,false,setsEndpoints:Binding.UsesEndpoints(Profile));
            });
        }
        private void EditEnd(bool restore)
        {
            UiEdit(()=>
            {
                var next=Binding.Calibration.Copy();
                var tip=restore?Binding.ReadModelEndOffset():ReadVector(endText,3500);
                next.SetEndpoints(next.Origin,tip,next.Span(Binding.Length));
                ApplyCalibration(next,false,setsEndpoints:true);
            });
        }
        private void EditModelLength(bool restore)
        {
            UiEdit(()=>
            {
                double length=restore?0:ReadMillimetres(modelText,0,500);
                if(length>0&&!ShaftStrokeMapping.ValidModelLength(length))throw new ArgumentException(L("模型长度须为 0 或 1–500 mm","Model length must be 0 or 1–500 mm"));
                var next=Binding.Calibration.Copy();
                next.ScaleRanges(Binding.Length,length==0?Binding.ModelLength:length);next.Length=length;
                ApplyCalibration(next,false);
            });
        }
        private void EditRange(int axis,double value,bool dragging)
        {
            if(Binding==null)return;
            var next=Binding.Calibration.Copy();
            if(axis==1)next.L1=value;else next.L2=value;
            ApplyCalibration(next,dragging);
        }
        private void EditEntry(bool reset)
        {
            UiEdit(()=>
            {
                var offset=reset?new MotionVector():ReadVector(entryText,500);
                BeginDriveEdit("entry offset changed");
                Profile.SetEntryOffset(Binding.Contact,offset);calibrationDirty=true;
                ClearPlayback();ResetMotionHistory();FinishDriveEdit();RefreshCalibrationText();
                uiNotice=L("已保存当前部位的入口偏移","Saved entry offset for current zone");
            });
        }
        private void EditStroke(bool endpoints)
        {
            UiEdit(()=>
            {
                double player=endpoints?PlayerLength:ReadMillimetres(playerText,1,500);
                double travel=ReadMillimetres(strokeText,20,500);
                double game=Profile!=null&&!endpoints?ReadMillimetres(gameText,20,500):0;
                BeginDriveEdit("stroke calibration changed");
                playerLength.Value=(float)(player*1000);deviceStroke.Value=(float)(travel*1000);
                if(game>0){Profile.GameLength=game;calibrationDirty=true;}
                Config.Save();ResetMotionHistory();FinishDriveEdit();RefreshCalibrationText();
                uiNotice=L("长度参数已保存","Length settings saved");
            });
        }

        private void ApplyCalibration(ActionCalibration next,bool dragging,bool setsEndpoints=false)
        {
            next.Validate();
            BeginDriveEdit("action calibration changed");
            Binding.ApplyCalibration(next);
            if(setsEndpoints)Profile.ReferenceReady=true;
            calibrationDirty=true;ClearPlayback();ResetMotionHistory();RefreshCalibrationText();
            if(!dragging){FinishDriveEdit();uiNotice=L("当前动作校准已保存","Action calibration saved");}
        }

        private void BeginDriveEdit(string reason)
        {
            if(!editingDrive)RestartCapture(reason);
            editingDrive=true;Devices.Stop();
        }
        private void FinishDriveEdit()
        {
            if(!editingDrive&&!responseDirty&&!axesDirty&&!calibrationDirty)return;
            editingDrive=false;
            UiEdit(()=>
            {
                if(calibrationDirty){calibrationDirty=false;SaveProfile();}
                if(axesDirty){axesDirty=false;SaveAxes();}
                if(responseDirty){responseDirty=false;SaveResponse();}
            });
        }

        private void RefreshCalibrationText()
        {
            playerText=Millimetres(PlayerLength);strokeText=Millimetres(DeviceStroke);
            gameText=Millimetres(Profile==null?ShaftStrokeMapping.ReferenceInteraction:Profile.GameLength);
            if(Binding==null)return;
            var c=Binding.Calibration;
            modelText=EditMillimetres(c.Length);l1Text=EditMillimetres(c.L1);l2Text=EditMillimetres(c.L2);
            RefreshVector(originText,c.Origin);
            RefreshVector(endText,c.ManualEnd??c.Origin+new MotionVector(Binding.Length,0,0));
            RefreshVector(entryText,Profile.EntryOffset(Binding.Contact));
        }
        private static void RefreshVector(string[] input,MotionVector value)
        {input[0]=EditMillimetres(value.X);input[1]=EditMillimetres(value.Y);input[2]=EditMillimetres(value.Z);}
        private static string EditMillimetres(double metres){return (metres*1000).ToString("G10",CultureInfo.InvariantCulture);}
        private static MotionVector ReadVector(string[] input,double limit)
        {return new MotionVector(ReadMillimetres(input[0],-limit,limit),ReadMillimetres(input[1],-limit,limit),ReadMillimetres(input[2],-limit,limit));}
        private static double ReadMillimetres(string text,double min,double max)
        {
            double value;
            if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||!Numbers.Finite(value)||value<min||value>max)
                throw new ArgumentException(L("请输入 ","Enter a value between ")+min+L(" 至 "," and ")+max+L(" mm 之间的数值"," mm"));
            return value/1000;
        }
        private void UiEdit(Action edit)
        {try{edit();}catch(Exception error){uiNotice=error.GetBaseException().Message;}}
    }
}

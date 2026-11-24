using System;
using System.Globalization;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private bool chapterEffectPicker,chapterFrequencyPicker,chapterGrindPicker;
        private Rect chapterEffectPickerArea,chapterFrequencyPickerArea,chapterGrindPickerArea;
        private Chapter effectPopupChapter,frequencyUndoChapter;
        private int climaxRangeDrag=-1;
        private Chapter EffectEditorChapter {get{return selectedChapter!=null&&Book.Chapters.Contains(selectedChapter)?selectedChapter:Book.At(Clock.Time);}}

        private void DrawChapterEffectSettings(Rect card)
        {
            var chapter=EffectEditorChapter;
            float y=Card(card,L("章节效果 · 所选章节","Chapter effect · selected chapter"));float x=card.x+12,w=card.width-24,field=w-124;
            GUI.Label(new Rect(x,y,w,22),chapter==null?L("未选择章节","No chapter selected"):"#"+(Book.Chapters.IndexOf(chapter)+1)+"  "+ChapterTitle(chapter),small);
            GUI.enabled=chapter!=null;
            GUI.Label(new Rect(x,y+31,120,24),L("章节类型 / 效果","Chapter type / effect"),small);
            var effectRect=new Rect(x+124,y+28,field,28);
            if(B(effectRect,(chapter!=null&&chapter.IsClimax?L("高潮 / 射精","Climax / ejaculation"):L("普通章节","Normal chapter"))+"  ▾"))
            {
                ClosePopups();ClearUiTextFocus();effectPopupChapter=chapter;chapterEffectPicker=true;
                chapterEffectPickerArea=new Rect(effectRect.x,ContentTop+effectRect.yMax+2,effectRect.width,68);
            }
            var options=ChapterEffectOptions(chapter);
            if(Event.current.rawType==EventType.MouseDown||Event.current.rawType==EventType.KeyDown)frequencyUndoChapter=null;

            if(chapter!=null&&chapter.IsClimax)
            {
                GUI.enabled=true;
                GUI.Label(new Rect(x,y+65,120,24),L("高潮同步模式","Climax sync mode"),small);
                var frequencyRect=new Rect(x+124,y+62,field,28);
                if(B(frequencyRect,ClimaxModeLabel(options.FrequencyMode)+"  ▾"))
                {
                    ClosePopups();ClearUiTextFocus();effectPopupChapter=chapter;chapterFrequencyPicker=true;
                    chapterFrequencyPickerArea=new Rect(frequencyRect.x,ContentTop+frequencyRect.yMax+2,frequencyRect.width,128);
                }
                if(options.FrequencyMode==ClimaxFrequencyMode.Fixed||options.FrequencyMode==ClimaxFrequencyMode.AutoMotion)
                {
                    double hz=Math.Round(GUI.HorizontalSlider(new Rect(x,y+104,w-90,20),(float)options.Hertz,.1f,20),1);
                    GUI.Label(new Rect(x+w-80,y+97,80,26),hz.ToString("0.0",CultureInfo.InvariantCulture)+" Hz",label);
                    if(Math.Abs(hz-options.Hertz)>.049)SetChapterFrequency(chapter,options.FrequencyMode,hz,true);
                }
                else if(options.FrequencyMode==ClimaxFrequencyMode.Simulated)
                {
                    GUI.Label(new Rect(x,y+98,w,26),L("高潮保持实时计算/手感模拟，不覆写定频震动","Keep live simulated motion during climax (no fixed vibe)"),small);
                }
                else GUI.Label(new Rect(x,y+98,w,26),chapter!=RotationChapter()?L("播放本章以读取实际频率","Play this chapter to measure frequency"):
                    liveL0Cycle.Ready?L("实测：","Measured: ")+liveL0Cycle.Hertz.ToString("0.00",CultureInfo.InvariantCulture)+" Hz":L("采样中 · 请播放完整往复","Sampling · play a full cycle"),small);
                string modeNote=UseScripts?L("脚本回放使用录制值；模式设置用于实时与烘焙","Script playback uses recorded values; mode applies to live & bake"):
                    options.FrequencyMode==ClimaxFrequencyMode.AutoMotion?
                        (ShouldUseFixedClimaxOscillation(chapter)?L("智能判定：本章抽插幅度/频率较低 → 启用定频震动","Smart: low scene motion → fixed climax vibration"):
                        L("智能判定：本章仍在积极抽插 → 保持计算模拟数据","Smart: active thrusting → keep simulated motion")):
                    L("每章独立保存 · 可自由切换定频或计算模拟","Saved per chapter · switch freely between fixed or simulated");
                GUI.Label(new Rect(x,y+126,w,24),modeNote,small);
                Divider(x,y+156,w);
                DrawChapterOutputSettings(chapter,options,x,y+166,w);
            }
            else
            {
                Divider(x,y+64,w);
                DrawChapterGrindSettings(chapter,options,x,y+72,w);
            }
            GUI.enabled=true;
            if(Event.current.rawType==EventType.MouseUp)frequencyUndoChapter=null;
        }

        private void DrawChapterGrindSettings(Chapter chapter,ClimaxOptions climaxOpt,float x,float y,float w)
        {
            bool direct=chapter!=null&&StudioGrindEngine.IsDirectInsertionOpening(chapter.Opening);
            GUI.Label(new Rect(x,y,w,22),L("✦ 科研 6D 研磨与骨架权重联动（直插章节）","✦ 6D Research Grinding & Skeleton Coupling (direct insertion)"),header);
            if(chapter==null)
            {
                GUI.Label(new Rect(x,y+28,w,40),L("请选择一个章节以配置 6D 研磨增强与骨架物理权重。","Select a chapter to configure 6D grinding and skeleton coupling."),wrapSmall);
                return;
            }
            if(!direct)
            {
                GUI.Label(new Rect(x,y+28,w,48),L("当前章节为手部/胸部非直插互动；科研 6D 骨盆研磨与骨架反写仅面向阴道、肛门、口部直接插入章节开放。","Current chapter is hand/chest interaction; 6D pelvic grinding & skeleton reverse-coupling applies to Vaginal, Anal and Oral insertion chapters."),wrapSmall);
                bool detectNon=Check(new Rect(x,y+90,w,24),climaxOpt.Detect,L("自动分析时识别本章高潮事件","Detect this chapter's climax during analysis"));
                if(detectNon!=climaxOpt.Detect)
                {
                    RememberChapters();var u=climaxOpt.Copy();u.LocalSettings=true;u.Detect=detectNon;u.Validate();
                    chapter.Climax=u;chapter.EffectsEdited=true;MarkSceneDataDirty();
                }
                return;
            }

            var grind=chapter.Grind!=null?chapter.Grind.Copy():new ChapterGrindOptions();
            float field=w-104;
            GUI.Label(new Rect(x,y+30,100,24),L("研磨轨迹","Grind curve"),small);
            var patRect=new Rect(x+104,y+27,field,28);
            if(B(patRect,GrindPatternLabel(grind.Pattern)+"  ▾"))
            {
                ClosePopups();ClearUiTextFocus();effectPopupChapter=chapter;chapterGrindPicker=true;
                chapterGrindPickerArea=new Rect(patRect.x,ContentTop+patRect.yMax+2,patRect.width,248);
            }

            bool on=grind.Enabled;
            GUI.enabled=on;
            GUI.Label(new Rect(x,y+64,76,22),L("研磨强度","Intensity"),small);
            double nextInt=Math.Round(GUI.HorizontalSlider(new Rect(x+80,y+70,w-140,20),(float)(grind.Intensity*100.0),10f,100f))/100.0;
            GUI.Label(new Rect(x+w-54,y+63,54,24),Math.Round(nextInt*100.0).ToString("0",CultureInfo.InvariantCulture)+"%",label);

            GUI.Label(new Rect(x,y+94,76,22),L("研磨频率","Grind Hz"),small);
            double nextHz=Math.Round(GUI.HorizontalSlider(new Rect(x+80,y+100,w-140,20),(float)grind.Hertz,.4f,3.0f)*10.0)/10.0;
            GUI.Label(new Rect(x+w-54,y+93,54,24),nextHz.ToString("0.0",CultureInfo.InvariantCulture)+"Hz",label);

            GUI.Label(new Rect(x,y+125,76,22),L("主发力方","Mover"),small);
            var moverNames=new[]{L("自动","Auto"),L("女主磨","Female"),L("男主磨","Male"),L("双向","Both")};
            int nextMover=GUI.Toolbar(new Rect(x+80,y+123,w-80,25),(int)grind.MoverMode,moverNames,button);

            bool nextDepthW=Check(new Rect(x,y+156,w,23),grind.DepthWeighted,L("按插入深度加权（深插渐强，拔出渐柔）","Depth-weighted (stronger deep, softer out)"));
            bool nextSkel=Check(new Rect(x,y+182,w,23),grind.LinkSkeleton,L("反向联动角色骨架（骨盆三段 + 脊柱S波 + 肢体IK）","Reverse-drive skeleton (3-tier pelvis + spine S-wave + IK)"));
            bool nextSoft=Check(new Rect(x,y+208,w,23),grind.SoftTissue,L("同步胸/臀软组织物理权重（DynamicBone 惯性）","Couple chest/butt DynamicBone soft-tissue weights"));
            GUI.enabled=true;

            if(on&&(Math.Abs(nextInt-grind.Intensity)>.009||Math.Abs(nextHz-grind.Hertz)>.049||nextMover!=(int)grind.MoverMode||nextDepthW!=grind.DepthWeighted||nextSkel!=grind.LinkSkeleton||nextSoft!=grind.SoftTissue))
            {
                if(frequencyUndoChapter!=chapter){RememberChapters();frequencyUndoChapter=chapter;}
                grind.Intensity=nextInt;grind.Hertz=nextHz;grind.MoverMode=(ActiveMoverMode)nextMover;
                grind.DepthWeighted=nextDepthW;grind.LinkSkeleton=nextSkel;grind.SoftTissue=nextSoft;
                grind.Validate();chapter.Grind=grind;chapter.EffectsEdited=true;MarkSceneDataDirty();
            }

            Divider(x,y+240,w);
            string liveInfo=on&&bodyGrind.Active
                ?L("✦ 实时联动中：","✦ Live coupling: ")+bodyGrind.ResolvedMover+L(" · 骨盆 "," · Pelvis ")+bodyGrind.PrimaryHipDispMm.ToString("0.0",CultureInfo.InvariantCulture)+L("mm · 软组织 ","mm · Soft ")+bodyGrind.SoftTissueBonesSynced+L(" 组"," bones")
                :on?L("✦ 已启用本章 6D 研磨：播放实时联动骨架，「一键高速烘焙 6 轴」自动写入全部六轴脚本。","✦ 6D grind armed: live playback drives skeleton & Fast Bake writes all 6 axes.")
                :L("选择任意研磨轨迹即可在保留原抽插的同时叠加 6D 研磨与骨架联动。","Pick any grind curve to layer 6D grinding & reverse skeleton motion onto this chapter.");
            GUI.Label(new Rect(x,y+248,w,40),liveInfo,wrapSmall);

            bool detect=Check(new Rect(x,y+294,w,24),climaxOpt.Detect,L("自动分析时识别本章高潮事件","Detect this chapter's climax during analysis"));
            if(detect!=climaxOpt.Detect)
            {
                RememberChapters();var u=climaxOpt.Copy();u.LocalSettings=true;u.Detect=detect;u.Validate();
                chapter.Climax=u;chapter.EffectsEdited=true;MarkSceneDataDirty();
            }
        }

        private string GrindPatternLabel(StudioGrindPattern pattern)
        {
            switch(pattern)
            {
                case StudioGrindPattern.Circle:return L("圆周深磨 (360° 扭腰)","Circle orbit (360°)");
                case StudioGrindPattern.Figure8:return L("∞ 字八字研磨","Figure-8 sway");
                case StudioGrindPattern.Corkscrew:return L("螺旋钻磨 (强 R0 旋拧)","Corkscrew twist");
                case StudioGrindPattern.RockFB:return L("前后深顶碾磨 (G点俯仰)","Deep F/B pelvic rock");
                case StudioGrindPattern.LineFB:return L("纯前后直线推磨 (L1+R2)","Straight F/B (L1+R2)");
                case StudioGrindPattern.LineLR:return L("纯左右直线横磨 (L2+R1)","Straight L/R (L2+R1)");
                case StudioGrindPattern.Lissajous6D:return L("6D 黄金比例李萨如 (科研流形)","6D Golden Lissajous");
                default:return L("关闭（保持原场景动作）","Off (scene motion only)");
            }
        }

        private void DrawChapterGrindPicker()
        {
            var chapter=effectPopupChapter;
            if(chapter==null||!Book.Chapters.Contains(chapter)){chapterGrindPicker=false;return;}
            var area=chapterGrindPickerArea;var e=Event.current;
            if(e.type==EventType.MouseDown&&!area.Contains(e.mousePosition)||e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape)
            {chapterGrindPicker=false;ClearUiTextFocus();e.Use();return;}
            Popup(area);
            var cur=chapter.Grind!=null?chapter.Grind.Pattern:StudioGrindPattern.Off;
            for(int i=0;i<=7;i++)
            {
                var p=(StudioGrindPattern)i;
                if(B(new Rect(area.x+4,area.y+4+i*30,area.width-8,27),(cur==p?"● ":"")+GrindPatternLabel(p)))
                {
                    RememberChapters();
                    var next=chapter.Grind!=null?chapter.Grind.Copy():new ChapterGrindOptions();
                    next.Pattern=p;
                    if(p!=StudioGrindPattern.Off&&next.Intensity<.1)next.Intensity=.65;
                    next.Validate();
                    chapter.Grind=p==StudioGrindPattern.Off?null:next;
                    chapter.EffectsEdited=true;
                    MarkSceneDataDirty();
                    chapterGrindPicker=false;ClearUiTextFocus();
                    status=p==StudioGrindPattern.Off?L("本章 6D 研磨已关闭","Chapter 6D grind disabled"):L("本章已启用科研研磨：","Chapter 6D grind enabled: ")+GrindPatternLabel(p);
                    if(e.type!=EventType.Used)e.Use();return;
                }
            }
        }

        private void DrawChapterOutputSettings(Chapter chapter,ClimaxOptions options,float x,float y,float w)
        {
            GUI.enabled=chapter!=null;
            bool enabled=Check(new Rect(x,y,w,24),options.Enabled,L("本章高潮启用 L0 效果","Enable climax L0 for this chapter"));
            float minimum,maximum;
            DrawClimaxRangeSlider(x,y+28,w,(float)options.Minimum,(float)options.Maximum,out minimum,out maximum);
            bool detect=Check(new Rect(x,y+86,w,24),options.Detect,L("自动分析时识别本章高潮事件","Detect this chapter's climax during analysis"));
            GUI.Label(new Rect(x,y+114,w,22),L("自动射精事件最长持续时间","Maximum detected shooting duration"),small);
            float duration=(float)Math.Round(GUI.HorizontalSlider(new Rect(x,y+142,w-90,20),(float)options.MaximumSeconds,.1f,60)*10)/10;
            GUI.Label(new Rect(x+w-80,y+135,80,26),duration.ToString("0.0",CultureInfo.InvariantCulture)+L(" 秒"," s"),label);
            if(chapter!=null&&(enabled!=options.Enabled||minimum!=(float)options.Minimum||maximum!=(float)options.Maximum||detect!=options.Detect||duration!=(float)options.MaximumSeconds))
            {
                if(frequencyUndoChapter!=chapter){RememberChapters();frequencyUndoChapter=chapter;}
                var updated=options.Copy();updated.LocalSettings=true;updated.Enabled=enabled;updated.Minimum=minimum;updated.Maximum=maximum;updated.Detect=detect;updated.MaximumSeconds=duration;updated.Validate();
                chapter.Climax=updated;chapter.EffectsEdited=true;MarkSceneDataDirty();RefreshActiveClimaxPulse();
            }
            GUI.enabled=true;
            GUI.Label(new Rect(x,y+170,w,40),L("仅本章生效；普通章节可叠加 6D 科研研磨与骨架权重联动。","This chapter only; normal direct-insertion chapters support 6D grinding & skeleton coupling."),wrapSmall);
        }
        private void SetChapterFrequency(Chapter chapter,ClimaxFrequencyMode mode,double hertz,bool slider)
        {
            if(chapter==null||!Book.Chapters.Contains(chapter)||!chapter.IsClimax)return;
            if(!slider||frequencyUndoChapter!=chapter){RememberChapters();frequencyUndoChapter=chapter;}
            chapter.Climax=ChapterEffectOptions(chapter);
            chapter.Climax.FrequencyMode=mode;chapter.Climax.Hertz=hertz;chapter.Climax.Validate();chapter.EffectsEdited=true;
            MarkSceneDataDirty();RefreshActiveClimaxPulse();
        }
        private string ClimaxModeLabel(ClimaxFrequencyMode mode)
        {
            switch(mode)
            {
                case ClimaxFrequencyMode.AutoMotion:return L("智能（低动频震动 / 抽插模拟）","Smart (vibe if low motion)");
                case ClimaxFrequencyMode.Fixed:return L("始终固定频率震动","Always fixed frequency");
                case ClimaxFrequencyMode.Simulated:return L("始终计算模拟（不震动）","Always simulated motion");
                case ClimaxFrequencyMode.LiveL0:return L("跟随实际 L0 周期","Follow actual L0 cycle");
                default:return L("智能（低动频震动 / 抽插模拟）","Smart (vibe if low motion)");
            }
        }
        private void DrawChapterFrequencyPicker()
        {
            var chapter=effectPopupChapter;
            if(chapter==null||!Book.Chapters.Contains(chapter)){chapterFrequencyPicker=false;return;}
            var area=chapterFrequencyPickerArea;var e=Event.current;
            if(e.type==EventType.MouseDown&&!area.Contains(e.mousePosition)||e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape)
            {chapterFrequencyPicker=false;ClearUiTextFocus();e.Use();return;}
            Popup(area);
            var options=ChapterEffectOptions(chapter);
            var modes=new[]{ClimaxFrequencyMode.AutoMotion,ClimaxFrequencyMode.Fixed,ClimaxFrequencyMode.Simulated,ClimaxFrequencyMode.LiveL0};
            for(int i=0;i<modes.Length;i++)if(B(new Rect(area.x+4,area.y+4+i*30,area.width-8,27),(options.FrequencyMode==modes[i]?"● ":"")+ClimaxModeLabel(modes[i])))
            {
                SetChapterFrequency(chapter,modes[i],options.Hertz,false);chapterFrequencyPicker=false;ClearUiTextFocus();
                if(e.type!=EventType.Used)e.Use();return;
            }
        }

        private void SetSelectedChapterEffect(bool climax)
        {
            var chapter=effectPopupChapter??EffectEditorChapter;
            if(chapter==null||!Book.Chapters.Contains(chapter))return;
            RememberChapters();
            chapter.Phase=climax?ChapterPhase.Climax:ChapterPhase.Normal;
            chapter.ClimaxDetected=false;
            chapter.ClimaxEvidence="";
            chapter.Climax=ChapterEffectOptions(chapter);
            chapter.EffectsEdited=true;
            chapterEffectPicker=false;
            ClearUiTextFocus();
            MarkSceneDataDirty();
            RefreshActiveClimaxPulse();
            status=climax?L("本章已设为高潮；L0 输出立即使用章节效果。","Chapter set to Climax; L0 output now uses the chapter effect."):
                L("本章已设为普通；可叠加 6D 研磨与骨架联动。","Chapter set to Normal; 6D grinding & skeleton coupling available.");
        }

        private void DrawChapterEffectPicker()
        {
            var chapter=effectPopupChapter;
            if(chapter==null||!Book.Chapters.Contains(chapter)){chapterEffectPicker=false;return;}
            var area=chapterEffectPickerArea;
            Popup(area);
            var e=Event.current;
            if(e.type==EventType.MouseDown&&!area.Contains(e.mousePosition)||e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape)
            {chapterEffectPicker=false;ClearUiTextFocus();e.Use();return;}
            bool climax=chapter.IsClimax;
            if(B(new Rect(area.x+4,area.y+4,area.width-8,27),(climax?"":"● ")+L("普通章节","Normal chapter")))
            {SetSelectedChapterEffect(false);if(e.type!=EventType.Used)e.Use();return;}
            if(B(new Rect(area.x+4,area.y+35,area.width-8,27),(climax?"● ":"")+L("高潮 / 射精","Climax / ejaculation")))
            {SetSelectedChapterEffect(true);if(e.type!=EventType.Used)e.Use();return;}
        }

        private void DrawClimaxRangeSlider(float x,float y,float width,float oldMinimum,float oldMaximum,out float minimum,out float maximum)
        {
            GUI.Label(new Rect(x,y,150,26),L("L0 输出范围","L0 output range"),label);
            var track=new Rect(x+6,y+35,width-12,8);
            var hit=new Rect(track.x-6,y+22,track.width+12,30);
            int id=GUIUtility.GetControlID(0x4b534c30,FocusType.Passive,hit);
            minimum=oldMinimum;maximum=oldMaximum;
            float minX=track.x+track.width*minimum/999f,maxX=track.x+track.width*maximum/999f;
            Box(track,new Color(.19f,.16f,.24f));
            Box(new Rect(minX,track.y,Mathf.Max(2,maxX-minX),track.height),new Color(.76f,.44f,.56f));
            Box(new Rect(minX-5,track.y-7,10,22),new Color(.90f,.60f,.71f));
            Box(new Rect(maxX-5,track.y-7,10,22),new Color(.91f,.76f,.57f));
            var e=Event.current;
            if(GUI.enabled&&e.type==EventType.MouseDown&&e.button==0&&hit.Contains(e.mousePosition))
            {
                float distanceToMinimum=Mathf.Abs(e.mousePosition.x-minX),distanceToMaximum=Mathf.Abs(e.mousePosition.x-maxX);
                climaxRangeDrag=distanceToMinimum==distanceToMaximum?(e.mousePosition.x<=(minX+maxX)*.5f?0:1):(distanceToMinimum<distanceToMaximum?0:1);
                GUIUtility.hotControl=id;e.Use();
            }
            if(GUIUtility.hotControl==id&&climaxRangeDrag>=0&&(e.type==EventType.MouseDrag||e.type==EventType.MouseDown))
            {
                float value=Mathf.Clamp(Mathf.Round((e.mousePosition.x-track.x)/track.width*999f),0,999);
                if(climaxRangeDrag==0)minimum=Mathf.Min(value,maximum);else maximum=Mathf.Max(value,minimum);
                e.Use();
            }
            if(GUIUtility.hotControl==id&&e.rawType==EventType.MouseUp)
            {
                float value=Mathf.Clamp(Mathf.Round((e.mousePosition.x-track.x)/track.width*999f),0,999);
                if(climaxRangeDrag==0)minimum=Mathf.Min(value,maximum);else maximum=Mathf.Max(value,minimum);
                climaxRangeDrag=-1;GUIUtility.hotControl=0;e.Use();
            }
            GUI.Label(new Rect(x+width-168,y,168,26),Mathf.Round(minimum).ToString("0",CultureInfo.InvariantCulture)+" – "+Mathf.Round(maximum).ToString("0",CultureInfo.InvariantCulture)+" / 999",small);
        }

    }
}

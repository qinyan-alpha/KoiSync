using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private Chapter selectedChapter;
        private string chapterName="",chapterStart="0",chapterEnd="0";
        private int chapterSource,chapterTarget,chapterOpening,chapterPicker;
        private Rect chapterPickerArea;
        private Vector2 chapterScroll,pickerScroll;
        private double viewStart,viewSpan;
        private readonly Stack<string> chapterUndo=new Stack<string>(),chapterRedo=new Stack<string>();
        private bool chapterBoundaryDrag;

        private void ResetChapterEditor()
        {SelectChapter(null);chapterUndo.Clear();chapterRedo.Clear();viewStart=viewSpan=0;chapterBoundaryDrag=false;seekDirty=false;chapterMenu=renamingChapter=false;}
        private void RememberChapters(){chapterUndo.Push(Book.ToXml());chapterRedo.Clear();}
        private void History(bool redo)
        {
            var from=redo?chapterRedo:chapterUndo;var to=redo?chapterUndo:chapterRedo;
            if(from.Count==0)return;double at=selectedChapter==null?Clock.Time:selectedChapter.Start;
            to.Push(Book.ToXml());Book.FromXml(from.Pop());SelectChapter(Book.At(at));appliedChapter="";Devices.Stop();MarkSceneDataDirty();
        }
        private void SelectChapter(Chapter chapter)
        {
            chapterEffectPicker=chapterFrequencyPicker=chapterGrindPicker=false;effectPopupChapter=frequencyUndoChapter=null;
            selectedChapter=chapter;chapterPicker=0;
            if(chapter==null){chapterName="";chapterStart=chapterEnd="0";chapterSource=chapterTarget=-1;chapterOpening=0;return;}
            int index=Book.Chapters.IndexOf(chapter);
            chapterName=ChapterBaseTitle(chapter);chapterStart=chapter.Start.ToString("F3",CultureInfo.InvariantCulture);
            chapterEnd=(index+1<Book.Chapters.Count?Book.Chapters[index+1].Start:Clock.Duration).ToString("F3",CultureInfo.InvariantCulture);
            chapterSource=actors.FindIndex(a=>Key(a)==chapter.Source);chapterTarget=actors.FindIndex(a=>Key(a)==chapter.Target);chapterOpening=chapter.Opening;
        }
        private void SelectAndLocateChapter(Chapter chapter)
        {
            SelectChapter(chapter);if(chapter==null||!Clock.Available)return;
            TimelineAction(()=>Clock.Seek(chapter.Start));
            manualOpening=false;followChapters=true;previousChapterTime=-1;appliedChapter="";
            forceSelectedChapterBinding=true;
            try{UpdateChapterBinding();RefreshActiveClimaxPulse();}
            catch(Exception error){Devices.Stop();status=error.GetBaseException().Message;}
            finally{forceSelectedChapterBinding=false;}
        }
        private void EditSelectedChapter()
        {
            if(selectedChapter==null)return;
            try
            {
                double start,end;
                if(!double.TryParse(chapterStart,NumberStyles.Float,CultureInfo.InvariantCulture,out start)||!double.TryParse(chapterEnd,NumberStyles.Float,CultureInfo.InvariantCulture,out end))
                    throw new ArgumentException(L("起止时间请输入秒数","Enter start and end in seconds"));
                if(chapterSource>=0&&chapterSource==chapterTarget)throw new ArgumentException(L("来源与目标不能是同一角色","Source and target must be different actors"));
                if(Book.Chapters.IndexOf(selectedChapter)==Book.Chapters.Count-1&&Math.Abs(end-Clock.Duration)<.001)end=Clock.Duration;
                string before=Book.ToXml();Book.SetBounds(selectedChapter,start,end,Clock.Duration);chapterUndo.Push(before);chapterRedo.Clear();
                string oldIdentity=selectedChapter.Identity;int oldOpening=selectedChapter.Opening;
                selectedChapter.CustomName=chapterName.Trim();
                selectedChapter.Source=chapterSource>=0&&chapterSource<actors.Count?Key(actors[chapterSource]):-1;
                selectedChapter.Target=chapterTarget>=0&&chapterTarget<actors.Count?Key(actors[chapterTarget]):-1;
                selectedChapter.SourceName=StudioBinding.Name(Actor(selectedChapter.Source));selectedChapter.TargetName=StudioBinding.Name(Actor(selectedChapter.Target));
                selectedChapter.Opening=chapterOpening;selectedChapter.Manual=true;
                selectedChapter.NeedsReview=false;selectedChapter.ReviewNote="";
                if(selectedChapter.Identity!=oldIdentity){selectedChapter.RotationReference=null;selectedChapter.StrokeCalibration=null;}
                if(oldOpening!=selectedChapter.Opening&&(oldOpening>=6||selectedChapter.Opening>=6))selectedChapter.Calibration=null;
                manualOpening=false;followChapters=true;appliedChapter="";Devices.Stop();
                SelectChapter(selectedChapter);MarkSceneDataDirty();status=L("章节修改已保存到场景数据目录","Chapter edits saved to the scene data folder");
            }
            catch(Exception error){status=LocalError(error.Message);}
        }
        private static bool TryParseTime(string text,out double seconds)
        {
            // Accept plain seconds or the mm:ss.ss form shown by the timeline.
            seconds=0;text=(text??"").Trim();int colon=text.LastIndexOf(':');double minutes=0,rest;
            if(colon>=0&&!double.TryParse(text.Substring(0,colon),NumberStyles.Float,CultureInfo.InvariantCulture,out minutes))return false;
            if(!double.TryParse(text.Substring(colon+1),NumberStyles.Float,CultureInfo.InvariantCulture,out rest))return false;
            seconds=minutes*60+rest;return !double.IsNaN(seconds)&&!double.IsInfinity(seconds);
        }
        private void SeekToText()
        {
            // Parse before TimelineAction: an invalid entry must not end an
            // armed recording or reset the live cycle.
            double target;
            if(!TryParseTime(seekText,out target)){status=L("定位时间请输入秒数或 分:秒","Enter the seek time as seconds or mm:ss");return;}
            TimelineAction(()=>Clock.Seek(target));
        }
        private void DrawTimelineTransport()
        {
            GUI.Label(new Rect(20,52,200,26),TimeText(Clock.Time)+" / "+TimeText(Clock.Duration),header);
            GUI.Label(new Rect(214,55,130,22),Clock.Playing?L("  播放","  Playing"):L("  暂停","  Paused"),small);
            GUI.enabled=Clock.Available;
            if(B(new Rect(352,50,85,28),Clock.Playing?L("❚❚ 暂停","❚❚ Pause"):L("▶ 播放","▶ Play"),primaryButton))TimelineAction(Clock.Toggle,true);
            if(B(new Rect(445,50,105,28),L("■ 停止 / 起点","■ Stop / Start"))){seekDirty=false;TimelineAction(Clock.Stop);}
            if(B(new Rect(558,50,64,28),"−1 s"))TimelineAction(()=>Clock.Seek(Clock.Time-1));
            if(B(new Rect(630,50,64,28),"+1 s"))TimelineAction(()=>Clock.Seek(Clock.Time+1));
            seekText=Text(new Rect(710,50,90,28),seekText,16);
            if(B(new Rect(808,50,82,28),L("定位","Seek")))SeekToText();
            if(B(new Rect(898,50,96,28),L("显示全长","Fit all"))){viewStart=0;viewSpan=Clock.Duration;}
            DrawChapterStrip(new Rect(20,88,974,126));
            GUI.enabled=true;
        }
        private void DrawChapterEditor()
        {
            // Toolbar: manual editing on the left, automatic analysis on the right.
            Box(new Rect(20,0,974,40),CardFill);Divider(20,39,974);
            GUI.enabled=Clock.Available;
            if(B(new Rect(28,5,128,30),L("播放头处分章","Split at playhead"))){RememberChapters();SplitChapter();SelectChapter(Book.At(Clock.Time));}
            GUI.enabled=selectedChapter!=null&&Book.Chapters.IndexOf(selectedChapter)>0;
            if(B(new Rect(162,5,128,30),L("合并到上一章","Merge previous"))){DeleteSelectedChapter();}
            GUI.enabled=chapterUndo.Count>0;if(B(new Rect(296,5,70,30),L("撤销","Undo")))History(false);
            GUI.enabled=chapterRedo.Count>0;if(B(new Rect(372,5,70,30),L("重做","Redo")))History(true);
            GUI.enabled=true;
            Box(new Rect(452,6,1,28),Edge);
            bool analysis=GUI.Toggle(new Rect(462,9,150,24),autoCameraPairs,L("自动分析 / 分章","Analysis / chapters"),toggle);
            if(analysis!=autoCameraPairs)SetAutomaticGrouping(analysis);
            bool follow=GUI.Toggle(new Rect(614,9,112,24),followChapters,L("跟随章节","Follow chapters"),toggle);
            if(follow!=followChapters){followChapters=follow;if(follow)manualOpening=false;appliedChapter="";Devices.Stop();}
            GUI.enabled=Clock.Available&&autoCameraPairs;
            if(BTip(new Rect(730,5,128,30),L("重建自动章节","Rebuild auto chapters"),L("保留手动编辑，读取吸附事件；连续播放时重新确认互动章节。","Keeps manual edits, reads attachment events and confirms interaction chapters during playback.")))RebuildAutomaticChapters();
            if(BTip(new Rect(864,5,124,30),L("重新识别本章","Re-detect chapter"),GroupingStatus()))RecheckAutomaticGrouping();
            GUI.enabled=true;
            DrawChapterRows(new Rect(20,50,580,398));DrawChapterDetails(new Rect(612,50,382,398));
        }
        private void DrawChapterStrip(Rect area)
        {
            double duration=Math.Max(.01,Clock.Duration);
            if(viewSpan<=0)viewSpan=duration;viewSpan=Math.Max(Math.Min(1,duration),Math.Min(duration,viewSpan));viewStart=Math.Max(0,Math.Min(duration-viewSpan,viewStart));
            Box(area,new Color(.12f,.105f,.152f));var e=Event.current;
            if(area.Contains(e.mousePosition)&&e.type==EventType.ScrollWheel&&e.control)
            {double anchor=viewStart+(e.mousePosition.x-area.x)/area.width*viewSpan;viewSpan*=Math.Pow(1.2,e.delta.y);viewSpan=Math.Max(Math.Min(1,duration),Math.Min(duration,viewSpan));viewStart=anchor-(e.mousePosition.x-area.x)/area.width*viewSpan;e.Use();}
            for(int i=0;i<=10;i++)
            {float x=area.x+i*area.width/10;Box(new Rect(x,area.y+23,1,area.height-23),new Color(.24f,.205f,.295f));GUI.Label(new Rect(x+2,area.y,94,22),TimeText(viewStart+viewSpan*i/10),small);}
            for(int i=0;i<Book.Chapters.Count;i++)
            {
                var c=Book.Chapters[i];double end=i+1<Book.Chapters.Count?Book.Chapters[i+1].Start:duration;
                if(end<viewStart||c.Start>viewStart+viewSpan)continue;
                float x=area.x+(float)((Math.Max(c.Start,viewStart)-viewStart)/viewSpan)*area.width;
                float right=area.x+(float)((Math.Min(end,viewStart+viewSpan)-viewStart)/viewSpan)*area.width;
                var block=new Rect(x,area.y+30,Math.Max(1,right-x-2),58);
                Box(block,c==selectedChapter?new Color(.48f,.25f,.35f):c.Source<0?new Color(.24f,.19f,.25f):c.IsClimax?new Color(.46f,.20f,.28f):c.NeedsReview?new Color(.44f,.32f,.20f):new Color(.23f,.195f,.295f));
                if(block.width>30)GUI.Label(new Rect(block.x+5,block.y+6,block.width-10,45),(i+1)+(c.NeedsReview?" [?] ":"  ")+ChapterTitle(c),small);
                if(e.type==EventType.MouseDown&&e.button==1&&block.Contains(e.mousePosition)){OpenChapterMenu(c);e.Use();}
                if(e.type==EventType.MouseDown&&e.button==0&&block.Contains(e.mousePosition))
                {
                    bool boundary=i>0&&c.Start>=viewStart&&Math.Abs(e.mousePosition.x-x)<8;
                    if(boundary){SelectChapter(c);RememberChapters();chapterBoundaryDrag=true;Devices.Stop();}
                    else SelectAndLocateChapter(c);
                    e.Use();
                }
            }
            if(chapterBoundaryDrag&&selectedChapter!=null&&(e.type==EventType.MouseDrag||e.rawType==EventType.MouseUp))
            {
                int index=Book.Chapters.IndexOf(selectedChapter);
                double high=index+1<Book.Chapters.Count?Book.Chapters[index+1].Start:duration;
                double target=viewStart+(e.mousePosition.x-area.x)/area.width*viewSpan;
                selectedChapter.Start=Math.Max(Book.Chapters[index-1].Start+.002,Math.Min(high-.002,target));selectedChapter.Manual=true;
                chapterStart=selectedChapter.Start.ToString("F3",CultureInfo.InvariantCulture);appliedChapter="";
                if(e.rawType==EventType.MouseUp){chapterBoundaryDrag=false;SelectChapter(selectedChapter);MarkSceneDataDirty();}e.Use();
            }
            if(e.type==EventType.MouseDown&&area.Contains(e.mousePosition)&&(e.button==2||e.button==0&&(e.mousePosition.y<area.y+25||Book.Chapters.Count==0)))
            {seekDirty=true;pendingSeek=Math.Max(0,Math.Min(duration,viewStart+(e.mousePosition.x-area.x)/area.width*viewSpan));nextSeekPreview=0;e.Use();}
            if(seekDirty)
            {
                bool released=e.rawType==EventType.MouseUp;
                if(e.type==EventType.MouseDrag||released)pendingSeek=Math.Max(0,Math.Min(duration,viewStart+(e.mousePosition.x-area.x)/area.width*viewSpan));
                if(released||e.type==EventType.Repaint&&Time.unscaledTime>=nextSeekPreview)
                {TimelineAction(()=>Clock.Seek(pendingSeek));nextSeekPreview=Time.unscaledTime+.1f;if(released)seekDirty=false;}
                if(e.type==EventType.MouseDrag||released)e.Use();
            }
            float playhead=area.x+(float)((Clock.Time-viewStart)/viewSpan)*area.width;
            if(playhead>=area.x&&playhead<=area.xMax)Box(new Rect(playhead,area.y+24,2,area.height-24),new Color(.98f,.76f,.42f));
            float pan=GUI.HorizontalSlider(new Rect(area.x,area.yMax-19,area.width,17),(float)viewStart,0,(float)Math.Max(.001,duration-viewSpan));
            if(Math.Abs(pan-viewStart)>.001)viewStart=pan;
        }
        private string ChapterTitle(Chapter c)
        {return (c.IsClimax?L("高潮/射精 · ","Climax / ejaculation · "):"")+ChapterBaseTitle(c);}
        private string ChapterBaseTitle(Chapter c)
        {
            if(!string.IsNullOrEmpty(c.CustomName))return c.CustomName;
            if(!c.Manual&&c.Source>=0&&c.Target>=0)
                return StudioBinding.Name(Actor(c.Source))+" → "+StudioBinding.Name(Actor(c.Target))+" · "+OpeningNames[c.Opening];
            return string.IsNullOrEmpty(c.Name)?L("未命名章节","Untitled chapter"):UiText.ChapterName(c.Name);
        }
        private string ChapterPairTitle(Chapter c)
        {
            if(c.Source<0||c.Target<0)return L("本章不计算","Disable chapter");
            string source=string.IsNullOrEmpty(c.SourceName)?StudioBinding.Name(Actor(c.Source)):c.SourceName;
            string target=string.IsNullOrEmpty(c.TargetName)?StudioBinding.Name(Actor(c.Target)):c.TargetName;
            return source+" → "+target;
        }
        private void DrawChapterRows(Rect card)
        {
            float top=Card(card,L("章节列表 · 单击选择并定位","Chapters · Click to select and seek"));
            GUI.enabled=Clock.Available&&!scenePending;
            string scanCaption=StepBakeRunning?(L("取消预扫描 ","Cancel prescan ")+Math.Round(StepBakeProgress*100)+"%"):L("一键高速预扫描","Auto prescan");
            if(BTip(new Rect(card.xMax-184,card.y+5,172,24),scanCaption,L("离线零 AI 高速步进扫描真实骨骼：整章投票防阴道/肛门乱选、自动标定行程与高潮候选，并生成 .director.json。","Offline zero-AI fast prescan using real bones: chapter voting prevents vaginal/anal flips, calibrates chapter ranges & climax candidates, and writes .director.json."),StepBakeRunning?dangerButton:primaryButton))
            {
                if(StepBakeRunning)CancelStepBake();
                else try{StartStepBake(0.05);}catch(Exception err){status=err.GetBaseException().Message;}
            }
            GUI.enabled=true;
            var area=new Rect(card.x+8,top,card.width-16,card.yMax-top-34);Box(area,new Color(.102f,.090f,.128f));
            chapterScroll=GUI.BeginScrollView(area,chapterScroll,new Rect(0,0,area.width-22,Math.Max(area.height-4,Book.Chapters.Count*34)));
            int first=Math.Max(0,(int)(chapterScroll.y/34));int last=Math.Min(Book.Chapters.Count,first+(int)(area.height/34)+2);
            var playing=Clock.Available?Book.At(Clock.Time):null;
            for(int i=first;i<last;i++)
            {
                var c=Book.Chapters[i];double end=i+1<Book.Chapters.Count?Book.Chapters[i+1].Start:Clock.Duration;
                var row=new Rect(0,i*34,area.width-22,32);
                Box(row,c==selectedChapter?new Color(.34f,.195f,.275f):i%2==0?new Color(.138f,.120f,.172f):new Color(.122f,.106f,.154f));
                // Orange: chapter at the playhead. Amber: needs review. Rose: climax chapter.
                if(c==playing)Box(new Rect(row.x,row.y,3,row.height),Warm);
                if(c.NeedsReview)Box(new Rect(row.xMax-8,row.y,4,row.height),new Color(.95f,.72f,.22f));
                if(c.IsClimax)Box(new Rect(row.xMax-4,row.y,4,row.height),new Color(.86f,.38f,.46f));
                GUI.Label(new Rect(10,i*34+5,36,24),(i+1).ToString("00"),small);
                GUI.Label(new Rect(46,i*34+5,170,24),TimeText(c.Start)+" – "+TimeText(end),small);
                string rowTitle=(c.NeedsReview?"[?] ":"")+ChapterTitle(c);
                GUI.Label(new Rect(218,i*34+5,row.width-230,24),rowTitle,c.Source<0?small:label);
                if(row.Contains(Event.current.mousePosition))hoverTip=rowTitle+"\n"+ChapterPairTitle(c)+(string.IsNullOrEmpty(c.ReviewNote)?"":"\n"+c.ReviewNote);
                if(Event.current.type==EventType.MouseDown&&row.Contains(Event.current.mousePosition))
                {
                    if(Event.current.button==1)OpenChapterMenu(c);
                    else if(Event.current.button==0)SelectAndLocateChapter(c);
                    Event.current.Use();
                }
            }
            GUI.EndScrollView();
            GUI.Label(new Rect(card.x+12,card.yMax-30,card.width-24,24),StepBakeRunning||!string.IsNullOrEmpty(StepBakeStatus)?StepBakeStatus:(L("自动分析：","Analysis: ")+GroupingStatus()),small);
        }
        private void CreateChapterFromCurrentBinding()
        {
            var existing=Book.At(Clock.Time);
            if(existing!=null){SelectChapter(existing);return;}
            if(Binding==null||!Binding.Alive){ReadActors();BindPair();SelectChapter(Book.At(Clock.Time));return;}
            RememberChapters();
            var chapter=MakeChapter(Binding.Source,Binding.Target,Binding.Kind);
            chapter.Manual=!autoCameraPairs;
            chapter.Calibration=CurrentCalibration();
            Book.Observe(Book.Chapters.Count==0?0:Clock.Time,chapter);
            SelectChapter(Book.At(Clock.Time));MarkSceneDataDirty();
            status=L("已使用当前绑定创建章节","Chapter created from current binding");
        }
        private void OpenChapterPicker(int kind,Rect anchor)
        {
            ClosePopups();ClearUiTextFocus();chapterPicker=kind;pickerScroll=Vector2.zero;
            chapterPickerArea=new Rect(anchor.x,ContentTop+anchor.yMax+2,Mathf.Max(anchor.width,250),220);
        }
        private void DrawChapterDetails(Rect card)
        {
            float top=Card(card,L("所选章节","Selected chapter"));
            float x=card.x+12,w=card.width-24,half=(w-8)/2;
            GUI.enabled=true;
            if(selectedChapter==null&&B(new Rect(card.xMax-194,card.y+5,182,24),L("读取角色 / 新建章节","Read actors / new chapter"),primaryButton))
            {CreateChapterFromCurrentBinding();}
            // The effect popup sits above the name and time fields. Keep the
            // covered controls out of IMGUI's input path while it is open so
            // its click cannot focus a field behind the popup.
            GUI.enabled=selectedChapter!=null&&chapterPicker==0&&!chapterEffectPicker;
            bool liveBinding=selectedChapter==null&&Binding!=null&&Binding.Alive;
            if(liveBinding)GUI.Label(new Rect(x,top,w,28),L("当前已绑定；点击新建章节开始编辑","Bound; create a chapter to edit"),label);
            else chapterName=Text(new Rect(x,top,w,28),chapterName,100);
            GUI.Label(new Rect(x,top+37,58,22),L("起始秒","Start (s)"),small);chapterStart=Text(new Rect(x+58,top+34,half-58,26),chapterStart,20);
            GUI.Label(new Rect(x+half+8,top+37,58,22),L("结束秒","End (s)"),small);chapterEnd=Text(new Rect(x+half+66,top+34,half-58,26),chapterEnd,20);
            var sourceRect=new Rect(x,top+68,w,26);var targetRect=new Rect(x,top+98,w,26);var openingRect=new Rect(x,top+128,half,26);
            if(B(sourceRect,L("来源：","Source: ")+ActorLabel(liveBinding?actors.IndexOf(Binding.Source):chapterSource)+" ▾"))OpenChapterPicker(1,sourceRect);
            if(B(targetRect,L("目标：","Target: ")+ActorLabel(liveBinding?actors.IndexOf(Binding.Target):chapterTarget)+" ▾"))OpenChapterPicker(2,targetRect);
            if(B(openingRect,OpeningNames[liveBinding?(int)Binding.Kind:chapterOpening]+" ▾"))OpenChapterPicker(3,openingRect);
            if(B(new Rect(x+half+8,top+128,half,26),L("本章不计算","Disable chapter"))){chapterSource=chapterTarget=-1;}
            if(B(new Rect(x,top+162,half,30),L("定位章节起点","Seek chapter start")))TimelineAction(()=>Clock.Seek(selectedChapter.Start));
            if(B(new Rect(x+half+8,top+162,half,30),L("应用章节修改","Apply chapter edits"),primaryButton))EditSelectedChapter();
            GUI.enabled=true;
            Divider(x,top+204,w);
            var calibration=selectedChapter==null?null:selectedChapter.Calibration;
            GUI.Label(new Rect(x,top+212,w,40),calibration==null?L("本章校准：尚未记录","Chapter calibration: not captured"):
                L("本章校准：长度 ","Chapter calibration: length ")+(calibration.Length*1000).ToString("F1")+" mm · L1 ±"+(calibration.L1*1000).ToString("F1")+" · L2 ±"+(calibration.L2*1000).ToString("F1")+" mm · "+L("起点 ","origin ")+(calibration.Origin.X*1000).ToString("F1")+" / "+(calibration.Origin.Y*1000).ToString("F1")+" / "+(calibration.Origin.Z*1000).ToString("F1")+" mm",wrapSmall);
            GUI.enabled=selectedChapter!=null&&Binding!=null;
            if(B(new Rect(x,top+256,w,28),L("将当前校准写入所选章节","Copy current calibration to chapter")))SaveCalibrationToSelectedChapter();
            GUI.enabled=true;
            GUI.Label(new Rect(x,top+290,w,40),L("调整长度、起点和范围会自动保存到播放头所在章节。","Length, origin and range edits save to the chapter at the playhead."),wrapSmall);
        }
        private void DrawChapterPicker()
        {
            var area=chapterPickerArea;Popup(area);
            if(Event.current.type==EventType.MouseDown&&!area.Contains(Event.current.mousePosition)||Event.current.type==EventType.KeyDown&&Event.current.keyCode==KeyCode.Escape)
            {chapterPicker=0;Event.current.Use();return;}
            int count=chapterPicker==3?OpeningNames.Length:actors.Count;float inner=area.width-24;
            pickerScroll=GUI.BeginScrollView(area,pickerScroll,new Rect(0,0,inner,Math.Max(area.height-4,(count+1)*30+8)));
            if(B(new Rect(4,4,inner-8,26),L("关闭列表","Close list")))chapterPicker=0;
            for(int i=0;i<count&&chapterPicker!=0;i++)if(B(new Rect(4,34+i*30,inner-8,26),chapterPicker==3?OpeningNames[i]:ActorLabel(i)))
            {if(chapterPicker==3)chapterOpening=i;else SelectChapterActor(i,chapterPicker==1);chapterPicker=0;}
            GUI.EndScrollView();
        }
    }
}

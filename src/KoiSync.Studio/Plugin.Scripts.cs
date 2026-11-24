using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using KoiSync.Core;
using KoiSync.Plugin;
using UnityEngine;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private enum MotionSource { Auto,Live,Script }
        private MotionSource motionSource;
        private readonly FunscriptPlayback scripts=new FunscriptPlayback();
        private readonly StudioRecorder recorder=new StudioRecorder();
        private string scenePath="",scriptStem="",recordedPath="";
        private double recordedStart,recordedEnd;
        private double recordingClock=-1,recordingWall;
        private bool recording,dataDirty,scenePending;
        private float dataSaveAt,sceneReadyAt;
        private int sceneReadyAttempts,sceneReadyFrame;
        private string scriptsStatus="";
        private readonly bool[] scriptEnabled=new bool[6];
        private bool UseScripts{get{return !recording&&motionSource!=MotionSource.Live&&scripts.Scripts!=null;}}
        private void MarkSceneDataDirty(){dataDirty=true;dataSaveAt=Time.realtimeSinceStartup+.7f;}
        private void SetScenePath(string path)
        {
            if(string.IsNullOrEmpty(path)||!string.Equals(Path.GetExtension(path),".png",StringComparison.OrdinalIgnoreCase))return;
            scenePath=Path.GetFullPath(path);scriptStem=SceneScriptPath.BasePath(BepInEx.Paths.GameRootPath,scenePath);
        }
        private void BeforeScriptSceneChange(string next)
        {
            ResetDirectorForSceneChange();
            if(recording)FinishRecording();SaveSceneData();
            scripts.Clear();motionSource=MotionSource.Auto;recordedPath="";recordedStart=recordedEnd=0;
            scenePath=scriptStem="";SetScenePath(next);scenePending=false;dataDirty=false;autoCameraPairs=false;ResetPairCandidates();ResetClimax();
        }
        private void QueueSceneReady(string path)
        {
            // Timeline applies its saved tracks after a 20-frame load delay.
            // A wall-clock delay alone can finish earlier on a heavy scene.
            SetScenePath(path);scenePending=true;sceneReadyAt=Time.realtimeSinceStartup+1;sceneReadyFrame=Time.frameCount+32;sceneReadyAttempts=0;
        }
        private void UpdateSceneFiles()
        {
            if(scenePending&&Time.frameCount>=sceneReadyFrame&&Time.realtimeSinceStartup>=sceneReadyAt)
            {
                sceneReadyAt=Time.realtimeSinceStartup+.5f;sceneReadyAttempts++;
                if(global::Studio.Studio.Instance!=null&&global::Studio.Studio.Instance.dicObjectCtrl!=null&&global::Studio.Studio.Instance.dicObjectCtrl.Count>0)
                {
                    ReadActors();
                    if(actors.Count>=2)
                    {
                        // OnGUI can draw the previous scene's duration while
                        // Timeline restores its tracks. Fit the new scene only
                        // after that restore, not just in BeforeSceneChange.
                        scenePending=false;viewStart=viewSpan=0;LoadSceneData();RestoreChapterActors();followChapters=true;manualOpening=false;appliedChapter="";
                        if(autoCameraPairs)CachePairCandidates();
                        TryLoadSceneScripts();UpdateChapterBinding();
                        if(autoCameraPairs&&Binding==null&&Book.Chapters.Count==0&&actors.Count==2)BindPair();
                        OnDirectorSceneReady();
                        scriptsStatus=L("场景数据已就绪","Scene data ready");
                    }
                }
                if(sceneReadyAttempts>=30)scenePending=false;
            }
            if(dataDirty&&!scenePending&&Time.realtimeSinceStartup>=dataSaveAt)SaveSceneData();
        }
        private void SaveSceneData()
        {
            if(!dataDirty||string.IsNullOrEmpty(scriptStem))return;
            try
            {
                var text=new StringBuilder();
                using(var xml=XmlWriter.Create(text,new XmlWriterSettings{OmitXmlDeclaration=true}))
                {
                    xml.WriteStartElement("koisync");xml.WriteAttributeString("version","2");xml.WriteAttributeString("scene",scenePath);
                    xml.WriteAttributeString("source",motionSource.ToString());xml.WriteAttributeString("script",scripts.Path??"");
                    xml.WriteAttributeString("analysis",autoCameraPairs?"true":"false");
                    xml.WriteAttributeString("recordedPath",recordedPath);xml.WriteAttributeString("recordStart",recordedStart.ToString("R",CultureInfo.InvariantCulture));
                    xml.WriteAttributeString("recordEnd",recordedEnd.ToString("R",CultureInfo.InvariantCulture));
                    xml.WriteRaw(Book.ToXml());xml.WriteRaw(modelOrigins.ToXml());xml.WriteEndElement();
                }
                Directory.CreateDirectory(Path.GetDirectoryName(scriptStem));string path=scriptStem+".koisync.xml";
                File.WriteAllText(path+".tmp",text.ToString(),new UTF8Encoding(false));
                if(File.Exists(path))File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);
                dataDirty=false;
            }
            catch(Exception error){scriptsStatus=L("场景数据保存失败：","Scene data save failed: ")+error.GetBaseException().Message;dataSaveAt=Time.realtimeSinceStartup+5;}
        }
        private void LoadSceneData()
        {
            if(string.IsNullOrEmpty(scriptStem)||!File.Exists(scriptStem+".koisync.xml"))return;
            try
            {
                var doc=new XmlDocument();using(var r=XmlReader.Create(scriptStem+".koisync.xml",new XmlReaderSettings{ProhibitDtd=true,XmlResolver=null}))doc.Load(r);
                var root=doc.DocumentElement;var book=new ChapterBook();var chapters=root.SelectSingleNode("chapters");if(chapters!=null)book.FromXml(chapters.OuterXml);
                var origins=new ModelOrigins();var offsets=root.SelectSingleNode("origins");if(offsets!=null)origins.FromXml(offsets.OuterXml);
                Book.FromXml(book.ToXml());modelOrigins.FromXml(origins.ToXml());
                string source=root.GetAttribute("source");motionSource=source=="Live"?MotionSource.Live:source=="Script"?MotionSource.Script:MotionSource.Auto;
                // Old camera=true was a default, not an explicit opt-in to
                // analysis. Only the new per-scene setting can enable it.
                autoCameraPairs=root.GetAttribute("analysis")=="true";recordedPath=root.GetAttribute("recordedPath");
                double.TryParse(root.GetAttribute("recordStart"),NumberStyles.Float,CultureInfo.InvariantCulture,out recordedStart);
                double.TryParse(root.GetAttribute("recordEnd"),NumberStyles.Float,CultureInfo.InvariantCulture,out recordedEnd);
                string selected=root.GetAttribute("script");if(File.Exists(selected))scripts.Load(selected);
            }
            catch(Exception error){scriptsStatus=L("场景数据读取失败：","Scene data load failed: ")+error.GetBaseException().Message;}
        }
        private void SaveAllChapterData()
        {
            if(scenePending||string.IsNullOrEmpty(scriptStem))
            {scriptsStatus=L("先加载或保存场景卡，以确定保存路径","Load or save a scene card to establish the save path");return;}
            try
            {
                CaptureChapterCalibration();MarkSceneDataDirty();SaveSceneData();
                if(!dataDirty)scriptsStatus=L("已保存全部章节信息 · ","All chapter data saved · ")+Book.Chapters.Count+L(" 章"," chapters");
            }
            catch(Exception error){scriptsStatus=L("场景数据保存失败：","Scene data save failed: ")+error.GetBaseException().Message;}
        }
        private void TryLoadSceneScripts()
        {
            if(scripts.Scripts!=null||string.IsNullOrEmpty(scriptStem))return;
            string path=File.Exists(scriptStem+".funscript")?scriptStem+".funscript":scriptStem+".L0.funscript";
            if(File.Exists(path))try{scripts.Load(path);}catch(Exception error){scriptsStatus=error.GetBaseException().Message;}
        }
        private void ChangeMotionSource(int source)
        {
            if(recording)FinishRecording();motionSource=(MotionSource)source;TryLoadSceneScripts();
            Drive.Reset();Devices.Stop();MarkSceneDataDirty();
        }
        private void SelectScript(string path)
        {
            if(string.IsNullOrEmpty(path))return;
            if(recording)FinishRecording();scripts.Load(path);motionSource=MotionSource.Script;
            Drive.Reset();Devices.Stop();MarkSceneDataDirty();scriptsStatus=L("已加载六轴配套文件：","Loaded companion axes: ")+scripts.Scripts.Count+" / 6";
        }
        private void BrowseScript()
        {
            try
            {
                Devices.Stop();if(Clock.Playing)Clock.Toggle();
                string directory=string.IsNullOrEmpty(scriptStem)?Path.Combine(BepInEx.Paths.GameRootPath,"UserData/KoiSyncStudio"):Path.GetDirectoryName(scriptStem);
                Directory.CreateDirectory(directory);SelectScript(FunscriptFileDialog.Select(directory));
            }
            catch(Exception error){scriptsStatus=error.GetBaseException().Message;}
        }
        private void OpenScriptFolder()
        {
            try
            {
                // Chapter data and newly recorded scripts belong to this scene,
                // even when an external script is selected for playback.
                string folder=string.IsNullOrEmpty(scriptStem)?Path.Combine(BepInEx.Paths.GameRootPath,"UserData/KoiSyncStudio"):Path.GetDirectoryName(scriptStem);
                Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start("explorer.exe","\""+folder+"\"");
            }
            catch(Exception error){scriptsStatus=error.GetBaseException().Message;}
        }
        private void BeginRecording()
        {
            if(string.IsNullOrEmpty(scriptStem)){scriptsStatus=L("先加载或保存场景卡，以确定脚本路径","Load or save a scene card to establish the script path");return;}
            // An analysed scene can begin with an interval before the first
            // contact. Arm at the playhead and record neutral frames until a
            // pair is found, so the UI can export the whole scene from zero.
            if(!Clock.Available||Binding==null&&!autoCameraPairs){scriptsStatus=L("需要 Timeline 和有效角色绑定","Timeline and a valid actor pair are required");return;}
            recorder.Begin(Clock.Time);recording=true;recordingClock=Clock.Time;recordingWall=Time.realtimeSinceStartup;Drive.Reset();CaptureChapterCalibration();
            scriptsStatus=L("录制已开启；播放时记录，暂停时等待","Recording armed; captures playback and waits while paused");
        }
        private void RecordFrame()
        {
            if(!recording||!Clock.Available)return;
            double elapsed=Time.realtimeSinceStartup-recordingWall,advance=Clock.Time-recordingClock;
            if(recordingClock>=0&&(advance<-.01||advance>Math.Max(.5,elapsed*Math.Max(1,Clock.Speed)*3+.1))){FinishRecording();return;}
            recordingClock=Clock.Time;recordingWall=Time.realtimeSinceStartup;
            if(recorder.Count>0&&Clock.Time<recorder.End-.01){FinishRecording();return;}
            if(Clock.Playing&&Time.timeScale>0&&Drive.Ready)
            {if(!recorder.Add(Clock.Time,outputValues,climaxPulse))FinishRecording();}
            else if(recorder.Count>1&&recorder.End>=Clock.Duration-.1)FinishRecording();
        }
        private void FinishRecording()
        {
            if(!recording)return;recording=false;
            if(recorder.Count<2){scriptsStatus=L("录制帧不足，未生成文件","Too few recorded frames; no files written");return;}
            try
            {
                if(Clock.Duration>=recorder.End&&Clock.Duration-recorder.End<.1)recorder.ExtendEnd(Clock.Duration);
                recorder.Save(scriptStem);recordedPath=scriptStem+".funscript";recordedStart=recorder.Start;recordedEnd=recorder.End;
                scripts.Load(recordedPath);CaptureChapterCalibration();MarkSceneDataDirty();SaveSceneData();
                scriptsStatus=L("六轴脚本已保存 · ","Six-axis scripts saved · ")+recorder.Count+L(" 帧"," frames");
            }
            catch(Exception error){scriptsStatus=L("录制保存失败：","Recording save failed: ")+error.GetBaseException().Message;}
        }
        private bool TrySampleScriptFrame(out double[] values)
        {
            values=null;
            bool recorded=string.Equals(scripts.Path,recordedPath,StringComparison.OrdinalIgnoreCase)&&recordedEnd>recordedStart;
            return Clock.Available&&scripts.Scripts!=null&&
                (!recorded||Clock.Time>=recordedStart-.001&&Clock.Time<=recordedEnd+.001)&&scripts.Scripts.TrySample(Clock.Time,out values);
        }
        private bool ApplyScriptFrame()
        {
            double[] values;
            if(!TrySampleScriptFrame(out values)){Drive.Reset();return false;}
            Drive.UpdateScript(values);for(int i=0;i<6;i++)scriptEnabled[i]=Drive.Enabled[i]&&Numbers.Finite(values[i]);return true;
        }
        private void DrawScriptsPage()
        {
            float top=Card(new Rect(20,0,974,100),L("数据源","Motion source"));
            int chosen=GUI.Toolbar(new Rect(32,top,950,30),(int)motionSource,new[]{L("自动（优先脚本）","Auto (prefer script)"),L("实时计算","Live calculation"),L("脚本回放","Script playback")},button);
            if(chosen!=(int)motionSource)ChangeMotionSource(chosen);
            GUI.Label(new Rect(32,top+36,950,24),recording?L("当前：实时计算 → 录制","Current: live → recording"):UseScripts?L("当前：脚本 · ","Current: script · ")+scripts.Scripts.Count+" / 6":motionSource==MotionSource.Script?L("当前：等待选择脚本","Current: waiting for script"):L("当前：实时计算","Current: live calculation"),label);

            top=Card(new Rect(20,110,478,214),L("章节数据","Chapter data"));
            GUI.enabled=!scenePending&&!string.IsNullOrEmpty(scriptStem);
            if(B(new Rect(32,top,454,32),L("✦ 保存全部章节信息","✦ Save all chapter data"),primaryButton))SaveAllChapterData();
            GUI.enabled=true;
            GUI.Label(new Rect(32,top+40,454,40),L("章节保存包括：时间划分、角色配对、校准及章节效果。","Saves chapter timing, actor pairs, calibration and chapter effects."),wrapSmall);
            GUI.Label(new Rect(32,top+82,454,22),L("目录：","Folder: ")+(string.IsNullOrEmpty(scriptStem)?L("等待加载场景卡","Load a scene card"):Path.GetDirectoryName(scriptStem)),small);
            GUI.Label(new Rect(32,top+104,454,22),L("章节数据：","Chapter data: ")+(string.IsNullOrEmpty(scriptStem)?"—":Path.GetFileName(scriptStem)+".koisync.xml"),small);
            if(B(new Rect(32,top+138,454,30),L("▤ 打开保存目录","▤ Open save folder")))OpenScriptFolder();

            top=Card(new Rect(516,110,478,214),L("录制 / 脚本","Recording / scripts"));
            if(B(new Rect(528,top,222,32),recording?"■ "+L("结束录制并保存","Finish and save"):L("● 开始随播放录制","● Record playback"),recording?dangerButton:primaryButton)){if(recording)FinishRecording();else BeginRecording();}
            GUI.enabled=Clock.Available&&!scenePending&&!string.IsNullOrEmpty(scriptStem);
            string bakeBtn=StepBakeRunning?(L("■ 取消烘焙 ","■ Cancel ")+Math.Round(StepBakeProgress*100)+"%"):L("✦ 一键高速烘焙 6 轴","✦ Fast bake 6-axis");
            if(BTip(new Rect(760,top,222,32),bakeBtn,L("按当前章节与三档手感离线步进烘焙 50Hz 六轴 .funscript（无需实时等待、零丢帧）。","Deterministically bakes 50Hz 6-axis .funscript from locked chapters & feel preset (no real-time wait, zero dropped frames)."),StepBakeRunning?dangerButton:primaryButton))
            {
                if(StepBakeRunning)CancelStepBake();
                else try{StartScriptBake(0.02,true);}catch(Exception err){scriptsStatus=err.GetBaseException().Message;}
            }
            GUI.enabled=true;
            if(recording)GUI.Label(new Rect(528,top+40,454,24),recorder.Count+L(" 帧 · "," frames · ")+TimeText(recorder.Start)+" – "+TimeText(recorder.End),label);
            else GUI.Label(new Rect(528,top+40,454,40),L("按 Timeline 绝对时间回放；选择 L0 自动加载同名其他轴。","Uses absolute Timeline time; L0 loads matching companion axes."),wrapSmall);
            GUI.Label(new Rect(528,top+104,454,22),L("L0：","L0: ")+(string.IsNullOrEmpty(scripts.Path)?(string.IsNullOrEmpty(scriptStem)?"—":Path.GetFileName(scriptStem)+".funscript"):Path.GetFileName(scripts.Path)),small);
            if(B(new Rect(528,top+138,454,30),L("◈ 选择 L0 脚本…","◈ Choose L0 script…")))BrowseScript();

            top=Card(new Rect(20,334,974,114),L("状态","Status"));
            GUI.Label(new Rect(32,top,950,22),GroupingStatus(),small);
            GUI.Label(new Rect(32,top+24,950,52),scriptsStatus,wrapLabel);
        }
    }
}

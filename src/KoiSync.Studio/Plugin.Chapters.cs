using System;
using System.Collections;
using System.Collections.Generic;
using ExtensibleSaveFormat;
using UnityEngine;
using GameStudio=global::Studio.Studio;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        internal readonly ChapterBook Book=new ChapterBook();
        private readonly Dictionary<ChaControl,int> actorKeys=new Dictionary<ChaControl,int>();
        private bool followChapters;
        private bool forceSelectedChapterBinding;
        private string appliedChapter="";
        private double previousChapterTime=-1;
        private int Key(ChaControl actor){int key;return actor!=null&&actorKeys.TryGetValue(actor,out key)?key:-1;}
        private ChaControl Actor(int key){foreach(var row in actorKeys)if(row.Value==key)return row.Key;return null;}
        private Chapter MakeChapter(ChaControl source,ChaControl target,OpeningKind kind)
        {return new Chapter{Source=Key(source),Target=Key(target),Opening=(int)kind,SourceName=StudioBinding.Name(source),TargetName=StudioBinding.Name(target),Name=StudioBinding.Name(source)+" → "+StudioBinding.Name(target)+" · "+OpeningNames[(int)kind]};}

        // Called explicitly after the scene is ready. Inspect only keyframes of
        // attachment-enable tracks; bone/camera/material tracks are never visited.
        internal void ReadAttachmentChapters()
        {
            if(!autoCameraPairs){status=L("请先开启自动分析 / 分章","Enable analysis / chapters first");return;}
            try
            {
                if(actors.Count==0)ReadActors();
                ReadClimaxCues();
                var type=RuntimeAccess.Find("Timeline.Timeline");var self=RuntimeAccess.Read(type,"_self");
                var tracks=RuntimeAccess.Read(self,"_interpolables") as IDictionary;var events=new List<AttachmentEvent>();
                if(tracks!=null)foreach(DictionaryEntry row in tracks)
                {
                    var track=row.Value;if(track==null||!RuntimeAccess.Flag(track,"enabled")||Convert.ToString(RuntimeAccess.Read(track,"id"))!="constraintEnabled")continue;
                    var handle=RuntimeAccess.Read(track,"parameter");
                    var child=RuntimeAccess.Read(handle,"childTransform") as Transform;var parent=RuntimeAccess.Read(handle,"parentTransform") as Transform;
                    if(child==null||parent==null)continue;
                    var source=child.GetComponentInParent<ChaControl>();var target=parent.GetComponentInParent<ChaControl>();
                    if(source==null||target==null||source==target||Key(source)<0||Key(target)<0)continue;
                    var candidate=pairActors.Find(a=>a.Actor==source);
                    if(child.name!="k_f_dan_entry"&&(candidate==null||!candidate.Models.Exists(m=>m.Contains(child))))continue;
                    var keys=RuntimeAccess.Read(track,"keyframes") as IDictionary;if(keys==null||keys.Count==0)continue;
                    var kind=StudioBinding.Infer(StudioBinding.AnatomicalName(parent,target.transform),OpeningKind.Vaginal);
                    foreach(DictionaryEntry key in keys)
                    {
                        object value=RuntimeAccess.Read(key.Value,"value");if(!(value is bool))continue;
                        events.Add(new AttachmentEvent{Time=Convert.ToDouble(key.Key),Constraint=child.GetInstanceID()+":"+parent.GetInstanceID(),Enabled=(bool)value,Binding=MakeChapter(source,target,kind)});
                    }
                }
                if(events.Count>0){Book.ReadAttachmentEvents(events);status=UiText.T("已直接读取 ")+events.Count+UiText.T(" 个吸附切换键 · ")+Book.Chapters.Count+UiText.T(" 章");}
                else status=UiText.T("没有可读取的吸附切换键；可手动分章，或正常播放时只记录对象切换。");
                appliedChapter="";MarkSceneDataDirty();
            }
            catch(Exception error){status=UiText.T("章节读取：")+error.GetBaseException().Message;}
        }
        private void UpdateChapterBinding()
        {
            if(!followChapters||!Clock.Available)return;
            double time=Clock.Time;var chapter=Book.At(time);
            double previousTime=previousChapterTime;
            previousChapterTime=time;
            bool hidden=chapter!=null&&chapter.Source>=0&&(!ActorVisible(Actor(chapter.Source))||!ActorVisible(Actor(chapter.Target)));
            if(!forceSelectedChapterBinding&&autoCameraPairs&&!manualOpening&&!UseScripts&&(chapter==null||!chapter.Manual||hidden))
            {
                bool boundary=chapter!=null&&previousTime>=0&&previousTime<chapter.Start&&time>=chapter.Start;
                if(boundary)nextPairCheck=0; // Evaluate real attachment switches before using a saved boundary.
                var selected=CameraChapter();
                if(selected!=null&&(chapter==null||selected.Identity!=chapter.Identity))
                {
                    if(Clock.Playing&&chapterSelection.Continuous)
                    {
                        bool continuous=previousTime>=0&&time>=previousTime&&time-previousTime<1;
                        if(!(boundary&&continuous&&Book.CorrectAutomaticOpening(time,selected)))
                        {
                            selected.Calibration=CalibrationForPair(selected.Identity);
                            Book.Observe(chapter==null&&Book.Chapters.Count==0?0:Math.Min(time,chapterSelection.Start),selected,hidden);
                        }
                        chapter=Book.At(time);MarkSceneDataDirty();
                    }
                    else
                    {
                        // Seeking/pausing is read-only analysis. It may update
                        // the preview, but cannot permanently fragment chapters.
                        selected.Calibration=CalibrationForPair(selected.Identity);chapter=selected;
                    }
                }
                else if(selected==null&&hidden)
                {
                    chapter=new Chapter{Name=L("等待可见互动组","Waiting for a visible interaction")};
                    if(Clock.Playing&&chapterSelection.Continuous){Book.Observe(time,chapter,true);MarkSceneDataDirty();}
                }
            }
            if(chapter==null)return;
            string identity=chapter.Start.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+":"+chapter.Identity+":"+chapter.Manual;
            if(identity==appliedChapter&&(Binding!=null&&Binding.Alive&&(Binding.IsHandSource||Binding.Model==VisibleShaft(Binding.Source))||chapter.Source<0))
            {if(Binding!=null&&chapter.Calibration==null&&Book.Chapters.Contains(chapter)){chapter.Calibration=CurrentCalibration();MarkSceneDataDirty();}return;}
            bool sameBinding=Binding!=null&&Binding.Alive&&Key(Binding.Source)==chapter.Source&&Key(Binding.Target)==chapter.Target&&(int)Binding.Kind==chapter.Opening&&(Binding.IsHandSource||Binding.Model==VisibleShaft(Binding.Source));
            appliedChapter=identity;
            if(sameBinding&&CalibrationMatchesBinding(chapter.Calibration))
            {if(chapter.Calibration==null&&Book.Chapters.Contains(chapter)){chapter.Calibration=CurrentCalibration();MarkSceneDataDirty();}return;}
            if(chapter.Source<0||chapter.Target<0){ReleaseBinding(UiText.T("本章没有明确计算对象"),false);followChapters=true;return;}
            var source=Actor(chapter.Source);var target=Actor(chapter.Target);
            if(UseScripts&&(!ActorVisible(source)||!ActorVisible(target)))
            {Binding=null;Sample=null;Measured=null;preview.Hide();return;}
            if(source==null||target==null){ReleaseBinding(UiText.T("本章角色不可用，请重新读取角色"),false);return;}
            // Keep the user's armed state across valid automatic chapters.
            // Stop the old pose; frame-end solving supplies the new pose.
            Devices.Stop();
            var opening=(OpeningKind)chapter.Opening;
            Binding=new StudioBinding(source,target,opening,chapter.Calibration==null?0:chapter.Calibration.Length,autoCameraPairs&&!chapter.Manual,true,StudioHandSource.IsHand(opening)?null:VisibleShaft(source),true);
            ApplyBindingCalibration();
            sourceIndex=actors.IndexOf(Binding.Source);targetIndex=actors.IndexOf(Binding.Target);CaptureChapterCalibration();
            Drive.Reset();BindOperations++;status=Binding.Description;
        }
        private void SplitChapter()
        {Book.Split(Clock.Time);CaptureChapterCalibration();MarkSceneDataDirty();appliedChapter="";status=UiText.T("已在 ")+Clock.Time.ToString("F2")+UiText.T(" s 手动分章；可指定本章对象。");}
        private void AssignChapter()
        {
            if(actors.Count<2){status=UiText.T("请在连接窗口读取并选择角色");return;}
            var chapter=Book.At(Clock.Time)??Book.Split(0);var chosen=MakeChapter(actors[sourceIndex],actors[targetIndex],fallback);
            if(chapter.Identity!=chosen.Identity){chapter.RotationReference=null;chapter.StrokeCalibration=null;}
            if(chapter.Opening!=chosen.Opening&&(chapter.Opening>=6||chosen.Opening>=6))chapter.Calibration=null;
            chapter.Source=chosen.Source;chapter.Target=chosen.Target;chapter.Opening=chosen.Opening;chapter.Name=chosen.Name;chapter.SourceName=chosen.SourceName;chapter.TargetName=chosen.TargetName;chapter.Manual=true;MarkSceneDataDirty();
            appliedChapter="";manualOpening=false;followChapters=true;previousChapterTime=-1;status=UiText.T("本章计算对象已指定");
        }
        private void PreviousChapter(int direction)
        {
            var current=Book.At(Clock.Time);int index=current==null?0:Book.Chapters.IndexOf(current);
            index=Math.Max(0,Math.Min(Book.Chapters.Count-1,index+direction));if(Book.Chapters.Count>0)TimelineAction(()=>Clock.Seek(Book.Chapters[index].Start));
        }
        private void SaveChapters(string path)
        {
            bodyGrind.RestorePreviousFrame();
            try
            {
                SetScenePath(path);CaptureChapterCalibration();MarkSceneDataDirty();SaveSceneData();
                var data=new PluginData{version=2};data.data["chapters"]=Book.ToXml();data.data["origins"]=modelOrigins.ToXml();
                ExtendedSave.SetSceneExtendedDataById("local.koisync.studio",data);
            }
            catch(Exception error){Logger.LogWarning(UiText.T("章节保存：")+error.Message);}
        }
        private void LoadChapters(string path)
        {
            Book.Chapters.Clear();modelOrigins.Clear();followChapters=false;appliedChapter="";previousChapterTime=-1;
            try
            {
                var data=ExtendedSave.GetSceneExtendedDataById("local.koisync.studio");object xml;
                if(data!=null&&data.version<=2&&data.data.TryGetValue("chapters",out xml)&&xml is string)Book.FromXml((string)xml);
                if(data!=null&&data.version<=2&&data.data.TryGetValue("origins",out xml)&&xml is string)modelOrigins.FromXml((string)xml);
            }
            catch(Exception error){status=UiText.T("章节元数据读取：")+error.Message;}
            QueueSceneReady(path);
        }
        private void ImportChapters(string path)
        {Book.Chapters.Clear();followChapters=false;status=UiText.T("场景导入后请重新读取吸附章节，以使用导入后的对象编号。");}
    }
}

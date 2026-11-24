using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using KoiSync.Core;
using UnityEngine;
using OCI=global::Studio.ObjectCtrlInfo;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> climaxDetection,climaxDefaultEnabled;
        private ConfigEntry<float> climaxMinimum,climaxMaximum,climaxHz,climaxEventSeconds;
        private sealed class ClimaxTrack
        {
            internal object Track;internal OCI Object;internal int Owner=-1;internal bool Marker;
            internal string Name;internal List<ClimaxInterval> Windows;
        }
        private readonly List<ClimaxTrack> climaxTracks=new List<ClimaxTrack>();
        private bool climaxCuesReady;
        private double climaxPreviousTime=-1,nativeClimaxStart=-1;
        private string nativeClimaxPair="",climaxEvidence="";
        private AxisOscillation climaxPulse;
        private double climaxLiveValue=double.NaN;
        private bool ClimaxEffectActive {get{return climaxPulse!=null||Numbers.Finite(climaxLiveValue);}}
        private readonly LiveL0Cycle liveL0Cycle=new LiveL0Cycle();
        private StudioBinding frequencyBinding;
        private MotionVector frequencyOrigin;
        private double frequencyLength;
        private void InitializeClimax()
        {
            climaxDetection=Config.Bind("Climax","Detect",true,"Detect explicit events only while scene Analysis / chapters is enabled.");
            climaxDefaultEnabled=Config.Bind("Climax","L0Enabled",true);
            climaxMinimum=Config.Bind("Climax","L0Minimum",350f,new ConfigDescription("Device coordinates, 0-999.",new AcceptableValueRange<float>(0,999)));
            climaxMaximum=Config.Bind("Climax","L0Maximum",750f,new ConfigDescription("Device coordinates, 0-999.",new AcceptableValueRange<float>(0,999)));
            climaxHz=Config.Bind("Climax","Hertz",10f,new ConfigDescription("Legacy default for chapters without saved frequency. Edit each chapter in Calibration.",new AcceptableValueRange<float>(.1f,20)));
            climaxEventSeconds=Config.Bind("Climax","EffectMaximumSeconds",6f,new ConfigDescription("Maximum duration of a shooting prop activation; persistent residue must not vibrate indefinitely.",new AcceptableValueRange<float>(.1f,60)));
        }
        private ClimaxOptions ClimaxDefaults()
        {return new ClimaxOptions{LocalSettings=true,Detect=climaxDetection.Value,MaximumSeconds=climaxEventSeconds.Value,Enabled=climaxDefaultEnabled.Value,Minimum=Math.Min(climaxMinimum.Value,climaxMaximum.Value),Maximum=Math.Max(climaxMinimum.Value,climaxMaximum.Value),Hertz=climaxHz.Value,FrequencyMode=ClimaxFrequencyMode.AutoMotion};}
        private void ResetClimax()
        {climaxTracks.Clear();climaxCuesReady=false;climaxPreviousTime=nativeClimaxStart=-1;nativeClimaxPair=climaxEvidence="";climaxPulse=null;climaxLiveValue=double.NaN;liveL0Cycle.Reset();frequencyBinding=null;}
        private void ObserveLiveL0Frequency()
        {
            if(Binding==null||Measured==null||!Clock.Available){liveL0Cycle.Reset();frequencyBinding=null;return;}
            if(frequencyBinding!=Binding||Math.Abs(frequencyLength-Binding.RestLength)>1e-9||(frequencyOrigin-Binding.OriginOffset).Length>1e-9)
            {liveL0Cycle.Reset();frequencyBinding=Binding;frequencyLength=Binding.RestLength;frequencyOrigin=Binding.OriginOffset;}
            liveL0Cycle.Observe(Clock.Time,-Measured.TargetDepth,Clock.Playing,Math.Max(.0001,Binding.RestLength*.01));
        }
        private ClimaxOptions ChapterEffectOptions(Chapter chapter)
        {
            return ClimaxOptions.Resolve(chapter==null?null:chapter.Climax,ClimaxDefaults());
        }
        private bool ShouldUseFixedClimaxOscillation(Chapter chapter)
        {
            if(chapter==null||!chapter.IsClimax)return false;
            var options=ChapterEffectOptions(chapter);
            double ampPct=Numbers.Finite(chapter.DepthP05)&&Numbers.Finite(chapter.DepthP95)?Math.Abs(chapter.DepthP95-chapter.DepthP05)*100.0:double.NaN;
            return options.ShouldUseFixedOscillation(chapter.PeakHz,ampPct,liveL0Cycle.Ready,liveL0Cycle.Hertz);
        }
        private void BuildChapterEffect(Chapter chapter)
        {
            climaxPulse=null;climaxLiveValue=double.NaN;
            if(chapter==null||!chapter.IsClimax||UseScripts)return;
            var options=ChapterEffectOptions(chapter);if(!options.Enabled)return;
            if(options.FrequencyMode==ClimaxFrequencyMode.LiveL0)
            {
                if(liveL0Cycle.Ready)climaxLiveValue=(options.Minimum+(options.Maximum-options.Minimum)*liveL0Cycle.Phase)/999;
                return;
            }
            if(!ShouldUseFixedClimaxOscillation(chapter))return;
            int index=Book.Chapters.IndexOf(chapter);
            double end=index+1<Book.Chapters.Count?Book.Chapters[index+1].Start:Clock.Duration;
            if(chapter.ClimaxDetected)end=Math.Min(end,chapter.Start+Math.Max(1.0,options.MaximumSeconds));
            if(Clock.Time>=end)return;
            climaxPulse=options.Create(chapter.Start,end,Math.Max(Drive.Minimum[0],Drive.PhysicalL0Minimum),Drive.Maximum[0]);
        }
        private void ReadClimaxCues()
        {
            climaxTracks.Clear();climaxCuesReady=true;
            if(!autoCameraPairs||!Clock.Available)return;
            var self=RuntimeAccess.Read(RuntimeAccess.Find("Timeline.Timeline"),"_self");
            var tracks=RuntimeAccess.Read(self,"_interpolables") as IDictionary;
            if(tracks==null)return;
            foreach(DictionaryEntry row in tracks)
            {
                object track=row.Value;if(Convert.ToString(RuntimeAccess.Read(track,"id"))!="objectEnabled")continue;
                var item=RuntimeAccess.Read(track,"oci") as OCI;if(item==null||item.treeNodeObject==null)continue;
                string name=item.treeNodeObject.textName;bool marker=ClimaxCues.Marker(name);
                if(!marker&&!ClimaxCues.Shooting(name))continue;
                var keys=RuntimeAccess.Read(track,"keyframes") as IDictionary;if(keys==null)continue;
                var sorted=new SortedDictionary<double,bool>();
                foreach(DictionaryEntry key in keys){object value=RuntimeAccess.Read(key.Value,"value");if(value is bool)sorted[Convert.ToDouble(key.Key)]=(bool)value;}
                var times=new List<double>();var values=new List<bool>();foreach(var key in sorted){times.Add(key.Key);values.Add(key.Value);}
                int owner=-1;
                for(var node=item.treeNodeObject.parent;node!=null&&owner<0;node=node.parent)
                    foreach(var actor in actorKeys){OCI info;if(global::Studio.Studio.Instance.dicObjectCtrl.TryGetValue(actor.Value,out info)&&info.treeNodeObject==node){owner=actor.Value;break;}}
                var windows=ClimaxCues.Windows(times,values,Clock.Duration,Clock.Duration,ClimaxCues.EpisodeGap);
                if(windows.Count>0)climaxTracks.Add(new ClimaxTrack{Track=track,Object=item,Owner=owner,Marker=marker,Name=name,Windows=windows});
            }
        }
        private bool ClimaxOwnerMatches(ClimaxTrack cue)
        {
            if(Binding==null||cue.Object==null||cue.Object.treeNodeObject==null)return false;
            // The tracked node's own visibility is evaluated from keys, including
            // short pulses between frames. Hidden parent groups still exclude it.
            for(var node=cue.Object.treeNodeObject.parent;node!=null;node=node.parent)if(!node.visible)return false;
            int source=Key(Binding.Source),target=Key(Binding.Target);
            if(cue.Owner>=0)return cue.Owner==source||cue.Owner==target;
            if(cue.Marker)
            {
                int visible=0;foreach(var actor in actors)if(ActorVisible(actor))visible++;
                return visible==2;
            }
            if(cue.Object.guideObject==null||cue.Object.guideObject.transformTarget==null)return false;
            Vector3 position=cue.Object.guideObject.transformTarget.position;
            float distance=Math.Min(Vector3.Distance(position,Binding.Opening.position),Vector3.Distance(position,Binding.Chain[Binding.Chain.Length-1].position));
            if(distance>Math.Max(.3,Binding.RestLength*2))return false;
            // An unparented effect must be clearly nearer this pair than an
            // unrelated visible actor. Ambiguous multi-pair shots stay manual.
            foreach(var candidate in pairActors)
            {
                if(candidate.Actor==Binding.Source||candidate.Actor==Binding.Target||!ActorVisible(candidate.Actor))continue;
                if(candidate.Root!=null&&Vector3.Distance(position,candidate.Root.position)<distance+.04)return false;
                foreach(var opening in candidate.Openings)
                    if(opening.Contact.Alive&&Vector3.Distance(position,opening.Position)<distance+.04)return false;
            }
            return true;
        }
        private string NativeClimax(ChaControl actor)
        {
            if(actor==null||actor.animBody==null||!ActorVisible(actor))return "";
            // A frozen base animation beneath an FK/IK Timeline is not evidence.
            if(Math.Abs(actor.animBody.speed)<.0001)return "";
            OCI item;if(global::Studio.Studio.Instance.dicObjectCtrl.TryGetValue(Key(actor),out item))
            {var character=item as global::Studio.OCIChar;if(character!=null&&Math.Abs(character.oiCharInfo.animeSpeed)<.0001)return "";}
            foreach(var clip in actor.animBody.GetCurrentAnimatorClipInfo(0))
                if(clip.weight>=.5f&&clip.clip!=null&&ClimaxCues.Animation(clip.clip.name))return clip.clip.name;
            return "";
        }
        private void UpdateClimax()
        {
            climaxPulse=null;climaxLiveValue=double.NaN;climaxEvidence="";
            double time=Clock.Time,previous=climaxPreviousTime;climaxPreviousTime=time;
            if(Binding==null||!Clock.Available||!ActorVisible(Binding.Source)||!ActorVisible(Binding.Target))return;
            var chapter=RotationChapter();if(chapter==null)return;
            var chapterOptions=ChapterEffectOptions(chapter);
            if(autoCameraPairs&&chapterOptions.Detect&&!UseScripts)
            {
                if(!climaxCuesReady)ReadClimaxCues();
                bool changed=false,explicitEvent=false;double eventStart=double.MaxValue,eventEnd=-1;
                var nearby=new List<ClimaxInterval>();string eventName="";
                foreach(var cue in climaxTracks)
                {
                    if(!RuntimeAccess.Flag(cue.Track,"enabled")||!ClimaxOwnerMatches(cue))continue;
                    var windows=new List<ClimaxInterval>();
                    foreach(var raw in cue.Windows)windows.Add(new ClimaxInterval(raw.Start,cue.Marker?raw.End:Math.Min(raw.End,raw.Start+chapterOptions.MaximumSeconds)));
                    nearby.AddRange(windows);
                    foreach(var interval in windows)
                    {
                        if(interval.Start>time||interval.End<=time)continue;
                        eventStart=Math.Min(eventStart,interval.Start);eventEnd=Math.Max(eventEnd,interval.End);
                        explicitEvent=true;eventName="Timeline: "+cue.Name;
                    }
                }
                if(explicitEvent)
                {
                    nearby.Sort((a,b)=>a.Start.CompareTo(b.Start));
                    bool expanded;
                    do
                    {
                        expanded=false;
                        foreach(var interval in nearby)
                        {
                            if(interval.Start<=eventEnd+ClimaxCues.EpisodeGap&&interval.End>=eventStart-ClimaxCues.EpisodeGap)
                            {
                                double nextStart=Math.Min(eventStart,interval.Start),nextEnd=Math.Max(eventEnd,interval.End);
                                if(nextStart<eventStart-1e-9||nextEnd>eventEnd+1e-9){eventStart=nextStart;eventEnd=nextEnd;expanded=true;}
                            }
                        }
                    }
                    while(expanded);
                    int chapterIndex=Book.Chapters.IndexOf(chapter);
                    double chapterEnd=chapterIndex+1<Book.Chapters.Count?Book.Chapters[chapterIndex+1].Start:Clock.Duration;
                    changed|=Book.MarkClimax(Math.Max(chapter.Start,eventStart),Math.Min(chapterEnd,eventEnd),Clock.Duration,chapter.Identity,chapterOptions,eventName);
                    climaxEvidence=eventName;
                }
                string native=NativeClimax(Binding.Source);if(native.Length==0)native=NativeClimax(Binding.Target);
                bool continuous=Clock.Playing&&previous>=0&&time>=previous&&time-previous<.5;
                if(continuous&&!explicitEvent)
                {
                    if(native.Length>0)
                    {
                        if(nativeClimaxPair!=chapter.Identity||nativeClimaxStart<0){nativeClimaxStart=time;nativeClimaxPair=chapter.Identity;}
                        changed|=Book.ObserveClimax(time,true,chapter.Identity,chapterOptions,"Animation: "+native);
                        climaxEvidence="Animation: "+native;
                    }
                    else
                    {
                        var active=Book.At(time);
                        if(active!=null&&active.ClimaxDetected&&active.ClimaxEvidence.StartsWith("Animation: "))
                            changed|=Book.ObserveClimax(time,false,chapter.Identity,chapterOptions,"");
                        nativeClimaxStart=-1;nativeClimaxPair="";
                    }
                }
                if(changed){MarkSceneDataDirty();chapter=Book.At(time);}
            }
            if(!chapter.IsClimax||UseScripts)return;
            BuildChapterEffect(chapter);
            if(climaxEvidence.Length==0)climaxEvidence=chapter.Phase==ChapterPhase.Climax?L("手动高潮章节","Manual climax chapter"):chapter.ClimaxEvidence;
        }
        private void RefreshActiveClimaxPulse()
        {
            climaxPulse=null;climaxLiveValue=double.NaN;
            if(!UseScripts&&Binding!=null&&Clock.Available)
            {
                var chapter=RotationChapter();
                BuildChapterEffect(chapter);
            }
            ApplyClimaxPulse();
            // A GUI selection may happen after the camera was solved. Ensure
            // the next render also rebuilds the full pose without waiting for
            // a later Timeline tick.
            SolvedFrame=-1;
        }
        private void ApplyClimaxPulse()
        {
            if(!Drive.Ready)return;
            // This explicit effect has its own frequency. Applying the ordinary
            // pose low-pass/slew again would flatten 10 Hz almost completely.
            if(climaxPulse!=null)Drive.ApplyL0Effect(climaxPulse,Clock.Time);
            else if(Numbers.Finite(climaxLiveValue))Drive.ApplyL0EffectValue(climaxLiveValue);
        }
    }
}

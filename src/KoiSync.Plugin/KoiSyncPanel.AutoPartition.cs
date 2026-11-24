using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using KoiSync.Core;
using UnityEngine;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed partial class KoiSyncPanel
    {
        private bool autoPartitionRunning,cancelAutoPartition;
        internal bool LaboratoryBusy{get{return autoPartitionRunning;}}
        private SceneProfile automaticScene;
        private List<TimelineSegment> beforeAutomatic;
        private InputField automaticDistance,automaticAngle,automaticMinimum;
        private Text automaticStatus;
        private void OpenAutoPartition()
        {
            if(autoPartitionRunning)return;
            var p=TrackPopup(UnityEngine.Input.mousePosition,490,292);
            Label(p,"按大幅镜头变化自动分区",14,10,463,30,18,Accent,FontStyle.Bold);
            Label(p,"直接读取镜头关键帧，不改变播放位置。\n来源使用已播放记录；未采样或不明确的段设为“无”。",14,48,463,46,12,Muted);
            Label(p,"位移阈值（世界单位）",14,106,229,26,12,Muted);automaticDistance=Input(p,287,101,185,"1");
            Label(p,"转角阈值（°）",14,147,229,26,12,Muted);automaticAngle=Input(p,287,142,185,"40");
            Label(p,"最短段长（秒）",14,188,229,26,12,Muted);automaticMinimum=Input(p,287,183,185,"1");
            Button(p,"生成并替换轨道",14,237,220,35,()=>LabAction(()=>{
                double distance=ProfileNumber(automaticDistance),angle=ProfileNumber(automaticAngle),minimum=ProfileNumber(automaticMinimum);
                if(distance<=0||angle<1||angle>180||minimum<.05)throw new ArgumentException("位移 > 0，角度 1–180，最短段长 ≥ 0.05 s");
                SaveAlignment(true);TimelineState.Refresh();if(!TimelineState.Available||TimelineState.Duration<.001)throw new ArgumentException("请先打开 Timeline");
                autoPartitionRunning=true;cancelAutoPartition=false;host.PauseOutput("自动分区分析 · 不输出设备运动");
                var progress=TrackPopup(UnityEngine.Input.mousePosition,490,161);trackPopup.GetComponent<PopupDismiss>().Dismiss=null;
                automaticStatus=Label(progress,"正在读取镜头轨道…",14,15,463,85,14,Accent);
                Button(progress,"取消分析",14,112,463,34,()=>cancelAutoPartition=true);
                StartCoroutine(RunAutoPartition(distance,angle,minimum));
            }));
            var undo=Button(p,"撤销上次自动分区",250,237,222,35,()=>LabAction(()=>{if(automaticScene!=host.Profiles.Scene||beforeAutomatic==null)return;host.Profiles.Scene.Segments=beforeAutomatic;beforeAutomatic=null;host.Profiles.Changed(true);LoadAlignment("");DismissTrackPopup();}));
            undo.interactable=automaticScene==host.Profiles.Scene&&beforeAutomatic!=null;
        }
        private IEnumerator RunAutoPartition(double distance,double angle,double minimum)
        {
            var work=AnalyzeTimeline(distance,angle,minimum);string failure=null;
            try
            {
                while(true)
                {
                    bool more;object wait=null;
                    try{more=work.MoveNext();if(more)wait=work.Current;}
                    catch(Exception ex){failure=ex.GetBaseException().Message;break;}
                    if(!more)break;yield return wait;
                }
            }
            finally
            {
                var disposable=work as IDisposable;if(disposable!=null)disposable.Dispose();autoPartitionRunning=false;
                DismissTrackPopup();if(failure!=null)alignmentStatus.text="自动分区未应用："+failure;
            }
        }
        private bool AnalysisCancelled(SceneProfile scene)
        {return cancelAutoPartition||!LaboratoryOpen||host.Profiles.Scene!=scene;}
        private static Camera AnalysisCamera()
        {var studio=UnityEngine.Object.FindObjectOfType<Studio.Studio>();return studio!=null&&studio.cameraCtrl!=null?StudioMembers.Read(studio.cameraCtrl,"mainCmaera") as Camera??Camera.main:Camera.main;}
        private IEnumerator AnalyzeTimeline(double distance,double angle,double minimum)
        {
            var scene=host.Profiles.Scene;double duration=TimelineState.Duration;
            var schedule=StudioAutoPartition.Schedule(duration,TimelineState.CameraKeyTimes());var observations=new List<CameraObservation>();var camera=AnalysisCamera();
            for(int i=0;i<schedule.Count;i++)
            {
                if(AnalysisCancelled(scene))yield break;
                CameraObservation observation;string reason;
                if(!TimelineState.CameraAt(schedule[i],camera,out observation,out reason))throw new ArgumentException(reason.Length==0?"镜头关键帧类型无法读取":reason);
                observations.Add(observation);
                if(i%120==0){automaticStatus.text="直接读取镜头 "+(i+1)+" / "+schedule.Count+"\n播放位置保持不变";yield return null;}
            }
            var boundaries=StudioAutoPartition.Boundaries(observations,distance,angle,minimum,duration);var generated=new List<TimelineSegment>();
            for(int i=1;i<boundaries.Count;i++)
            {
                string key=null,label="",name="";bool reliable=true;
                for(int probe=1;probe<=3;probe++)
                {
                    double t=boundaries[i-1]+(boundaries[i]-boundaries[i-1])*probe/4;List<StudioAutoPartition.PoseRecord> poses;
                    if(recordedScene!=scene||!recordedChoices.TryGetValue((int)Math.Round(t*4),out poses)){reliable=false;continue;}
                    CameraObservation view;string reason;if(!TimelineState.CameraAt(t,camera,out view,out reason)){reliable=false;continue;}
                    var choice=StudioAutoPartition.Choose(poses,view);if(choice==null){reliable=false;continue;}
                    if(key!=null&&key!=choice.Source)reliable=false;key=choice.Source;label=choice.Label;name=choice.Name;
                }
                generated.Add(new TimelineSegment{Start=boundaries[i-1],End=boundaries[i],Source=reliable&&key!=null?key:TimelineSegment.NoSource,Label=reliable?label:"",Name=reliable&&key!=null?name:"无",ColorIndex=i-1,Measurement=new MeasurementProfile{AlignmentVersion=1}});
            }
            if(AnalysisCancelled(scene))yield break;
            var staged=new SceneProfile{Segments=generated};staged.Validate();
            beforeAutomatic=scene.Segments;automaticScene=scene;scene.Segments=generated;host.Profiles.Changed(true);LoadAlignment("");
            alignmentStatus.text="直接分区完成："+generated.Count+" 段，"+generated.FindAll(s=>s.Source==TimelineSegment.NoSource).Count+" 段无来源 / 未采样；可播放记录后重新分区。";
        }
    }
}

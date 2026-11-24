using System;
using System.Collections.Generic;
using System.Globalization;
using KoiSync.Core;
using UnityEngine;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed partial class KoiSyncPanel
    {
        private GameObject playbackPage;
        private Text playbackTitle,playbackState,playbackNotice;
        private TimelineStrip playbackStrip;
        private Dropdown playbackSegments,playbackSource,playbackTarget;
        private InputField segmentStart,segmentEnd,profileLength,profileGain,profileLow,profileHigh;
        private readonly InputField[,] profileOffsets=new InputField[3,3];
        private Toggle profileFixed;
        private RangeSlider profileRange;
        private readonly List<string> playbackKeys=new List<string>(),bindingKeys=new List<string>();
        private NativeTarget[] playbackTargets;
        private SceneProfile shownScene;
        private string editingKey="",shownNative="",bindingSignature="";
        private bool playbackRefreshing;
        private bool profileApplied;
        private int shownRevision=-1;
        private TimelineSegment EditedSegment{get{return host.Profiles.Scene.Segments.Find(s=>s.Id==editingKey);}}
        private MeasurementProfile EditedMeasurement{get{if(host.IsMainGame){var a=host.Profiles.Animation(editingKey);return a==null?null:a.Measurement;}var s=EditedSegment;return s==null?null:s.Measurement;}}
        private void ProfileAction(Action action)
        {try{action();playbackNotice.text=host.Profiles.Status;}catch(Exception ex){playbackNotice.text=ex.GetBaseException().Message;}}
        private void ShowPlaybackProfiles()
        {
            if(!host.IsMainGame){ShowStudioExperiment();return;}
            if(playbackPage!=null){ActivatePage(WorkspacePage.Playback);RefreshPlaybackProfiles();return;}
            var page=CreatePage(WorkspacePage.Playback);playbackPage=page.gameObject;
            playbackTitle=Label(page,host.IsMainGame?"本体 · 动画来源与参考点":"Timeline · 分段与参考点",22,15,950,35,23,Accent,FontStyle.Bold);
            playbackState=Label(page,"",23,55,950,36,12,Muted);
            if(!host.IsMainGame)
            {
                var track=Rect(page,"Timeline segments",22,98,950,35);playbackStrip=track.gameObject.AddComponent<TimelineStrip>();playbackStrip.color=Color.white;
                playbackStrip.Seek=t=>ProfileAction(()=>{TimelineState.Seek(t);var s=host.Profiles.Scene.At(t);if(s!=null)editingKey=s.Id;ReadProfileEditor();});
            }
            playbackSegments=Dropdown(page,22,145,480,new[]{"等待配置"},i=>{if(playbackRefreshing||i<0||i>=playbackKeys.Count)return;editingKey=playbackKeys[i];ReadProfileEditor();});
            if(!host.IsMainGame)
            {
                Button(page,"播放 / 暂停",520,145,138,34,TimelineState.TogglePlaying);
                Button(page,"建立全长段",672,145,142,34,()=>ProfileAction(()=>{
                    if(host.Profiles.Scene.Segments.Count>0)throw new InvalidOperationException("已有分段，请使用在播放头切段");
                    if(TimelineState.Duration<=.001)throw new InvalidOperationException("请先设置 Timeline 总时长");
                    var s=new TimelineSegment{Start=0,End=TimelineState.Duration,Measurement=new MeasurementProfile{LengthScale=host.StudioOptions.LengthScale.Value,Gain=host.StudioOptions.Gain.Value,Target=host.StudioOptions.Target.Value,ReverseCollider=host.StudioOptions.ReverseCollider.Value}};
                    var source=host.StudioAdapter.Find(host.SelectedId);if(source!=null){s.Source=source.SavedKey;s.Label=source.Label;}
                    host.Profiles.Scene.Segments.Add(s);editingKey=s.Id;host.Profiles.Changed(true);RefreshPlaybackProfiles();ReadProfileEditor();}));
                Button(page,"播放头切段",828,145,145,34,()=>ProfileAction(()=>{editingKey=host.Profiles.Scene.Split(TimelineState.Time).Id;host.Profiles.Changed(true);RefreshPlaybackProfiles();ReadProfileEditor();}));
                Label(page,"段起点 / 终点（秒）",23,192,208,27,12,Muted);
                segmentStart=Input(page,226,189,124,"0");segmentEnd=Input(page,366,189,124,"1");
                Button(page,"删除所选段",520,189,138,33,()=>ProfileAction(()=>{var s=EditedSegment;if(s==null)return;host.Profiles.Scene.Segments.Remove(s);editingKey="";host.Profiles.Changed(true);RefreshPlaybackProfiles();ReadProfileEditor();}));
                Button(page,"重载场景配置",674,189,298,33,()=>ProfileAction(()=>{if(host.Profiles.ScenePath.Length==0)throw new InvalidOperationException("请先加载场景卡");host.Profiles.LoadScene(host.Profiles.ScenePath);RefreshPlaybackProfiles();}));
                playbackSource=Dropdown(page,22,234,950,new[]{"自动 · 镜头方向 / 距离，BP 优先"},i=>{});
            }
            else
            {
                Label(page,"当前动作自动匹配；也可选择已经保存的其他动画进行编辑。",22,99,940,30,14,Muted);
                Button(page,"编辑当前动画",522,145,218,34,()=>{editingKey=host.Profiles.NativeKey;ReadProfileEditor();});
                Button(page,"重载动画配置",756,145,217,34,()=>ProfileAction(()=>{host.Profiles.ReloadNative();shownRevision=-1;RefreshPlaybackProfiles();ReadProfileEditor();}));
                Label(page,"切换动作或结束场景仍保持同步启用，缺少有效数据时等待。",23,192,944,29,13,Muted);
            }
            var targetList=new List<NativeTarget>();var labels=new List<string>();foreach(NativeTarget t in Enum.GetValues(typeof(NativeTarget)))if(t!=NativeTarget.BOTH_FEET&&(host.IsMainGame||NativeProfiles.ActorIndex(t)==0)){targetList.Add(t);labels.Add(t==NativeTarget.AUTO?"AUTO · 绑定 / 动画映射":t.ToString());}playbackTargets=targetList.ToArray();
            playbackTarget=Dropdown(page,22,280,350,labels.ToArray(),i=>{});
            Label(page,"有效长度 ×",390,286,100,25,12,Muted);profileLength=Input(page,491,280,108,"1");
            Label(page,"行程倍率 ×",617,286,100,25,12,Muted);profileGain=Input(page,724,280,108,"1");
            Button(page,"取当前范围",847,280,126,34,()=>ProfileAction(()=>{double lo,hi;if(!host.Pipeline.TryGetNativeRange(out lo,out hi))throw new InvalidOperationException("当前来源尚无有效观察范围");profileRange.SetRange((float)lo,(float)hi);profileLow.text=Math.Round(lo*999).ToString();profileHigh.text=Math.Round(hi*999).ToString();profileFixed.isOn=true;}));
            profileFixed=Toggle(page,"使用本段 / 动画的插入范围",22,326,365,false,v=>{});
            profileLow=Input(page,395,322,75,"0");profileHigh=Input(page,897,322,75,"999");
            profileRange=Range(page,483,325,400,0,1,(lo,hi)=>{if(playbackRefreshing)return;profileLow.text=Math.Round(lo*999).ToString();profileHigh.text=Math.Round(hi*999).ToString();});
            Label(page,"L0：两点距离 / 有效长度，重合 000；固定范围可重新映射这段比例。",23,363,950,24,12,Muted);
            string[] names={"参考平面偏移","目标参考点偏移","来源根部偏移"};
            for(int row=0;row<3;row++)
            {
                Label(page,names[row],24,403+row*47,200,28,14,Accent);
                for(int col=0;col<3;col++){float x=237+col*244;Label(page,new[]{"X","Y","Z"}[col],x,405+row*47,25,22,13,Muted);profileOffsets[row,col]=Input(page,x+29,398+row*47,196,"0");}
            }
            Label(page,"XYZ 偏移沿蓝色参考面的局部轴；旋转、末端、尺寸和对象隐藏在浮动 3D 对齐窗口编辑。",23,543,950,27,12,Muted);
            Button(page,"应用并保存",22,583,222,37,ApplyProfileEditor);
            Button(page,"偏移归零",260,583,220,37,()=>{foreach(InputField input in profileOffsets)input.text="0";});
            Button(page,"当前动画 3D 对齐",496,583,222,37,()=>{ApplyProfileEditor();if(profileApplied)ShowStudioTools();});
            Button(page,"保存全部配置",734,583,239,37,()=>ProfileAction(()=>{if(host.IsMainGame)host.Profiles.SaveNative();else host.Profiles.SaveScene();}));
            playbackNotice=Label(page,"",23,630,950,28,12,Muted);
            RefreshPlaybackProfiles();ReadProfileEditor();
        }
        private void RefreshPlaybackProfiles()
        {
            if(playbackPage==null||!playbackPage.activeInHierarchy)return;
            var profiles=host.Profiles;
            playbackState.text=host.IsMainGame?"当前："+profiles.NativeKey+"\n"+profiles.NativeFile:
                (profiles.ScenePath.Length==0?"未确定场景卡，请保存或重新加载场景卡":System.IO.Path.GetFileName(profiles.ScenePath))+" · "+TimelineState.Time.ToString("F2")+" / "+TimelineState.Duration.ToString("F2")+" s · "+(TimelineState.Playing?"播放":"暂停")+"\n"+(profiles.CurrentSegment==null?"当前无分段 · 使用工作室选源":profiles.CurrentSegment.Label.Length==0?"当前段按镜头自动选源":profiles.CurrentSegment.Label);
            if(playbackStrip!=null)playbackStrip.Set(profiles.Scene,TimelineState.Time,TimelineState.Duration,editingKey);
            bool changed=shownScene!=profiles.Scene||shownRevision!=profiles.Revision||host.IsMainGame&&shownNative!=profiles.NativeKey;
            if(changed)
            {
                if(shownScene!=profiles.Scene)editingKey="";
                if(host.IsMainGame&&shownNative!=profiles.NativeKey)editingKey=profiles.NativeKey;
                shownScene=profiles.Scene;shownRevision=profiles.Revision;shownNative=profiles.NativeKey;
                playbackRefreshing=true;playbackKeys.Clear();playbackSegments.options.Clear();
                if(host.IsMainGame){foreach(var a in profiles.Native.Animations){playbackKeys.Add(a.Key);playbackSegments.options.Add(new Dropdown.OptionData(a.Key));}}
                else foreach(var s in profiles.Scene.Segments){playbackKeys.Add(s.Id);playbackSegments.options.Add(new Dropdown.OptionData(s.Start.ToString("F2")+" — "+s.End.ToString("F2")+" s · "+(s.Label.Length==0?"自动选源":s.Label)));}
                if(editingKey.Length==0&&playbackKeys.Count>0)editingKey=playbackKeys[0];
                if(playbackKeys.Count==0)playbackSegments.options.Add(new Dropdown.OptionData(host.IsMainGame?"等待本体动画":"点击建立全长段"));
                playbackRefreshing=false;ReadProfileEditor();
            }
            if(!host.IsMainGame)
            {
                string signature="";foreach(var s in host.StudioAdapter.Sources)signature+=s.SavedKey+":"+s.Label+";";
                if(signature!=bindingSignature){bindingSignature=signature;RebuildBindingChoices();ReadProfileEditor();}
            }
            playbackRefreshing=true;playbackSegments.value=Math.Max(0,playbackKeys.IndexOf(editingKey));playbackSegments.RefreshShownValue();playbackRefreshing=false;
        }
        private void RebuildBindingChoices()
        {
            bindingKeys.Clear();bindingKeys.Add("");playbackSource.options.Clear();playbackSource.options.Add(new Dropdown.OptionData("自动 · 镜头方向 / 距离，BP 优先"));
            foreach(var s in host.StudioAdapter.Sources){bindingKeys.Add(s.SavedKey);playbackSource.options.Add(new Dropdown.OptionData(s.Label));}
        }
        private void ReadProfileEditor()
        {
            if(profileLength==null)return;MeasurementProfile m=EditedMeasurement;playbackRefreshing=true;
            try
            {
                if(m==null)m=new MeasurementProfile();
                if(!host.IsMainGame)
                {
                    var s=EditedSegment;if(s!=null){segmentStart.text=s.Start.ToString("F3",CultureInfo.InvariantCulture);segmentEnd.text=s.End.ToString("F3",CultureInfo.InvariantCulture);}
                    string key=s==null?"":s.Source;
                    if(!bindingKeys.Contains(key)&&key.Length>0){bindingKeys.Add(key);playbackSource.options.Add(new Dropdown.OptionData("未找到已保存来源 · "+(s==null?"":s.Label)));}
                    playbackSource.value=Math.Max(0,bindingKeys.IndexOf(key));playbackSource.RefreshShownValue();
                }
                playbackTarget.value=Math.Max(0,Array.IndexOf(playbackTargets,m.Target));playbackTarget.RefreshShownValue();
                profileLength.text=m.LengthScale.ToString("G",CultureInfo.InvariantCulture);profileGain.text=m.Gain.ToString("G",CultureInfo.InvariantCulture);profileFixed.isOn=m.FixedRange;
                profileLow.text=Math.Round(m.RangeStart*999).ToString();profileHigh.text=Math.Round(m.RangeEnd*999).ToString();profileRange.SetRange((float)m.RangeStart,(float)m.RangeEnd);
                MotionVector[] offsets={m.PlaneOffset,m.TargetOffset,m.RootOffset};
                for(int row=0;row<3;row++){double[] values={offsets[row].X,offsets[row].Y,offsets[row].Z};for(int col=0;col<3;col++)profileOffsets[row,col].text=values[col].ToString("G",CultureInfo.InvariantCulture);}
                playbackNotice.text=host.Profiles.Status;
            }
            finally{playbackRefreshing=false;}
        }
        private static double ProfileNumber(InputField input)
        {double value;if(!double.TryParse(input.text,NumberStyles.Float,CultureInfo.InvariantCulture,out value)||!Numbers.Finite(value))throw new ArgumentException("请输入有效数字："+input.text);return value;}
        private void ApplyProfileEditor()
        {
            profileApplied=false;
            ProfileAction(()=>{
                MeasurementProfile old=EditedMeasurement;if(old==null)throw new InvalidOperationException(host.IsMainGame?"请先进入本体动画":"请先建立或选择分段");
                var m=old.Clone();m.Target=playbackTargets[playbackTarget.value];m.LengthScale=ProfileNumber(profileLength);m.Gain=ProfileNumber(profileGain);m.FixedRange=profileFixed.isOn;m.RangeStart=ProfileNumber(profileLow)/999;m.RangeEnd=ProfileNumber(profileHigh)/999;m.AlignmentVersion=1;
                MotionVector[] offsets=new MotionVector[3];for(int row=0;row<3;row++)offsets[row]=new MotionVector(ProfileNumber(profileOffsets[row,0]),ProfileNumber(profileOffsets[row,1]),ProfileNumber(profileOffsets[row,2]));
                m.PlaneOffset=offsets[0];m.TargetOffset=offsets[1];m.RootOffset=offsets[2];m.ReverseCollider=old.ReverseCollider;m.Validate();
                if(host.IsMainGame)
                {
                    host.Profiles.Animation(editingKey).Measurement=m;
                    if(editingKey==host.Profiles.NativeKey){host.Native.Override.Value=NativeTarget.AUTO;if(nativeProfile!=null)nativeProfile.value=0;}
                }
                else
                {
                    var segment=EditedSegment;
                    // Display milliseconds while retaining the exact Timeline float when a time field was not edited.
                    double start=segmentStart.text==segment.Start.ToString("F3",CultureInfo.InvariantCulture)?segment.Start:ProfileNumber(segmentStart);
                    double end=segmentEnd.text==segment.End.ToString("F3",CultureInfo.InvariantCulture)?segment.End:ProfileNumber(segmentEnd);
                    if(start<0||end<=start)throw new ArgumentException("分段终点必须晚于起点");
                    foreach(var s in host.Profiles.Scene.Segments)if(s!=segment&&start<s.End&&end>s.Start)throw new ArgumentException("此时间范围与其他分段重叠");
                    segment.Start=start;segment.End=end;segment.Measurement=m;segment.Source=bindingKeys[playbackSource.value];segment.Label=segment.Source.Length==0?"":playbackSource.options[playbackSource.value].text;host.Profiles.Scene.Validate();
                }
                host.PauseOutput("已应用测量配置 · 同步继续等待新数据");host.Profiles.Changed(!host.IsMainGame,host.IsMainGame?editingKey:null);shownRevision=-1;RefreshPlaybackProfiles();profileApplied=true;
            });
        }
    }
}

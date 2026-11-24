using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using KoiSync.Core;
using UnityEngine;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed partial class KoiSyncPanel
    {
        private GameObject studioPage;
        private Dropdown studioSources;
        private Text studioDiscovery,studioDetails,studioValues,studioGain,studioLength,studioPlayback;
        private Toggle studioAuto;
        private string studioSignature="";
        private bool studioRefreshing;
        private GameObject studioReplacePage;
        private Text studioReplaceCard,studioReplaceCount,studioReplaceStatus;
        private Button studioReplaceButton;
        private string studioReplaceCardPath="";
        private bool studioReplaceBusy;
        private int studioReplaceDone,studioReplaceTotal;
        private void ShowStudioBindings()
        {
            if(host.StudioAdapter==null)return;
            if(studioPage!=null){ActivatePage(WorkspacePage.Studio);return;}
            RectTransform page=CreatePage(WorkspacePage.Studio);studioPage=page.gameObject;
            Label(page,"工作室 · 绑定与测量",22,16,710,36,23,Accent,FontStyle.Bold);
            Button(page,"重新扫描",700,18,120,35,host.RescanStudio);
            Button(page,"诊断绑定",830,18,144,35,ShowStudioDiagnosis);
            studioDiscovery=Label(page,"",24,60,950,36,13,Muted);
            RectTransform selection=Panel(page,20,104,958,179,Card);
            Label(selection,"选择绑定组",18,12,480,28,17,Color.white,FontStyle.Bold);
            studioAuto=Toggle(selection,"跟随镜头自动选择",650,14,270,host.StudioOptions.AutoSource.Value,v=>{if(!studioRefreshing)host.SetAutoSource(v);});
            studioSources=Dropdown(selection,18,51,920,new[]{"选择绑定组…"},index=>
            {if(studioRefreshing)return;host.Select(index>0&&index<=host.StudioAdapter.Sources.Count?host.StudioAdapter.Sources[index-1].Id:"");});
            studioDetails=Label(selection,"",18,99,920,68,12,Muted);
            RectTransform settings=Panel(page,20,298,469,275,Card),monitor=Panel(page,505,298,473,275,Card);
            Label(settings,"当前计算配置 · 只读",18,12,410,28,17,Color.white,FontStyle.Bold);
            Label(settings,"参考面、计算点、长度与范围在实验室浮动窗口中调整。",18,51,431,43,13,Muted);
            studioGain=Label(settings,"",18,99,430,24,13,Accent);
            studioLength=Label(settings,"",18,164,430,24,13,Accent);
            Button(settings,"进入工作室实验",18,219,431,35,ShowStudioExperiment);
            Label(monitor,"当前测量输入 · 0–999",18,12,435,28,17,Color.white,FontStyle.Bold);
            studioValues=Label(monitor,"",18,57,435,67,20,Accent);
            studioPlayback=Label(monitor,"",18,135,435,117,12,Muted);
            Label(page,"每个时间块独立保存来源与 3D 对齐；没有时间块时使用场景默认配置。",23,586,950,40,12,Muted);
            Button(page,"六轴详细监控",22,628,222,32,ShowAxisMonitor);
            Button(page,"查看场景 3D",257,628,222,32,()=>{host.DebugVisible=true;SetVisible(false);});
            Button(page,"进入实验室",492,628,238,32,ShowStudioExperiment);
            RefreshStudio();
        }
        private static string SourceState(string source)
        {return source==TimelineSegment.NoSource?"无（会阻止一切测量，请改回“自动”）":source.Length==0?"自动 · 镜头选择":"已指定 "+source;}
        // Binding diagnosis: the exact reason no measurement source appears, on screen and in a text file.
        private RectTransform studioDiagnosisWindow;
        private Text studioDiagnosisText;
        private void ShowStudioDiagnosis()
        {
            StudioAdapter adapter=host.StudioAdapter;if(adapter==null)return;
            adapter.RequestDiagnosis();adapter.Refresh();
            string path=System.IO.Path.Combine(host.Profiles.Root,"studio-diagnosis.txt");
            string body=adapter.Diagnosis+
                "\n场景："+(host.Profiles.ScenePath.Length==0?"未确定场景卡":System.IO.Path.GetFileName(host.Profiles.ScenePath))+" · 分段 "+host.Profiles.Scene.Segments.Count+" 个"+
                "\n当前段数据源："+SourceState(host.Profiles.StudioSource)+
                "\n场景默认数据源："+SourceState(host.Profiles.Scene.DefaultSource??"")+
                "\n测量状态："+(host.Pipeline.Status.Length==0?"等待":host.Pipeline.Status)+" · 候选 "+host.Frame.Candidates.Count+" 个 · 上次采样 "+(adapter.Sampling.Length==0?"无":adapter.Sampling)+
                "\n\n已写入："+path;
            try{if(host.Profiles.Root.Length>0)System.IO.File.WriteAllText(path,body,System.Text.Encoding.UTF8);}catch(Exception){}
            if(studioDiagnosisWindow==null)
            {
                studioDiagnosisWindow=FloatingWindow("KoiSync diagnosis","工作室绑定诊断",780,540,new Vector2(-40,10),()=>{studioDiagnosisWindow.gameObject.SetActive(false);HideIfEmpty();});
                studioDiagnosisText=Label(studioDiagnosisWindow,"",14,48,752,478,11,Color.white);
            }
            studioDiagnosisWindow.gameObject.SetActive(true);SetVisible(true);studioDiagnosisWindow.SetAsLastSibling();
            studioDiagnosisText.text=body;
        }
        private void RefreshStudio()
        {
            if(studioPage==null||!studioPage.activeInHierarchy)return;
            studioRefreshing=true;
            try
            {
                StudioAdapter adapter=host.StudioAdapter;string signature="";foreach(var source in adapter.Sources)signature+=source.Id+"|"+source.Label+";";
                if(signature!=studioSignature){studioSignature=signature;studioSources.options.Clear();studioSources.options.Add(new Dropdown.OptionData("选择绑定组…"));foreach(var source in adapter.Sources)studioSources.options.Add(new Dropdown.OptionData(source.Label));}
                studioSources.value=adapter.Sources.FindIndex(s=>s.Id==host.SelectedId)+1;studioSources.RefreshShownValue();
                var segment=host.Profiles.CurrentSegment;var preset=host.Profiles.StudioMeasurement;
                studioAuto.isOn=segment==null&&preset==null?host.StudioOptions.AutoSource.Value:host.Profiles.StudioSource.Length==0;
                studioDiscovery.text=(segment!=null?"本段数据源："+SourceState(host.Profiles.StudioSource)+" · ":"")+(host.StudioOptions.AutoSource.Value?"镜头自动选择":"手动锁定")+"\n"+adapter.Discovery;
                StudioSource selected=adapter.Find(host.SelectedId);
                studioDetails.text=selected==null?(adapter.Sources.Count>1?"存在多组关系：请选择一组后观察。":"等待有效绑定。"):
                    "来源："+selected.Source.Name+"  →  目标："+selected.Target.Name+"\n约束："+(selected.Link.Parent==null?"已移除":selected.Link.Parent.name)+" → "+(selected.Link.Child==null?"已移除":selected.Link.Child.name)+"\n"+
                    (selected.IsCollider?"DynamicBoneCollider · "+(selected.Collider==null?"已移除":selected.Collider.m_Direction+" / "+selected.Collider.m_Bound):selected.Bp?"BP 控制器 · 实际变形骨骼链":"角色原生骨骼 · 本体参考解算");
                double gain=preset==null?host.StudioOptions.Gain.Value:preset.Gain,length=preset==null?host.StudioOptions.LengthScale.Value:preset.LengthScale;
                studioGain.text="L0 运动倍率 ×"+gain.ToString("F2",CultureInfo.InvariantCulture);
                studioLength.text="有效参考长度 ×"+length.ToString("F2",CultureInfo.InvariantCulture);
                studioValues.text=raw.text;
                studioPlayback.text=selected==null?adapter.Sampling:selected.Error.Length>0?selected.Error:selected.Playback+"\n"+
                    (selected.Live?"绑定有效":"绑定失效")+" · "+(host.Pipeline.HasLiveSample?"活动采样":"暂停 / 等待播放")+"\n"+
                    "参考长度 "+host.Pipeline.NativeLength.ToString("F4",CultureInfo.InvariantCulture)+" u\n"+host.Pipeline.Status;
            }
            finally{studioRefreshing=false;}
        }

        private void ShowStudioCharacterReplacement()
        {
            if(host.StudioAdapter==null)return;
            if(studioReplacePage!=null){ActivatePage(WorkspacePage.StudioReplace);RefreshStudioCharacterReplacement();return;}
            RectTransform page=CreatePage(WorkspacePage.StudioReplace);studioReplacePage=page.gameObject;
            Label(page,"工作室 · 女性角色批量替换",22,16,760,36,23,Accent,FontStyle.Bold);
            Label(page,"选择一张女性人物卡，然后替换当前场景中的全部女性角色。场景对象编号与层级会保留。",24,66,930,44,14,Muted);
            RectTransform card=Panel(page,20,126,958,144,Card);
            Label(card,"替换人物卡",18,12,360,28,17,Color.white,FontStyle.Bold);
            Button(card,"选择人物卡…",18,52,178,38,ChooseStudioReplacementCard);
            studioReplaceCard=Label(card,"尚未选择人物卡",214,49,722,44,13,Accent);
            studioReplaceCount=Label(card,"",18,99,910,24,13,Muted);
            studioReplaceButton=Button(page,"替换全部女性角色",20,292,260,42,StartStudioCharacterReplacement);
            studioReplaceStatus=Label(page,"替换只作用于当前内存场景；检查结果后再保存为新的场景卡。",20,350,920,72,14,Muted);
            Label(page,"替换完成后 KoiSync 会重新扫描角色，并按当前场景文件重载 Timeline 与 NodeConstraints 的对象绑定。",20,438,920,58,13,Muted);
            RefreshStudioCharacterReplacement();
        }

        private void ChooseStudioReplacementCard()
        {
            try
            {
                string directory=studioReplaceCardPath.Length>0?Path.GetDirectoryName(studioReplaceCardPath):Path.Combine(Path.Combine(BepInEx.Paths.GameRootPath,"UserData"),Path.Combine("chara","female"));
                if(!Directory.Exists(directory))directory=BepInEx.Paths.GameRootPath;
                string path=FunscriptFileDialog.SelectCharacterCard(directory);
                if(path!=null){studioReplaceCardPath=Path.GetFullPath(path);studioReplaceStatus.text="已选中人物卡。替换会暂停 Timeline，完成后恢复播放位置与状态。";}
            }
            catch(Exception ex){studioReplaceStatus.text="选择人物卡失败："+ex.GetBaseException().Message;}
            RefreshStudioCharacterReplacement();
        }

        private static System.Collections.Generic.List<Studio.OCIChar> FemaleStudioCharacters(Studio.Studio studio)
        {
            var result=new System.Collections.Generic.List<Studio.OCIChar>();
            if(studio==null||studio.dicObjectCtrl==null)return result;
            foreach(var row in studio.dicObjectCtrl)
            {
                Studio.OCIChar actor=row.Value as Studio.OCIChar;
                if(actor!=null&&actor.charInfo!=null&&actor.charInfo.sex==1)result.Add(actor);
            }
            return result;
        }

        private void RefreshStudioCharacterReplacement()
        {
            if(studioReplacePage==null||!studioReplacePage.activeInHierarchy)return;
            int count=FemaleStudioCharacters(UnityEngine.Object.FindObjectOfType<Studio.Studio>()).Count;
            studioReplaceCount.text="当前女性角色："+count+" 位"+(host.Profiles.ScenePath.Length>0?" · 场景卡："+Path.GetFileName(host.Profiles.ScenePath):" · 当前场景尚未保存");
            studioReplaceCard.text=studioReplaceCardPath.Length==0?"尚未选择人物卡":Path.GetFileName(studioReplaceCardPath)+"\n"+studioReplaceCardPath;
            studioReplaceButton.interactable=!studioReplaceBusy&&count>0&&File.Exists(studioReplaceCardPath)&&File.Exists(host.Profiles.ScenePath);
            if(!studioReplaceBusy&&studioReplaceCardPath.Length==0)
                studioReplaceStatus.text="先选择女性人物卡；请确保当前场景已保存，以便重载 Timeline 和 NodeConstraints。";
        }

        private void StartStudioCharacterReplacement()
        {
            if(studioReplaceBusy)return;
            Studio.Studio studio=UnityEngine.Object.FindObjectOfType<Studio.Studio>();
            var actors=FemaleStudioCharacters(studio);
            if(actors.Count==0||!File.Exists(studioReplaceCardPath)||!File.Exists(host.Profiles.ScenePath))
            {RefreshStudioCharacterReplacement();return;}
            studioReplaceTotal=actors.Count;studioReplaceDone=0;studioReplaceBusy=true;
            host.BeginStudioMutation("替换女性角色 · 暂停采样与设备输出");
            StartCoroutine(ReplaceStudioCharacters(actors,studioReplaceCardPath,host.Profiles.ScenePath));
            RefreshStudioCharacterReplacement();
        }

        private IEnumerator ReplaceStudioCharacters(System.Collections.Generic.List<Studio.OCIChar> actors,string cardPath,string scenePath)
        {
            TimelineState.Refresh();bool timelineAvailable=TimelineState.Available,wasPlaying=TimelineState.Playing;double oldTime=TimelineState.Time;
            if(timelineAvailable)try{TimelineState.SetPlaying(false);}catch(Exception){}
            var errors=new System.Collections.Generic.List<string>();
            try
            {
                for(int i=0;i<actors.Count;i++)
                {
                    try{if(actors[i]!=null&&actors[i].charInfo!=null){actors[i].ChangeChara(cardPath);studioReplaceDone++;}}
                    catch(Exception ex){errors.Add((i+1)+"："+ex.GetBaseException().Message);if(studioReplaceDone==0)break;}
                    studioReplaceStatus.text="正在替换女性角色："+studioReplaceDone+" / "+studioReplaceTotal;
                    yield return new WaitForSecondsRealtime(.6f);
                }
                if(studioReplaceDone>0)
                {
                    for(int frame=0;frame<30;frame++)yield return null;
                    string nodeError=ReloadScenePlugin("NodesConstraints.NodesConstraints",scenePath);
                    string timelineError=ReloadScenePlugin("Timeline.Timeline",scenePath);
                    if(nodeError.Length>0)errors.Add(nodeError);if(timelineError.Length>0)errors.Add(timelineError);
                    for(int frame=0;frame<45;frame++)yield return null;
                    TimelineState.Refresh();
                    if(timelineAvailable&&TimelineState.Available)
                    {
                        try{TimelineState.Seek(oldTime);TimelineState.SetPlaying(wasPlaying);}catch(Exception ex){errors.Add("恢复 Timeline："+ex.GetBaseException().Message);}
                    }
                }
            }
            finally
            {
                studioReplaceBusy=false;
                string result="已替换 "+studioReplaceDone+" / "+studioReplaceTotal+" 位女性角色。";
                if(errors.Count>0)result+="\n部分项目需要检查："+string.Join("；",errors.ToArray());
                studioReplaceStatus.text=result;
                host.EndStudioMutation(result);
                RefreshStudioCharacterReplacement();
            }
        }

        private static string ReloadScenePlugin(string typeName,string scenePath)
        {
            try
            {
                Type type=StudioMembers.FindType(typeName);if(type==null)return "未找到 "+typeName+"，已跳过重载";
                object instance=StudioMembers.Read(type,"_self");
                MethodInfo method=type.GetMethod("OnSceneLoad",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static,null,new[]{typeof(string)},null);
                if(method==null)return typeName+" 没有 OnSceneLoad(string)";
                if(!method.IsStatic&&instance==null)return typeName+" 实例不可用";
                method.Invoke(method.IsStatic?null:instance,new object[]{scenePath});
                return "";
            }
            catch(Exception ex){return typeName+" 重载失败："+ex.GetBaseException().Message;}
        }
    }
}

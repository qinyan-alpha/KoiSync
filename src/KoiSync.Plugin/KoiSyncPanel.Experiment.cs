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
        private GameObject experimentPage;
        private InputField experimentFilter,experimentZero,experimentFull;
        private Dropdown experimentShapes;
        private Toggle experimentPreview,experimentDirection;
        private Text experimentContext,experimentSelection,experimentWeight,experimentObserved,experimentStatus,experimentCompare,experimentBp;
        private readonly List<StudioShape> experimentChoices=new List<StudioShape>();
        private string experimentSignature="",experimentEditError="";
        private bool experimentRefreshing;
        private void ShowStudioExperiment()
        {
            if(host.StudioAdapter==null)return;
            if(experimentPage!=null){ActivatePage(WorkspacePage.Experiment);RefreshExperiment();return;}
            var page=CreatePage(WorkspacePage.Experiment);experimentPage=page.gameObject;
            Label(page,"工作室实验 · 形态键与 BP 方向",22,14,575,36,23,Accent,FontStyle.Bold);
            Button(page,"3D 对齐 / 时间轨道",607,17,194,35,ShowStudioTools);
            Button(page,"选择绑定组",816,17,160,35,ShowStudioBindings);
            experimentContext=Label(page,"",23,57,950,42,13,Muted);
            var left=Panel(page,20,106,565,396,Card);var right=Panel(page,601,106,377,396,Card);
            Label(left,"手动选择形态键",18,12,510,27,17,Color.white,FontStyle.Bold);
            experimentFilter=Input(left,18,48,382,host.Experiment.Filter);
            experimentFilter.onValueChanged.AddListener(value=>{host.Experiment.Filter=value;experimentSignature="";});
            Button(left,"显示全部",412,48,134,35,()=>{experimentFilter.text="";});
            experimentShapes=Dropdown(left,18,94,529,new[]{"选择形态键…"},i=>
            {if(experimentRefreshing)return;host.PauseOutput("已切换实验形态键");host.Experiment.Select(i>0&&i<=experimentChoices.Count?experimentChoices[i-1]:null);experimentSignature="";});
            experimentSelection=Label(left,"",18,137,530,46,12,Muted);
            experimentWeight=Label(left,"权重 —  →  L0 —",18,188,530,31,21,Accent);
            experimentObserved=Label(left,"",18,226,530,34,12,Muted);
            Label(left,"L0 000 对应权重",18,267,178,22,12,Muted);Label(left,"L0 999 对应权重",207,267,178,22,12,Muted);
            experimentZero=Input(left,18,291,174,host.Experiment.Zero.Value.ToString(CultureInfo.InvariantCulture));
            experimentFull=Input(left,207,291,174,host.Experiment.Full.Value.ToString(CultureInfo.InvariantCulture));
            Button(left,"应用端点",395,291,151,35,ApplyExperimentEndpoints);
            Button(left,"当前权重 → 000",18,345,174,33,()=>CaptureExperimentEndpoint(false));
            Button(left,"当前权重 → 999",207,345,174,33,()=>CaptureExperimentEndpoint(true));
            Button(left,"重置观察",395,345,151,33,()=>host.Experiment.ResetObservation());
            Label(right,"BP 自动目标 · 只读",18,12,340,27,17,Color.white,FontStyle.Bold);
            experimentBp=Label(right,"",18,51,342,263,13,Muted);
            experimentDirection=Toggle(right,"场景显示 BP 方向（粉色）",18,323,340,host.Experiment.ShowDirection,v=>{if(!experimentRefreshing)host.Experiment.ShowDirection=v;});
            Button(right,"查看场景方向",18,356,340,30,()=>{host.DebugVisible=true;SetVisible(false);});
            var bottom=Panel(page,20,516,958,145,Card);
            experimentPreview=Toggle(bottom,"使用形态键绝对 L0 预览（设备输出停用）",18,12,700,false,v=>
            {if(experimentRefreshing)return;host.PauseOutput("已切换形态键实验预览");host.Experiment.AbsolutePreview=v;});
            Button(bottom,"六轴监控",787,11,151,33,ShowAxisMonitor);
            experimentCompare=Label(bottom,"",18,49,921,29,17,Accent);
            experimentStatus=Label(bottom,"",18,85,921,49,12,Muted);
            RefreshExperiment();
        }
        private void CaptureExperimentEndpoint(bool full)
        {
            double weight=host.Experiment.Weight;if(!Numbers.Finite(weight)){experimentEditError="请先选择有效形态键";return;}
            (full?experimentFull:experimentZero).text=weight.ToString("G9",CultureInfo.InvariantCulture);ApplyExperimentEndpoints();
        }
        private void ApplyExperimentEndpoints()
        {
            float zero,full;
            if(!float.TryParse(experimentZero.text,NumberStyles.Float,CultureInfo.InvariantCulture,out zero)||!float.TryParse(experimentFull.text,NumberStyles.Float,CultureInfo.InvariantCulture,out full)||!Numbers.Finite(ShapeReference.Normalize(0,zero,full)))
            {experimentEditError="端点未应用：请输入两个不同的有限数值";return;}
            host.PauseOutput("已更改形态键映射端点");host.Experiment.Zero.Value=zero;host.Experiment.Full.Value=full;experimentEditError="";
        }
        private static string ExperimentNumber(double value,string format){return Numbers.Finite(value)?value.ToString(format,CultureInfo.InvariantCulture):"—";}
        private void RefreshExperiment()
        {
            if(experimentPage==null||!experimentPage.activeInHierarchy)return;
            experimentRefreshing=true;
            try
            {
                StudioExperiment e=host.Experiment;var choices=new List<StudioShape>();string signature=e.Context+"|"+(e.Selected==null?"":e.Selected.Id)+"|"+e.Filter;
                foreach(var shape in e.Shapes)if(e.Filter.Length==0||shape.Name.IndexOf(e.Filter,StringComparison.OrdinalIgnoreCase)>=0||shape.Label.IndexOf(e.Filter,StringComparison.OrdinalIgnoreCase)>=0)
                {choices.Add(shape);signature+="|"+shape.Id;}
                if(signature!=experimentSignature)
                {
                    experimentSignature=signature;experimentChoices.Clear();experimentChoices.AddRange(choices);experimentShapes.options.Clear();
                    experimentShapes.options.Add(new Dropdown.OptionData(choices.Count==0?"没有匹配项 · 可清空筛选":"选择形态键…"));
                    foreach(var shape in choices)experimentShapes.options.Add(new Dropdown.OptionData(shape.Label));
                    experimentShapes.value=e.Selected==null?0:choices.FindIndex(s=>s.Id==e.Selected.Id)+1;experimentShapes.RefreshShownValue();
                }
                experimentContext.text=e.Context+"\n当前绑定两侧共 "+e.Shapes.Count+" 个形态键；筛选匹配 "+choices.Count+" 个。只读取权重，不修改模型。";
                experimentSelection.text=e.Selected==null?"未选择。名称由模型决定；不会把 open 自动当作 penetrate。":e.Selected.Label+"\n网格："+(e.Selected.Mesh==null?"已移除":e.Selected.Mesh.name)+" · 索引 "+e.Selected.Index;
                experimentWeight.text="权重 "+ExperimentNumber(e.Weight,"F3")+"  →  L0 "+ChannelMonitor.PositionText(e.Absolute);
                experimentObserved.text="本次观察 "+ExperimentNumber(e.ObservedMin,"F3")+" ～ "+ExperimentNumber(e.ObservedMax,"F3")+" · "+e.Samples+" 次采样\n映射：(权重 − "+e.Zero.Value.ToString("G5")+") / ("+e.Full.Value.ToString("G5")+" − "+e.Zero.Value.ToString("G5")+")";
                var bp=e.Bp;
                experimentBp.text="Auto-Target = "+bp.AutoTarget+"\n入口父节点："+bp.EntryName+"\n内部父节点："+bp.EndName+"\n\n"+
                    "整链与通道夹角："+(bp.Valid?ExperimentNumber(bp.Deviation,"F2"):"—")+"°\n末段与通道夹角："+(bp.Valid?ExperimentNumber(bp.TipDeviation,"F2"):"—")+"°\n局部偏航偏差："+(bp.Valid?ExperimentNumber(bp.Yaw,"F2"):"—")+"°\n局部俯仰偏差："+(bp.Valid?ExperimentNumber(bp.Pitch,"F2"):"—")+"°\n\n"+bp.Status;
                experimentPreview.isOn=e.AbsolutePreview;experimentPreview.interactable=e.AbsolutePreview||Numbers.Finite(e.Absolute)&&e.Skeletal!=null;
                experimentDirection.isOn=e.ShowDirection;
                experimentCompare.text="骨骼 L0 "+(e.Skeletal==null?"—":ChannelMonitor.PositionText(e.Skeletal.Normalized[0]))+"    形态键 L0 "+ChannelMonitor.PositionText(e.Absolute)+"    当前模式："+(e.AbsolutePreview?"绝对值预览":"骨骼测量 / 形态键观察");
                experimentStatus.text=(experimentEditError.Length>0?experimentEditError:e.Status)+"\n绝对值预览不使用自动居中或 L0 倍率；保留限幅、平滑和播放状态。BP 方向偏差独立显示，R0–R2 保持原算法。";
            }
            finally{experimentRefreshing=false;}
        }
    }
}

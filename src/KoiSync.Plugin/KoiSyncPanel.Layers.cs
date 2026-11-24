using System;
using System.Globalization;
using KoiSync.Core;
using UnityEngine;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed partial class KoiSyncPanel
    {
        private readonly Toggle[] layerEyes=new Toggle[5];
        private readonly Button[] layerRows=new Button[5];
        private InputField planeWidth,planeDepth,planeLateral;
        private bool layersRefreshing;
        // Per-animation axis ranges live in the collapsible block of the 3D alignment window.
        private RectTransform alignmentRangeRoot,alignmentFooter;
        private Button alignmentRangeButton;
        private readonly RangeSlider[] alignmentAxisRanges=new RangeSlider[6];
        private readonly InputField[] alignmentAxisLow=new InputField[6],alignmentAxisHigh=new InputField[6];
        private Toggle alignmentRangeFixed;
        private RangeSlider alignmentFixedRange;
        private Text alignmentFixedValue;
        private bool alignmentRangesLoading;
        private float alignmentTop;
        private void BuildPlaneControls(RectTransform p,float y)
        {
            // 面宽 / 面深 / L1-2 keep their own spacing: the three labels and fields used to overlap.
            Label(p,"面宽",186,y+6,44,24,12,Muted);planeWidth=Input(p,232,y,62,"0");
            Label(p,"面深",312,y+6,44,24,12,Muted);planeDepth=Input(p,358,y,62,"0");
            Label(p,"L1/2 ×",438,y+6,66,24,12,Muted);planeLateral=Input(p,506,y,50,"2");
            foreach(var input in new[]{planeWidth,planeDepth,planeLateral})input.onEndEdit.AddListener(v=>{if(alignmentRefreshing)return;LabAction(()=>{var m=alignmentDraft.Clone();m.PlaneWidth=ProfileNumber(planeWidth);m.PlaneDepth=ProfileNumber(planeDepth);m.LateralSpan=ProfileNumber(planeLateral);m.Validate();alignmentDraft=m;alignmentGizmo.Draft=m;SaveAlignment(false);});});
        }
        private void ReadPlaneControls()
        {if(planeWidth==null||alignmentDraft==null)return;planeWidth.text=alignmentDraft.PlaneWidth.ToString("G",CultureInfo.InvariantCulture);planeDepth.text=alignmentDraft.PlaneDepth.ToString("G",CultureInfo.InvariantCulture);planeLateral.text=alignmentDraft.LateralSpan.ToString("G",CultureInfo.InvariantCulture);RefreshLayers();}
        private void BuildLayers(RectTransform p,float top)
        {
            var names=new[]{"参考面","根点 A","末端 E","目标 B","交互参考线"};
            for(int i=0;i<5;i++)
            {
                int part=i;layerRows[i]=Button(p,names[i],14,top+i*39,133,32,()=>alignmentGizmo.Choose((AlignmentPart)part));
                layerEyes[i]=Toggle(p,"",156,top+7+i*39,28,true,v=>{if(layersRefreshing||alignmentDraft==null)return;alignmentGizmo.SetObjectVisible((AlignmentPart)part,v);});
            }
        }
        private void RefreshLayers()
        {
            if(layerRows[0]==null||alignmentDraft==null)return;layersRefreshing=true;
            for(int i=0;i<5;i++){layerEyes[i].isOn=(alignmentDraft.HiddenObjects&(1<<i))==0;layerRows[i].GetComponent<Image>().color=(int)alignmentGizmo.Part==i?new Color32(38,93,91,255):new Color32(43,62,80,255);}
            layersRefreshing=false;
        }
        // 更多轴范围配置: the six axes of this one animation, plus the L0 insertion range. These values are
        // stored in the animation profile and win over the global six-value configuration.
        private void BuildAlignmentRangeControls(RectTransform p,float top)
        {
            alignmentRangeRoot=Rect(p,"Axis ranges",14,top,542,304);
            Label(alignmentRangeRoot,"插入范围与六轴范围 · 只影响当前动画，默认 000 — 999 表示跟随全局",0,0,542,24,12,Accent);
            alignmentRangeFixed=Toggle(alignmentRangeRoot,"使用插入范围",0,30,140,false,v=>{if(alignmentRangesLoading||alignmentDraft==null)return;alignmentDraft.FixedRange=v;LabAction(()=>SaveAlignment(false));});
            alignmentFixedValue=ValueLabel(alignmentRangeRoot,410,28,132);
            alignmentFixedRange=Range(alignmentRangeRoot,150,36,250,0,1,(lo,hi)=>{if(alignmentRangesLoading||alignmentDraft==null)return;alignmentDraft.RangeStart=lo;alignmentDraft.RangeEnd=Math.Max(lo,hi);alignmentFixedValue.text=Mathf.RoundToInt(lo*999)+" — "+Mathf.RoundToInt(hi*999);});
            alignmentFixedRange.Finished=()=>LabAction(()=>SaveAlignment(false));
            for(int i=0;i<6;i++)
            {
                int axis=i;float y=70+i*38;
                Label(alignmentRangeRoot,MotionPipeline.Axes[i],0,y+6,40,24,13,Color.white);
                alignmentAxisLow[i]=Input(alignmentRangeRoot,42,y,60,"0");
                alignmentAxisHigh[i]=Input(alignmentRangeRoot,108,y,60,"999");
                alignmentAxisRanges[i]=Range(alignmentRangeRoot,178,y+3,240,0,1,(lo,hi)=>{
                    if(alignmentRangesLoading||alignmentDraft==null)return;
                    alignmentAxisLow[axis].text=Mathf.RoundToInt(lo*999).ToString();alignmentAxisHigh[axis].text=Mathf.RoundToInt(hi*999).ToString();
                    alignmentDraft.ChannelMinimum[axis]=lo;alignmentDraft.ChannelMaximum[axis]=hi;
                });
                alignmentAxisRanges[i].Finished=()=>LabAction(()=>SaveAlignment(false));
                alignmentAxisLow[i].onEndEdit.AddListener(v=>SyncAlignmentAxis(axis));
                alignmentAxisHigh[i].onEndEdit.AddListener(v=>SyncAlignmentAxis(axis));
            }
        }
        private void SyncAlignmentAxis(int axis)
        {
            if(alignmentRangesLoading||alignmentDraft==null)return;
            int lo,hi;
            if(!int.TryParse(alignmentAxisLow[axis].text,out lo)||!int.TryParse(alignmentAxisHigh[axis].text,out hi)||lo<0||hi>999||lo>hi)return;
            alignmentRangesLoading=true;alignmentAxisRanges[axis].SetRange(lo/999f,hi/999f);alignmentRangesLoading=false;
            alignmentDraft.ChannelMinimum[axis]=lo/999.0;alignmentDraft.ChannelMaximum[axis]=hi/999.0;
            LabAction(()=>SaveAlignment(false));
        }
        private void ReadAlignmentRanges()
        {
            if(alignmentRangeRoot==null||alignmentDraft==null)return;
            alignmentRangesLoading=true;
            try
            {
                alignmentRangeFixed.isOn=alignmentDraft.FixedRange;
                alignmentFixedRange.SetRange((float)alignmentDraft.RangeStart,(float)alignmentDraft.RangeEnd);
                alignmentFixedValue.text=Mathf.RoundToInt((float)(alignmentDraft.RangeStart*999))+" — "+Mathf.RoundToInt((float)(alignmentDraft.RangeEnd*999));
                for(int i=0;i<6;i++)
                {
                    double lo=alignmentDraft.ChannelMinimum[i],hi=alignmentDraft.ChannelMaximum[i];
                    alignmentAxisLow[i].text=Mathf.RoundToInt((float)(lo*999)).ToString();alignmentAxisHigh[i].text=Mathf.RoundToInt((float)(hi*999)).ToString();
                    alignmentAxisRanges[i].SetRange((float)lo,(float)hi);
                }
            }
            finally{alignmentRangesLoading=false;}
        }
        private void ToggleAlignmentRanges(){SetAlignmentExpanded(alignmentRangeRoot==null||!alignmentRangeRoot.gameObject.activeSelf);}
        private void SetAlignmentExpanded(bool expanded)
        {
            if(alignmentRangeRoot!=null)alignmentRangeRoot.gameObject.SetActive(expanded);
            if(alignmentRangeButton!=null)alignmentRangeButton.GetComponentInChildren<Text>().text=expanded?"更多轴范围配置 ▴":"更多轴范围配置 ▾";
            if(alignmentFooter!=null)alignmentFooter.anchoredPosition=new Vector2(0,-(expanded?alignmentTop+584:alignmentTop+266));
            if(alignmentWindow!=null)
            {
                float height=alignmentTop+(expanded?648:330),delta=height-alignmentWindow.sizeDelta.y;
                alignmentWindow.sizeDelta=new Vector2(570,height);
                alignmentWindow.anchoredPosition=new Vector2(alignmentWindow.anchoredPosition.x,alignmentWindow.anchoredPosition.y-delta*.5f);
            }
        }
    }
}

using KoiSync.Core;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace KoiSync.Plugin
{
    public sealed partial class KoiSyncPanel
    {
        private GameObject skeletonPage;
        private SkeletonInspector inspector;
        private Text skeletonCaption,skeletonValues,skeletonRanges,skeletonCalibration;
        private void ShowSkeletonInspection()
        {
            if(skeletonPage!=null){ActivatePage(WorkspacePage.Skeleton);return;}
            RectTransform modal=CreatePage(WorkspacePage.Skeleton);skeletonPage=modal.gameObject;
            Label(modal,"全身骨架 · 实时姿态检查",22,16,850,36,23,Accent,FontStyle.Bold);
            Button(modal,"六轴详细值",758,18,153,36,()=>TogglePage(WorkspacePage.Monitor,ShowAxisMonitor));
            skeletonCaption=Label(modal,"读取当前角色骨架…",24,58,865,24,13,Muted);
            RectTransform controls=Panel(modal,20,97,297,488,Card),visual=Panel(modal,333,97,645,488,Card);
            skeletonValues=Label(controls,"",15,14,268,121,13,Accent);
            skeletonRanges=Label(controls,"",15,147,268,155,11,Color.white);
            skeletonCalibration=Label(controls,"",15,317,268,87,12,Muted);
            Dropdown projection=Dropdown(controls,15,417,159,new[]{"主轴 · 局部 Z","主轴 · 局部 Y","主轴 · 局部 X"},i=>{if((int)host.Axis!=i){host.PauseOutput("已更改投影轴");host.Axis=(StrokeAxis)i;}});projection.value=(int)host.Axis;
            projection.gameObject.SetActive(host.Demo);
            if(!host.Demo)Label(controls,"实际参考轴 · 自动",15,421,160,25,12,Muted);
            Button(controls,"清空观察",186,417,97,34,()=>inspector.ClearObservation());
            Label(controls,"原始距离 ≠ 归一化行程",15,457,270,24,12,Muted);
            Label(visual,"当前骨骼姿态",14,8,200,26,16,Color.white,FontStyle.Bold);
            Label(visual,"拖动旋转 · 滚轮缩放",365,12,266,22,12,Muted);
            RectTransform imageRect=Rect(visual,"Live full-body skeleton",6,37,633,414);
            RawImage picture=imageRect.gameObject.AddComponent<RawImage>();picture.raycastTarget=true;
            inspector=modal.gameObject.AddComponent<SkeletonInspector>();inspector.Initialize(host);picture.texture=inspector.Texture;
            imageRect.gameObject.AddComponent<SkeletonOrbit>().Inspector=inspector;
            Label(visual,"蓝色：参考角色    橙色：目标角色    白 A / 粉 B：测量点",14,455,618,22,11,Muted);
            Label(modal,"关节读取当前动画后的真实位置；头部圆环为示意。u 为世界单位，非毫米。",23,594,949,24,12,Muted);
            Button(modal,"全身取景",22,624,147,32,inspector.FrameBody);
            Button(modal,"聚焦测量点",182,624,159,32,inspector.FocusMeasurement);
            Button(modal,"同场景方向",354,624,159,32,inspector.MatchSceneView);
            Button(modal,"＋ 放大",526,624,112,32,()=>inspector.Zoom(.8f));
            Button(modal,"－ 缩小",651,624,112,32,()=>inspector.Zoom(1.25f));
            Toggle(modal,"显示测量轴",784,627,192,true,inspector.SetMeasurementVisible);
            RefreshSkeleton();
        }
        internal void SampleSkeleton(){if(inspector!=null&&inspector.gameObject.activeInHierarchy)inspector.Sample();}
        private void RefreshSkeleton()
        {
            if(inspector==null||!inspector.gameObject.activeInHierarchy)return;
            skeletonCaption.text=inspector.Caption+(TimelineState.HasActorTracks?" · "+TimelineState.Description:"");
            skeletonValues.text=inspector.Readings;skeletonRanges.text=inspector.RangeReadings;skeletonCalibration.text=inspector.Calibration;
        }
    }
    public sealed class SkeletonOrbit:MonoBehaviour,IDragHandler,IScrollHandler
    {
        public SkeletonInspector Inspector;
        public void OnDrag(PointerEventData e){Inspector.Rotate(e.delta.x*.3f,-e.delta.y*.3f);}
        public void OnScroll(PointerEventData e){Inspector.Zoom(Mathf.Pow(.85f,e.scrollDelta.y));}
    }
}

using System;
using UnityEngine;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed partial class KoiSyncPanel
    {
        private GameObject geometryPage;
        private Text contactModelStatus;
        private void Show3DSettings()
        {
            if(geometryPage!=null){ActivatePage(WorkspacePage.Geometry);return;}
            RectTransform modal=CreatePage(WorkspacePage.Geometry);geometryPage=modal.gameObject;
            Label(modal,"3D 显示设置",22,16,720,36,23,Accent,FontStyle.Bold);
            Label(modal,"参考点 A → 目标点 B · 世界空间线框",24,55,840,21,13,Muted);
            contactModelStatus=Label(modal,host.ContactPreviewStatus,24,77,947,18,11,Muted);
            RectTransform controls=Panel(modal,20,97,319,488,Card);
            RectTransform visual=Panel(modal,355,97,623,488,Card);
            Label(visual,"几何预览",17,11,200,28,17,Color.white,FontStyle.Bold);
            Label(visual,"独立数学样例 · 可旋转视角",262,16,345,22,12,Muted);
            RectTransform imageRect=Rect(visual,"World-space mesh preview",12,49,599,361);
            RawImage picture=imageRect.gameObject.AddComponent<RawImage>();picture.raycastTarget=false;
            GeometryPreview preview=modal.gameObject.AddComponent<GeometryPreview>();preview.Initialize(host.ViewSettings);picture.texture=preview.Texture;
            Button(visual,"← 旋转",16,425,99,34,()=>preview.Orbit(-20));Button(visual,"旋转 →",126,425,99,34,()=>preview.Orbit(20));
            Button(visual,"俯视",236,425,99,34,()=>preview.Tilt(15));Button(visual,"仰视",346,425,99,34,()=>preview.Tilt(-15));
            Button(visual,"复位视角",456,425,150,34,preview.ResetView);
            Label(visual,"白色 A / 粉色 B · 青 L0/R0 · 橙 L1/R1 · 紫 L2/R2",17,463,589,22,11,Muted);
            DebugViewSettings view=host.ViewSettings;
            Toggle wireToggle=Toggle(controls,"显示 3D 线框（全局，F8 临时切换）",17,18,288,view.ShowWireframe,v=>view.ShowWireframe=v);
            Toggle axesToggle=Toggle(controls,"参考 / 目标坐标轴",17,52,288,view.Axes,v=>view.Axes=v);
            Toggle planeToggle=Toggle(controls,"参考平面网格",17,86,288,view.Plane,v=>view.Plane=v);
            Toggle boundsToggle=Toggle(controls,"三轴投影线框盒",17,120,288,view.Bounds,v=>view.Bounds=v);
            Toggle arcsToggle=Toggle(controls,"相对旋转角度弧",17,154,288,view.Arcs,v=>view.Arcs=v);
            Toggle labelsToggle=Toggle(controls,"场景中的节点标签",17,188,288,view.Labels,v=>view.Labels=v);
            Toggle xrayToggle=Toggle(controls,"透视显示（穿过遮挡）",17,222,288,view.XRay,v=>view.XRay=v);
            Toggle contactToggle=Toggle(controls,"SR6 驱动飞机杯 + 实体杆",17,256,288,view.ShowContactModel,v=>view.ShowContactModel=v);
            Toggle visibleLengthToggle=Toggle(controls,"预览杆长跟随 BP 可见链",17,288,288,view.FollowVisibleRodLength,v=>view.FollowVisibleRodLength=v);
            Slider size=DisplaySlider(controls,"辅助图形大小",324,.5f,3,view.Scale,v=>view.Scale=v,"F2"," ×");
            Slider width=DisplaySlider(controls,"线框宽度",378,.0005f,.006f,view.Width,v=>view.Width=v,"F4"," u");
            Slider extent=DisplaySlider(controls,"平面半宽 / 参考线长度",432,.02f,.2f,view.Extent,v=>view.Extent=v,"F3"," u");
            Text feedback=Label(modal,"修改即时预览；保存后作为下次启动的显示设置。",23,593,947,23,12,Muted);
            Action refresh=()=>{wireToggle.isOn=view.ShowWireframe;axesToggle.isOn=view.Axes;planeToggle.isOn=view.Plane;boundsToggle.isOn=view.Bounds;arcsToggle.isOn=view.Arcs;labelsToggle.isOn=view.Labels;xrayToggle.isOn=view.XRay;contactToggle.isOn=view.ShowContactModel;visibleLengthToggle.isOn=view.FollowVisibleRodLength;size.value=view.Scale;width.value=view.Width;extent.value=view.Extent;preview.Render();};
            Button(modal,"保存显示设置",22,624,171,32,()=>{try{view.Save(host.Config);feedback.text="已保存到 BepInEx 配置中的 3D Display。";}catch(Exception ex){feedback.text="保存失败："+ex.Message;}});
            Button(modal,"读取已保存",205,624,171,32,()=>{try{host.Config.Reload();view.Load(host.Config);refresh();feedback.text="已读取保存的显示设置。";}catch(Exception ex){feedback.text="读取失败："+ex.Message;}});
            Button(modal,"恢复默认",388,624,153,32,()=>{view.Reset();refresh();feedback.text="已恢复默认显示；点击保存可保留。";});
            Button(modal,"全身骨架检查",553,624,180,32,ShowSkeletonInspection);
            Button(modal,"查看当前场景  ↗",746,624,230,32,()=>{host.DebugVisible=true;SetVisible(false);});
        }
        private Slider DisplaySlider(Transform parent,string caption,float y,float lo,float hi,float value,Action<float> changed,string format,string suffix)
        {
            Text label=Label(parent,"",17,y,289,24,12,Color.white);
            Slider slider=Slider(parent,18,y+33,280,0,v=>{});slider.minValue=lo;slider.maxValue=hi;slider.value=value;
            Action<float> update=v=>{label.text=caption+"  "+v.ToString(format)+suffix;changed(v);};update(value);
            slider.onValueChanged.AddListener(v=>update(v));return slider;
        }
    }
}

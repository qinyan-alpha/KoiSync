using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed partial class KoiSyncPanel
    {
        private bool vrWorld;
        private VrFrame vrFrame;
        private VrPanelInput vrInput;
        private BaseInput savedInput;
        private Canvas vrCanvas;
        private Image vrPointer;
        private GameObject vrPage;
        private Text vrStatus,vrShortcuts,vrDistance,vrWidth;
        private Toggle vrEnabled;
        private Dropdown vrCombo;
        private Slider vrDistanceSlider,vrWidthSlider;
        private bool vrRefreshing;
        private static readonly FieldInfo InputOverride=typeof(BaseInputModule).GetField("m_InputOverride",BindingFlags.Instance|BindingFlags.NonPublic);
        internal void UpdateVr(VrFrame frame,bool recenter)
        {
            vrFrame=frame;bool active=frame!=null&&frame.Active;
            if(vrCanvas==null)vrCanvas=canvasObject.GetComponent<Canvas>();
            if(active)
            {
                if(!vrWorld)
                {
                    DismissDropdowns();vrWorld=true;canvasObject.GetComponent<CanvasScaler>().enabled=false;
                    vrCanvas.renderMode=RenderMode.WorldSpace;
                    var root=(RectTransform)canvasObject.transform;root.sizeDelta=new Vector2(1500,950);root.pivot=new Vector2(.5f,.5f);vrCanvas.scaleFactor=1;
                    SetVrLayer(canvasObject.transform);recenter=true;
                }
                if(vrCanvas.worldCamera!=frame.Camera){vrCanvas.worldCamera=frame.Camera;recenter=true;}
                if(recenter)RecenterVr();
                if(Visible)
                {
                    EnsureEventSystem();var module=eventObject.GetComponent<StandaloneInputModule>();
                    if(vrInput==null){vrInput=eventObject.AddComponent<VrPanelInput>();vrInput.ResetPointer();}
                    if(!ReferenceEquals(InputOverride.GetValue(module),vrInput)){savedInput=InputOverride.GetValue(module) as BaseInput;InputOverride.SetValue(module,vrInput);vrInput.ResetPointer();}
                    vrInput.Frame=frame;vrInput.Workspace=canvasObject.GetComponent<RectTransform>();vrInput.Sample();
                    if(vrPointer==null){var r=Rect(canvasObject.transform,"VR pointer",0,0,11,11);r.pivot=new Vector2(.5f,.5f);vrPointer=Image(r,Accent,true);vrPointer.raycastTarget=false;var pointerCanvas=r.gameObject.AddComponent<Canvas>();pointerCanvas.overrideSorting=true;pointerCanvas.sortingOrder=32010;}
                    vrPointer.gameObject.SetActive(vrInput.Hit);if(vrInput.Hit)vrPointer.transform.position=vrInput.HitPoint-vrCanvas.transform.forward*.0003f;
                    vrPointer.transform.SetAsLastSibling();
                }
                else ReleaseVrInput();
            }
            else if(vrWorld)
            {
                DismissDropdowns();ReleaseVrInput();vrWorld=false;vrCanvas.worldCamera=null;vrCanvas.renderMode=RenderMode.ScreenSpaceOverlay;
                canvasObject.transform.localScale=Vector3.one;canvasObject.transform.rotation=Quaternion.identity;canvasObject.transform.position=Vector3.zero;
                canvasObject.GetComponent<CanvasScaler>().enabled=true;
            }
            if(vrStatus!=null&&vrPage.activeInHierarchy){vrStatus.text=(frame==null?"未检测到活动 VR 相机":frame.Status)+"\n"+(vrWorld?"面板已使用世界空间显示":"当前使用普通屏幕面板");RefreshVrSettings();}
        }
        private static void SetVrLayer(Transform root){root.gameObject.layer=0;foreach(Transform child in root)SetVrLayer(child);}
        private void ReleaseVrInput()
        {
            if(vrInput!=null)
            {
                var module=vrInput.GetComponent<StandaloneInputModule>();
                if(module!=null&&ReferenceEquals(InputOverride.GetValue(module),vrInput))InputOverride.SetValue(module,savedInput);
                savedInput=null;vrInput.ResetPointer();
            }
            if(vrPointer!=null)vrPointer.gameObject.SetActive(false);
        }
        internal void RecenterVr()
        {
            if(!vrWorld||vrFrame==null||!vrFrame.Active)return;
            Transform head=vrFrame.Head;float scale=Mathf.Max(.0001f,Mathf.Abs(head.lossyScale.x));
            canvasObject.transform.position=head.position+head.forward*host.VrOptions.Distance.Value*scale;
            canvasObject.transform.rotation=Quaternion.LookRotation(head.forward,head.up);
            canvasObject.transform.localScale=Vector3.one*(host.VrOptions.Width.Value/1200f*scale);
            window.GetComponent<RectTransform>().anchoredPosition=Vector2.zero;
        }
        private void ShowVrSettings()
        {
            if(vrPage!=null){ActivatePage(WorkspacePage.VR);return;}
            var page=CreatePage(WorkspacePage.VR);vrPage=page.gameObject;
            Label(page,"VR · 面板与打开方式",22,17,940,36,23,Accent,FontStyle.Bold);
            var settings=host.VrOptions;
            vrStatus=Label(page,"等待 VR 状态",23,67,945,57,14,Muted);
            var card=Panel(page,20,143,958,470,Card);
            vrEnabled=Toggle(card,"在活动 VR 环境使用空间面板",22,20,890,settings.Enabled.Value,v=>{if(!vrRefreshing)settings.Enabled.Value=v;});
            vrShortcuts=Label(card,"打开 / 收起："+settings.ToggleKey.Value+"\n移回视野前方："+settings.RecenterKey.Value+"\n普通面板快捷键："+host.PanelShortcut+"（继续可用）",22,65,906,94,18,Accent);
            Label(card,"手柄组合键 · 持续按住 "+settings.HoldSeconds.Value.ToString("F1")+" 秒，每次触发后需松开",22,176,900,26,14,Muted);
            vrCombo=Dropdown(card,22,214,906,new[]{"仅使用键盘组合键","同时按住左右握持键","同时按住左右菜单键"},i=>{if(!vrRefreshing)settings.ControllerShortcut.Value=(VrControllerShortcut)i;});vrCombo.value=(int)settings.ControllerShortcut.Value;
            vrDistance=Label(card,"面板距离 "+settings.Distance.Value.ToString("F2"),22,266,900,25,14,Accent);
            vrDistanceSlider=Slider(card,22,302,906,(settings.Distance.Value-.5f)/2.5f,v=>{if(vrRefreshing)return;settings.Distance.Value=.5f+v*2.5f;vrDistance.text="面板距离 "+settings.Distance.Value.ToString("F2");RecenterVr();});
            vrWidth=Label(card,"面板宽度 "+settings.Width.Value.ToString("F2"),22,337,900,25,14,Accent);
            vrWidthSlider=Slider(card,22,373,906,(settings.Width.Value-.6f)/1.8f,v=>{if(vrRefreshing)return;settings.Width.Value=.6f+v*1.8f;vrWidth.text="面板宽度 "+settings.Width.Value.ToString("F2");RecenterVr();});
            Button(card,"移到视野前方",22,414,242,35,RecenterVr);
            Label(card,"手柄指向 + 扳机点击；摇杆上下滚动。无手柄时用头部指向 + 空格。",282,419,646,34,12,Muted);
            Label(page,"kPlugVR 请使用官方 KoikatuVR.exe 启动。面板位置在打开时确定，Ctrl + Shift + F7 可将它移回视野。",23,629,949,34,12,Muted);
        }
        private void RefreshVrSettings()
        {
            if(vrWidthSlider==null)return;vrRefreshing=true;
            try
            {
                var s=host.VrOptions;vrEnabled.isOn=s.Enabled.Value;vrCombo.value=(int)s.ControllerShortcut.Value;vrCombo.RefreshShownValue();
                vrDistanceSlider.value=(s.Distance.Value-.5f)/2.5f;vrWidthSlider.value=(s.Width.Value-.6f)/1.8f;
                vrDistance.text="面板距离 "+s.Distance.Value.ToString("F2");vrWidth.text="面板宽度 "+s.Width.Value.ToString("F2");
                vrShortcuts.text="打开 / 收起："+s.ToggleKey.Value+"\n移回视野前方："+s.RecenterKey.Value+"\n普通面板快捷键："+host.PanelShortcut+"（继续可用）";
            }
            finally{vrRefreshing=false;}
        }
    }
}

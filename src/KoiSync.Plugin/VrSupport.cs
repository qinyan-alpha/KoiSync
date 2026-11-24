using System;
using System.Collections;
using System.Reflection;
using BepInEx.Configuration;
using KoiSync.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KoiSync.Plugin
{
    internal enum VrControllerShortcut{Disabled,BothGrip,BothMenu}
    // A VR shortcut is stored as one hand plus one controller button bit, for example "B:4" (both grips)
    // or "R:33" (right trigger). None disables the binding.
    internal static class VrButton
    {
        internal static bool TryParse(string token,out char hand,out int bit)
        {
            hand='B';bit=-1;
            if(string.IsNullOrEmpty(token)||token=="None")return false;
            int colon=token.IndexOf(':');
            if(colon<1)return false;
            hand=char.ToUpperInvariant(token[0]);
            return (hand=='L'||hand=='R'||hand=='B')&&int.TryParse(token.Substring(colon+1),out bit)&&bit>=0&&bit<64;
        }
        internal static string Name(int bit)
        {
            switch(bit){case 0:return"系统键";case 1:return"菜单键";case 2:return"握持键";case 32:return"摇杆";case 33:return"扳机";default:return"按键"+bit;}
        }
        internal static string Text(string token)
        {
            char hand;int bit;
            if(!TryParse(token,out hand,out bit))return "未设置";
            return (hand=='L'?"左":hand=='R'?"右":"双侧")+Name(bit);
        }
        // The VR page still offers the two classic chords; they mirror into the same token.
        internal static string ForShortcut(VrControllerShortcut shortcut)
        {return shortcut==VrControllerShortcut.BothGrip?"B:4":shortcut==VrControllerShortcut.BothMenu?"B:2":"None";}
        internal static string Effective(VrSettings settings)
        {
            if(settings.ControllerShortcut.Value!=VrControllerShortcut.Disabled)return ForShortcut(settings.ControllerShortcut.Value);
            return settings.PanelButton.Value;
        }
    }
    internal sealed class VrSettings
    {
        internal readonly ConfigEntry<bool> Enabled;
        internal readonly ConfigEntry<KeyboardShortcut> ToggleKey,RecenterKey;
        internal readonly ConfigEntry<VrControllerShortcut> ControllerShortcut;
        internal readonly ConfigEntry<string> PanelButton,RecenterButton;
        internal readonly ConfigEntry<float> HoldSeconds,Distance,Width;
        // Set while the shortcut page waits for a controller button, so the runtime reads the raw masks.
        internal bool Capture;
        internal VrSettings(ConfigFile config)
        {
            Enabled=config.Bind("VR","Enabled",true,"Show KoiSync as a world-space panel with VRGIN or the official KoikatuVR headset camera");
            ToggleKey=config.Bind("VR","ToggleKey",new KeyboardShortcut(KeyCode.F7,KeyCode.LeftControl),"Toggle panel, including in VR");
            RecenterKey=config.Bind("VR","RecenterKey",new KeyboardShortcut(KeyCode.F7,KeyCode.LeftControl,KeyCode.LeftShift),"Open and reposition panel in front of the headset");
            ControllerShortcut=config.Bind("VR","ControllerShortcut",VrControllerShortcut.BothGrip,"Hold both tracked controllers' Grip or Menu buttons; Disabled uses the keyboard only");
            HoldSeconds=config.Bind("VR","HoldSeconds",.7f,new ConfigDescription("Hold duration; release before another toggle",new AcceptableValueRange<float>(.3f,2f)));
            Distance=config.Bind("VR","PanelDistance",1.25f,new ConfigDescription("Panel distance in headset-scaled world units",new AcceptableValueRange<float>(.5f,3f)));
            Width=config.Bind("VR","PanelWidth",1.2f,new ConfigDescription("Workspace width in headset-scaled world units",new AcceptableValueRange<float>(.6f,2.4f)));
            PanelButton=config.Bind("VR","PanelButton","B:4","VR controller button that toggles the panel: B/L/R plus a button bit, or None");
            RecenterButton=config.Bind("VR","RecenterButton","None","VR controller button that recenters the panel: B/L/R plus a button bit, or None");
        }
    }
    internal sealed class VrFrame
    {
        internal Camera Camera;
        internal Transform Head,Aim;
        internal bool Chord,Trigger,BothTriggers;
        // Raw controller masks, filled only while a VR shortcut waits for a button.
        internal ulong LeftHeld,RightHeld;
        internal object LeftDevice,RightDevice;
        internal bool Twins;
        internal Vector2 Scroll;
        internal string Status="未检测到活动 VR 相机";
        internal bool Active{get{return Camera!=null&&Camera.isActiveAndEnabled&&Head!=null&&Head.gameObject.activeInHierarchy;}}
    }
    internal sealed class VrRuntime
    {
        private Type vr;
        private Component cameraRig;
        private Component officialRig;
        private float nextScan;
        private readonly HoldShortcut hold=new HoldShortcut();
        internal VrFrame Frame=new VrFrame();
        internal bool Poll(VrSettings settings)
        {
            Frame=new VrFrame();
            try
            {
                if(Time.unscaledTime>=nextScan)
                {
                    nextScan=Time.unscaledTime+1;vr=null;cameraRig=null;officialRig=null;
                    foreach(Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        Type api=assembly.GetType("VRGIN.Core.VR",false),rig=assembly.GetType("VRGIN.Core.VRCamera",false);
                        if(api==null||rig==null||!StudioMembers.Flag(api,"Active",false))continue;
                        foreach(UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(rig))if(obj is Component){cameraRig=(Component)obj;vr=api;break;}
                        if(cameraRig!=null)break;
                    }
                    if(cameraRig==null)
                    {
                        Type type=StudioMembers.FindType("VRViveCameraManager");
                        if(type!=null)foreach(UnityEngine.Object obj in UnityEngine.Object.FindObjectsOfType(type))
                            if(obj is Behaviour&&((Behaviour)obj).isActiveAndEnabled)
                            {
                                Camera camera=StudioMembers.Read(StudioMembers.Read(obj,"scrCamera"),"camera") as Camera;
                                if(officialRig==null)officialRig=(Component)obj;
                                if(camera!=null&&camera.isActiveAndEnabled){officialRig=(Component)obj;break;}
                            }
                    }
                }
                if(settings.Enabled.Value&&vr!=null&&cameraRig!=null&&StudioMembers.Flag(vr,"Active",false))
                {
                    Frame.Head=StudioMembers.Read(cameraRig,"Head") as Transform;
                    object steam=StudioMembers.Read(cameraRig,"SteamCam");Frame.Camera=StudioMembers.Read(steam,"camera") as Camera;
                    if(Frame.Camera==null&&Frame.Head!=null)Frame.Camera=Frame.Head.GetComponent<Camera>();
                    object mode=StudioMembers.Read(vr,"Mode"),left=StudioMembers.Read(mode,"Left"),right=StudioMembers.Read(mode,"Right");
                    object leftInput=Device(left),rightInput=Device(right);
                    Frame.LeftDevice=leftInput;Frame.RightDevice=rightInput;Frame.Twins=left!=right;
                    Frame.Chord=left!=right&&ButtonHeld(VrButton.Effective(settings));
                    // Both triggers (LT + RT) jump straight to the climax animation in the main game.
                    Frame.BothTriggers=left!=right&&Press(leftInput,1UL<<33)&&Press(rightInput,1UL<<33);
                    if(settings.Capture){Frame.LeftHeld=Held(leftInput);Frame.RightHeld=Held(rightInput);}
                    object pointer=rightInput!=null?right:left;object input=rightInput??leftInput;
                    Component tracked=StudioMembers.Read(pointer,"Tracking") as Component;
                    Frame.Aim=tracked!=null?tracked.transform:pointer is Component?((Component)pointer).transform:Frame.Head;
                    Frame.Trigger=Press(input,1UL<<33)&&!Frame.Chord;
                    Frame.Scroll=Axis(input)*Time.unscaledDeltaTime*8;
                    Frame.Status=vr.Assembly.GetName().Name+" · "+(Frame.Camera==null?"等待头显相机":input==null?"头部指向 / 空格点击":"手柄指向 / 扳机点击");
                }
                else if(settings.Enabled.Value&&officialRig!=null)
                    Frame=ReadOfficial(officialRig,settings.ControllerShortcut.Value,Time.unscaledDeltaTime,settings.Capture);
            }
            catch(Exception ex){Frame=new VrFrame{Status="VR 接口暂不可用："+ex.GetBaseException().Message};nextScan=0;}
            return hold.Update(Frame.Active&&Frame.Chord,Time.unscaledTime,settings.HoldSeconds.Value);
        }
        // Reuse the official game's initialized SteamVR devices. Never create or initialize a second runtime.
        internal static VrFrame ReadOfficial(object manager,VrControllerShortcut shortcut,float deltaTime)
        {return ReadOfficial(manager,shortcut,deltaTime,false);}
        internal static VrFrame ReadOfficial(object manager,VrControllerShortcut shortcut,float deltaTime,bool capture)
        {
            var frame=new VrFrame();object steam=StudioMembers.Read(manager,"scrCamera");
            frame.Camera=StudioMembers.Read(steam,"camera") as Camera;
            frame.Head=StudioMembers.Read(steam,"head") as Transform;
            if(frame.Head==null&&frame.Camera!=null)frame.Head=frame.Camera.transform;
            object a=null,b=null,first=null,second=null;
            IEnumerable controllers=StudioMembers.Read(StudioMembers.Read(manager,"scrControllerManager"),"lstController") as IEnumerable;
            if(controllers!=null)foreach(object controller in controllers)
            {
                object device=OfficialDevice(controller);if(device==null)continue;
                if(a==null){a=device;first=controller;}
                else if(!SameDevice(a,device)){b=device;second=controller;break;}
            }
            ulong button=shortcut==VrControllerShortcut.BothMenu?2UL:4UL;
            frame.Chord=shortcut!=VrControllerShortcut.Disabled&&b!=null&&Press(a,button)&&Press(b,button);
            frame.BothTriggers=b!=null&&Press(a,1UL<<33)&&Press(b,1UL<<33);
            frame.LeftDevice=a;frame.RightDevice=b;frame.Twins=b!=null&&!SameDevice(a,b);
            if(capture){frame.LeftHeld=Held(a);frame.RightHeld=Held(b);}
            object pointer=second??first,input=b??a;
            Component tracked=StudioMembers.Read(pointer,"trackedObj") as Component;
            frame.Aim=tracked==null?frame.Head:tracked.transform;
            frame.Trigger=Press(input,1UL<<33)&&!frame.Chord;frame.Scroll=Axis(input)*deltaTime*8;
            frame.Status="官方 KoikatuVR · 跟踪手柄 "+(b!=null?2:a!=null?1:0)+" / 2 · "+
                (input==null?"头部指向 / 空格点击":"手柄指向 / 扳机点击")+"\n组合键："+
                (shortcut==VrControllerShortcut.Disabled?"仅键盘":shortcut==VrControllerShortcut.BothMenu?"双菜单":"双侧握持")+
                (frame.Chord?" · 正在按住":" · 等待长按");
            return frame;
        }
        private static object OfficialDevice(object controller)
        {
            Behaviour behaviour=controller as Behaviour;
            if(behaviour!=null&&!behaviour.isActiveAndEnabled)return null;
            object tracked=StudioMembers.Read(controller,"trackedObj"),device=StudioMembers.Read(controller,"device");
            // connected/hasTracking refresh the cached pose before checking the most recent controller-state validity.
            return StudioMembers.Flag(tracked,"isValid",false)&&StudioMembers.Flag(device,"connected",false)&&
                StudioMembers.Flag(device,"hasTracking",false)&&StudioMembers.Flag(device,"valid",false)?device:null;
        }
        private static bool SameDevice(object a,object b)
        {
            if(ReferenceEquals(a,b))return true;
            object i=StudioMembers.Read(a,"index"),j=StudioMembers.Read(b,"index");return i!=null&&i.Equals(j);
        }
        private static object Device(object controller)
        {return controller is Behaviour&&((Behaviour)controller).isActiveAndEnabled&&StudioMembers.Flag(controller,"IsTracking",false)?StudioMembers.Read(controller,"Input"):null;}
        private static bool Press(object device,ulong mask)
        {
            if(device==null)return false;
            MethodInfo method=device.GetType().GetMethod("GetPress",new[]{typeof(ulong)});
            return method!=null&&(bool)method.Invoke(device,new object[]{mask});
        }
        // Every button the controller reports right now. Only used while a shortcut waits for input.
        private static ulong Held(object device)
        {
            if(device==null)return 0;
            MethodInfo method=device.GetType().GetMethod("GetPress",new[]{typeof(ulong)});
            if(method==null)return 0;
            ulong mask=0;
            for(int bit=0;bit<40;bit++){ulong probe=1UL<<bit;if((bool)method.Invoke(device,new object[]{probe}))mask|=probe;}
            return mask;
        }
        // True while the configured controller button is held; used by the recenter binding.
        internal bool ButtonHeld(string token)
        {
            char hand;int bit;
            if(!VrButton.TryParse(token,out hand,out bit))return false;
            ulong mask=1UL<<bit;bool left=hand!='R'&&Press(Frame.LeftDevice,mask),right=hand!='L'&&Press(Frame.RightDevice,mask);
            if(hand=='B')return Frame.Twins&&left&&right;
            return hand=='L'?left:right;
        }
        private static Vector2 Axis(object device)
        {
            if(device==null)return Vector2.zero;
            foreach(MethodInfo method in device.GetType().GetMethods())if(method.Name=="GetAxis"&&method.GetParameters().Length==1)
            {Type parameter=method.GetParameters()[0].ParameterType;if(parameter.IsEnum){Vector2 value=(Vector2)method.Invoke(device,new[]{Enum.ToObject(parameter,32)});return new Vector2(0,Mathf.Abs(value.y)>.25f?value.y:0);}}
            return Vector2.zero;
        }
    }

    public sealed class VrPanelInput:BaseInput
    {
        internal VrFrame Frame;
        internal RectTransform Workspace;
        internal Vector2 Position;
        internal Vector3 HitPoint;
        internal bool Hit,Down,Up,Held;
        private bool wasHeld,ready;
        internal void ResetPointer(){wasHeld=false;ready=false;Hit=Down=Up=Held=false;Position=new Vector2(-100000,-100000);}
        internal void Sample()
        {
            Hit=false;Position=new Vector2(-100000,-100000);
            if(Frame!=null&&Frame.Active&&Workspace!=null)
            {
                Transform aim=Frame.Aim==null?Frame.Head:Frame.Aim;Ray ray=new Ray(aim.position,aim.forward);float distance;
                if(new Plane(Workspace.forward,Workspace.position).Raycast(ray,out distance)&&distance>0)
                {
                    HitPoint=ray.GetPoint(distance);Vector3 local=Workspace.InverseTransformPoint(HitPoint);
                    Hit=Workspace.rect.Contains(new Vector2(local.x,local.y));
                    if(Hit)Position=RectTransformUtility.WorldToScreenPoint(Frame.Camera,HitPoint);
                }
            }
            bool pressed=Frame!=null&&(Frame.Trigger||Input.GetKey(KeyCode.Space));
            // Opening/reattaching a controller while its trigger is held cannot click a control.
            if(Frame!=null&&Frame.Chord)ready=false;
            else if(!pressed)ready=true;
            Held=ready&&pressed;Down=Held&&!wasHeld&&Hit;Up=!Held&&wasHeld;wasHeld=Held;
        }
        public override bool mousePresent{get{return true;}}
        public override Vector2 mousePosition{get{return Position;}}
        public override Vector2 mouseScrollDelta{get{return Hit&&Frame!=null?Frame.Scroll:Vector2.zero;}}
        public override bool GetMouseButtonDown(int button){return button==0&&Down;}
        public override bool GetMouseButtonUp(int button){return button==0&&Up;}
        public override bool GetMouseButton(int button){return button==0&&Held;}
        public override float GetAxisRaw(string name){return 0;}
        public override bool GetButtonDown(string name){return false;}
    }
}

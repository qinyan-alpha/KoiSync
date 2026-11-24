using System;
using System.Collections.Generic;
using KoiSync.Core;
using KoiSync.Wire;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    internal enum AlignmentPart{Plane,Root,End,Target,TargetPlane}
    public sealed class AlignmentGizmo:MonoBehaviour
    {
        internal MeasurementProfile Draft;
        internal Candidate Preview;
        internal Camera ViewCamera;
        internal bool Editing,Rotating;
        internal AlignmentPart Part;
        internal Action Changed,Finished,SelectionChanged;
        internal Func<bool> RotateKeyDown=()=>UnityEngine.Input.GetKeyDown(KeyCode.R),HideKeyDown=()=>UnityEngine.Input.GetKeyDown(KeyCode.H);
        internal Func<bool> InputBlocked;
        internal float Extent=.12f;
        internal bool Dragging{get{return dragAxis>=0;}}
        private DebugGeometry drawing;
        private int dragAxis=-1;
        private Vector3 dragOrigin,dragDirection,ringStart;
        private Quaternion rotationStart;
        private Vector2 screenStart;
        private MotionVector valueStart;
        private float axisStart,handleLength;
        private static readonly Color[] colors={new Color(1,.3f,.33f),new Color(.4f,1,.45f),new Color(.35f,.65f,1)};
        internal bool PlaneSelected{get{return Part==AlignmentPart.Plane||Part==AlignmentPart.TargetPlane;}}
        internal MotionVector Value
        {
            get
            {
                if(Draft==null)return new MotionVector();
                switch(Part){case AlignmentPart.Plane:return Rotating?Draft.PlaneRotation:Draft.PlaneOffset;case AlignmentPart.TargetPlane:return Rotating?Draft.TargetPlaneRotation:Draft.TargetPlaneOffset;case AlignmentPart.Root:return Draft.RootOffset;case AlignmentPart.End:return Draft.EndOffset;default:return Draft.TargetOffset;}
            }
            set
            {
                if(Draft==null)return;
                switch(Part){case AlignmentPart.Plane:if(Rotating)Draft.PlaneRotation=value;else Draft.PlaneOffset=value;break;case AlignmentPart.TargetPlane:if(Rotating)Draft.TargetPlaneRotation=value;else Draft.TargetPlaneOffset=value;break;case AlignmentPart.Root:Draft.RootOffset=value;break;case AlignmentPart.End:Draft.EndOffset=value;break;default:Draft.TargetOffset=value;break;}
                if(Changed!=null)Changed();
            }
        }
        internal MotionVector Coordinates
        {
            get{if(PlaneSelected&&!Rotating&&Preview!=null){var f=Part==AlignmentPart.Plane?Preview.Native.BodyFrame:Preview.Native.TargetBodyFrame;return f.World(Value);}return Value;}
            set{if(PlaneSelected&&!Rotating&&Preview!=null){var f=Part==AlignmentPart.Plane?Preview.Native.BodyFrame:Preview.Native.TargetBodyFrame;Value=f.Local(value);}else Value=value;}
        }
        internal void Choose(AlignmentPart part){CancelDrag();Part=part;Rotating=false;if(SelectionChanged!=null)SelectionChanged();}
        internal void ToggleRotation(){if(!PlaneSelected)return;CancelDrag();Rotating=!Rotating;if(SelectionChanged!=null)SelectionChanged();}
        internal bool ObjectVisible(AlignmentPart part){return Draft==null||(Draft.HiddenObjects&(1<<(int)part))==0;}
        internal void SetObjectVisible(AlignmentPart part,bool visible)
        {if(Draft==null)return;CancelDrag();if(visible)Draft.HiddenObjects&=~(1<<(int)part);else Draft.HiddenObjects|=1<<(int)part;if(SelectionChanged!=null)SelectionChanged();if(Finished!=null)Finished();Redraw();}
        internal void CancelDrag(){dragAxis=-1;}
        internal bool HitTest(Vector2 screen){return Editing&&Preview!=null&&ViewCamera!=null&&(Dragging||PickHandle(screen)>=0||Pick(screen,ViewCamera.ScreenPointToRay(screen)).HasValue);}
        internal void ResetSelected()
        {
            if(Draft==null)return;
            if(Part==AlignmentPart.Plane)Draft.PlaneOffset=Draft.PlaneRotation=new MotionVector();
            else if(Part==AlignmentPart.TargetPlane)Draft.TargetPlaneOffset=Draft.TargetPlaneRotation=new MotionVector();
            else Value=new MotionVector();
            if(Changed!=null)Changed();if(Finished!=null)Finished();
        }
        internal Vector3 Position
        {
            get{var s=Preview.Native;return V(Part==AlignmentPart.Plane?s.ReferenceFrame.Origin:Part==AlignmentPart.TargetPlane?s.TargetReferenceFrame.Origin:Part==AlignmentPart.Root?s.Origin:Part==AlignmentPart.End?s.End:s.Target);}
        }
        private Vector3 Axis(int i)
        {
            if(PlaneSelected)return i==0?Vector3.right:i==1?Vector3.up:Vector3.forward;
            var f=Preview.Native.ReferenceFrame;return V(i==0?f.X:i==1?f.Y:f.Z);
        }
        private void Awake(){drawing=gameObject.AddComponent<DebugGeometry>();drawing.LabelsVisible=false;drawing.Settings.XRay=true;drawing.Settings.Width=.0012f;}
        internal void Redraw()
        {
            drawing.RenderCamera=ViewCamera;drawing.BeginSkeleton();
            if(!Editing||Preview==null||Preview.Native==null||ViewCamera==null||!ObjectVisible(Part)){drawing.EndSkeleton();return;}
            Vector3 p=Position;float depth=ViewCamera.WorldToScreenPoint(p).z;
            handleLength=ViewCamera.orthographic?ViewCamera.orthographicSize*2*82/ViewCamera.pixelHeight:Mathf.Max(.01f,depth)*Mathf.Tan(ViewCamera.fieldOfView*Mathf.Deg2Rad*.5f)*2*82/ViewCamera.pixelHeight;
            handleLength=Mathf.Max(.006f,handleLength);drawing.Settings.Width=handleLength*.012f;
            drawing.Marker(p,handleLength*.08f,new Color(1,.85f,.3f));
            for(int i=0;i<3;i++)
            {
                if(Rotating&&PlaneSelected)drawing.Line(colors[i],dragAxis==i?3:1.5f,Ring(p,Axis(i),handleLength));
                else drawing.Arrow(p,Axis(i),handleLength,colors[i]);
            }
            if(PlaneSelected)
            {
                var f=Part==AlignmentPart.Plane?Preview.Native.ReferenceFrame:Preview.Native.TargetReferenceFrame;
                Vector3 x=V(f.X)*(Part==AlignmentPart.Plane?(float)Preview.Native.Width*.5f:Extent),z=V(f.Z)*(Part==AlignmentPart.Plane?(float)Preview.Native.Depth*.5f:Extent);
                drawing.Line(new Color(1,.85f,.3f),1.2f,p-x-z,p+x-z,p+x+z,p-x+z,p-x-z);
            }
            drawing.EndSkeleton();
        }
        private void Update()
        {
            if(!Editing||Preview==null||ViewCamera==null){CancelDrag();return;}
            if(RotateKeyDown()&&!Typing())ToggleRotation();
            if(HideKeyDown()&&!Typing())SetObjectVisible(Part,!ObjectVisible(Part));
            Vector2 screen=Input.mousePosition;bool blocked=InputBlocked!=null&&InputBlocked();
            if(Input.GetMouseButtonDown(0)&&!blocked)PointerDown(screen);
            if(Input.GetMouseButton(0)&&Dragging)PointerDrag(screen);
            if(Input.GetMouseButtonUp(0)&&Dragging){CancelDrag();if(Finished!=null)Finished();}
        }
        private static bool Typing(){var go=EventSystem.current==null?null:EventSystem.current.currentSelectedGameObject;return go!=null&&go.GetComponent<InputField>()!=null;}
        internal void PointerDown(Vector2 screen)
        {
            if(Preview==null||ViewCamera==null||Draft==null)return;Ray ray=ViewCamera.ScreenPointToRay(screen);
            AlignmentPart? hit=Pick(screen,ray);
            if(hit.HasValue&&hit.Value!=Part&&hit.Value>=AlignmentPart.Root&&hit.Value<=AlignmentPart.Target)
            {
                var s=Preview.Native;Vector3 point=V(hit==AlignmentPart.Root?s.Origin:hit==AlignmentPart.End?s.End:s.Target);
                if(Vector2.Distance(screen,ViewCamera.WorldToScreenPoint(point))<8){Choose(hit.Value);return;}
            }
            int axis=PickHandle(screen);
            if(axis>=0)
            {
                dragAxis=axis;dragOrigin=Position;dragDirection=Axis(axis);screenStart=screen;valueStart=Value;
                axisStart=AxisParameter(ray,dragOrigin,dragDirection);
                var f=Part==AlignmentPart.TargetPlane?Preview.Native.TargetReferenceFrame:Preview.Native.ReferenceFrame;
                rotationStart=Q(f);float d;ringStart=new Plane(dragDirection,dragOrigin).Raycast(ray,out d)?(ray.GetPoint(d)-dragOrigin).normalized:Vector3.zero;return;
            }
            if(hit.HasValue)Choose(hit.Value);
        }
        internal void PointerDrag(Vector2 screen)
        {
            if(!Dragging||Draft==null||Preview==null)return;Ray ray=ViewCamera.ScreenPointToRay(screen);
            if(Rotating&&PlaneSelected)
            {
                float distance;if(ringStart.sqrMagnitude<.1f||!new Plane(dragDirection,dragOrigin).Raycast(ray,out distance))return;
                Vector3 now=(ray.GetPoint(distance)-dragOrigin).normalized;
                float degrees=Mathf.Atan2(Vector3.Dot(dragDirection,Vector3.Cross(ringStart,now)),Vector3.Dot(ringStart,now))*Mathf.Rad2Deg;
                var basis=Part==AlignmentPart.Plane?Preview.Native.BodyFrame:Preview.Native.TargetBodyFrame;
                Quaternion relative=Quaternion.Inverse(Q(basis))*Quaternion.AngleAxis(degrees,dragDirection)*rotationStart;
                Vector3 e=relative.eulerAngles;Value=new MotionVector(Signed(e.x),Signed(e.y),Signed(e.z));
            }
            else
            {
                float now=AxisParameter(ray,dragOrigin,dragDirection),delta=now-axisStart;
                if(float.IsNaN(now)||float.IsNaN(axisStart))
                {Vector2 projected=(Vector2)ViewCamera.WorldToScreenPoint(dragOrigin+dragDirection*handleLength)-(Vector2)ViewCamera.WorldToScreenPoint(dragOrigin);if(projected.sqrMagnitude<1)return;delta=Vector2.Dot(screen-screenStart,projected)/projected.sqrMagnitude*handleLength;}
                AlignmentFrame basis=PlaneSelected?(Part==AlignmentPart.Plane?Preview.Native.BodyFrame:Preview.Native.TargetBodyFrame):Preview.Native.ReferenceFrame;
                Value=valueStart+basis.Local(M(dragDirection*delta));
            }
        }
        private static float Signed(float angle){return angle>180?angle-360:angle;}
        private static float AxisParameter(Ray ray,Vector3 p,Vector3 axis)
        {Vector3 w=ray.origin-p;float b=Vector3.Dot(axis,ray.direction),den=1-b*b;return den<.0001f?float.NaN:(Vector3.Dot(axis,w)-b*Vector3.Dot(ray.direction,w))/den;}
        private int PickHandle(Vector2 p)
        {
            if(!ObjectVisible(Part))return -1;
            float best=11;int found=-1;
            for(int i=0;i<3;i++)
            {
                Vector3[] points=Rotating&&PlaneSelected?Ring(Position,Axis(i),handleLength):new[]{Position+Axis(i)*handleLength*.2f,Position+Axis(i)*handleLength};
                for(int n=1;n<points.Length;n++){float d=ScreenDistance(p,points[n-1],points[n]);if(d<best){best=d;found=i;}}
            }
            return found;
        }
        internal AlignmentPart? Pick(Vector2 p,Ray ray)
        {
            var s=Preview.Native;float best=15;AlignmentPart? result=null;
            var positions=new[]{s.Origin,s.End,s.Target};
            for(int i=0;i<3;i++){if(!ObjectVisible((AlignmentPart)(i+1)))continue;Vector3 screen=ViewCamera.WorldToScreenPoint(V(positions[i]));float d=Vector2.Distance(p,screen);if(screen.z>0&&d<best){best=d;result=(AlignmentPart)(i+1);}}
            if(result.HasValue)return result;
            var targetLine=s.Contact==null?s.TargetReferenceFrame.X:StudioContactMotion.Twist(s);if(targetLine.Length<1e-7)targetLine=s.TargetReferenceFrame.X;else targetLine=targetLine.Unit();
            if(ObjectVisible(AlignmentPart.TargetPlane)&&s.HasTargetFrame&&ScreenDistance(p,V(s.TargetReferenceFrame.Origin-targetLine*Extent),V(s.TargetReferenceFrame.Origin+targetLine*Extent))<12)return AlignmentPart.TargetPlane;
            if(!ObjectVisible(AlignmentPart.Plane))return null;
            float width=(float)s.Width*.5f,depth=(float)s.Depth*.5f;
            Vector3 center=V(s.ReferenceFrame.Origin),x=V(s.ReferenceFrame.X)*width,z=V(s.ReferenceFrame.Z)*depth;var corners=new[]{center-x-z,center+x-z,center+x+z,center-x+z,center-x-z};
            for(int i=1;i<corners.Length;i++)if(ScreenDistance(p,corners[i-1],corners[i])<10)return AlignmentPart.Plane;
            float t;if(new Plane(V(s.ReferenceFrame.Y),V(s.ReferenceFrame.Origin)).Raycast(ray,out t)&&t>0)
            {MotionVector local=s.ReferenceFrame.Local(M(ray.GetPoint(t))-s.ReferenceFrame.Origin);if(Math.Abs(local.X)<=width&&Math.Abs(local.Z)<=depth)return AlignmentPart.Plane;}
            return null;
        }
        private float ScreenDistance(Vector2 p,Vector3 a,Vector3 b)
        {Vector3 x=ViewCamera.WorldToScreenPoint(a),y=ViewCamera.WorldToScreenPoint(b);if(x.z<=0||y.z<=0)return float.MaxValue;Vector2 d=(Vector2)y-(Vector2)x;float t=d.sqrMagnitude<.01f?0:Mathf.Clamp01(Vector2.Dot(p-(Vector2)x,d)/d.sqrMagnitude);return Vector2.Distance(p,(Vector2)x+d*t);}
        private static Vector3[] Ring(Vector3 origin,Vector3 axis,float radius)
        {Vector3 x=Vector3.Cross(axis,Mathf.Abs(axis.y)<.9f?Vector3.up:Vector3.right).normalized,z=Vector3.Cross(axis,x);var points=new Vector3[65];for(int i=0;i<points.Length;i++){float a=i*Mathf.PI*2/64;points[i]=origin+(x*Mathf.Cos(a)+z*Mathf.Sin(a))*radius;}return points;}
        internal static Quaternion Q(AlignmentFrame f){return Quaternion.LookRotation(V(f.Z),V(f.Y));}
        internal static Vector3 V(MotionVector p){return new Vector3((float)p.X,(float)p.Y,(float)p.Z);}
        internal static MotionVector M(Vector3 p){return new MotionVector(p.x,p.y,p.z);}
        private void OnDestroy(){if(drawing!=null)Destroy(drawing);}
    }
}

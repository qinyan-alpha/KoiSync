using System;
using System.Collections.Generic;
using System.Globalization;
using KoiSync.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed class TimelineStrip:MaskableGraphic,IPointerDownHandler,IPointerUpHandler,IDragHandler,IEndDragHandler,IPointerExitHandler,IScrollHandler
    {
        internal Action<double> Seek,Select;
        internal Action<double,Vector2> Context;
        internal Action<string> Edited;
        internal Func<bool> ControlHeld;
        internal bool ControllerPointer;
        internal bool ReadOnly;
        internal MotionCurve Curve;
        internal FunscriptSet Scripts;
        internal double ScriptOffset;
        internal int CurveAxis=-1;
        private float controllerClick=-10;
        private string controllerId;
        internal readonly TimelineViewport View=new TimelineViewport();
        internal readonly HashSet<string> SelectedIds=new HashSet<string>();
        internal double SelectionStart,SelectionEnd;
        internal bool Dragging{get{return gesture!=0;}}
        internal bool Lifted{get{return gesture==2;}}
        private SceneProfile scene;
        private double time,pressTime,pressAt,dragAt,previewStart,previewEnd;
        private string selected,dragId;
        private int gesture;
        private Font font;
        private readonly List<Text> captions=new List<Text>();
        private bool captionsDirty;
        internal void Initialize(Font value){font=value;raycastTarget=true;}
        internal void Set(SceneProfile s,double t,double d,string selection)
        {
            if(scene!=s){CancelGesture();View.Fit(d);SelectedIds.Clear();}
            if(View.Duration!=Math.Max(.01,d)){View.Duration=Math.Max(.01,d);View.Clamp();}
            if(selected!=selection&&!SelectedIds.Contains(selection)){SelectedIds.Clear();if(!string.IsNullOrEmpty(selection))SelectedIds.Add(selection);}
            scene=s;time=t;selected=selection;Redraw();
        }
        private void Redraw(){SetVerticesDirty();captionsDirty=true;}
        internal void ZoomAt(double fraction,double wheel){View.Zoom(fraction,wheel);Redraw();}
        internal void Fit(){View.Fit(View.Duration);Redraw();}
        internal void CancelGesture(){gesture=0;dragId=null;SelectionStart=SelectionEnd=0;Redraw();}
        private float X(double t){return rectTransform.rect.xMin+rectTransform.rect.width*(float)((t-View.Start)/View.Span);}
        internal static Color SegmentColor(TimelineSegment s){return Color.HSVToRGB((float)((Math.Max(0,s.ColorIndex)*.61803398875+.52)%1),.48f,.59f);}
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();Rect r=rectTransform.rect;Quad(vh,r.xMin,r.xMax,r.yMin,r.yMax,new Color32(17,29,43,255));if(scene==null)return;
            double major=View.Tick(r.width),minor=major/5;
            for(double t=Math.Ceiling(View.Start/minor)*minor;t<=View.Start+View.Span+minor*.01;t+=minor)
            {
                bool large=Math.Abs(t/major-Math.Round(t/major))<.001;float x=X(t);
                Quad(vh,x,x+1,r.yMax-(large?31:12),r.yMax,new Color32(106,125,146,255));
            }
            foreach(var s in scene.Segments)if(s.Id!=dragId||gesture!=2)Block(vh,s);
            if(gesture==2){var s=scene.Segments.Find(x=>x.Id==dragId);if(s!=null)Block(vh,s);}
            if(gesture==5&&Math.Abs(SelectionStart-SelectionEnd)>.0001)Quad(vh,Mathf.Max(r.xMin,X(Math.Min(SelectionStart,SelectionEnd))),Mathf.Min(r.xMax,X(Math.Max(SelectionStart,SelectionEnd))),r.yMin,r.yMax-37,new Color(.4f,.8f,1,.3f));
            DrawCurve(vh,r);
            float cursor=X(time);if(cursor>=r.xMin&&cursor<=r.xMax){Quad(vh,cursor-1,cursor+1,r.yMin,r.yMax,new Color32(255,71,86,255));Quad(vh,cursor-5,cursor+5,r.yMax-7,r.yMax,new Color32(255,71,86,255));}
        }
        private void Bounds(TimelineSegment s,out float a,out float b,out float lift)
        {
            Rect r=rectTransform.rect;bool moving=s.Id==dragId&&gesture>=2&&gesture<=4;
            a=Mathf.Max(r.xMin,X(moving?previewStart:s.Start))+1;b=Mathf.Min(r.xMax,X(moving?previewEnd:s.End))-1;lift=s.Id==dragId&&gesture==2?18:0;
        }
        private void Block(VertexHelper vh,TimelineSegment s)
        {
            float a,b,lift;Bounds(s,out a,out b,out lift);Rect r=rectTransform.rect;if(b<=a)return;
            float lo=r.yMin+5+lift,hi=r.yMax-43+lift;
            if(lift>0)Quad(vh,a+3,b+3,lo-7,hi-7,new Color(0,0,0,.55f));
            Color fill=SegmentColor(s);if(CurveAxis>=0)fill.a=.24f;Quad(vh,a,b,lo,hi,fill);
            if(SelectedIds.Contains(s.Id))
            {
                Color border=new Color32(107,229,202,255);Quad(vh,a,b,lo,lo+3,border);Quad(vh,a,b,hi-3,hi,border);
                Quad(vh,a,a+3,lo,hi,border);Quad(vh,b-3,b,lo,hi,border);
            }
        }
        private void DrawCurve(VertexHelper vh,Rect r)
        {
            if(CurveAxis<0||CurveAxis>=6||Curve==null)return;
            float bottom=r.yMin+7,height=r.height-51;
            for(int i=0;i<=2;i++)Quad(vh,r.xMin,r.xMax,bottom+height*i/2,bottom+height*i/2+1,new Color(1,1,1,.12f));
            Funscript script=Scripts==null?null:Scripts[CurveAxis];
            if(script!=null)
            {
                // Sample at screen resolution: long scripts do not create one mesh vertex per action.
                double start=Math.Max(View.Start,Math.Max(0,ScriptOffset)),end=Math.Min(View.Start+View.Span,script.Duration+ScriptOffset);
                if(end>start)
                {
                    int steps=Math.Max(1,(int)r.width);Vector2 previousScript=Vector2.zero;bool havePrevious=false;
                    for(int i=0;i<=steps;i++)
                    {
                        double at=start+(end-start)*i/steps,position;
                        if(!script.TryPosition(at-ScriptOffset,out position)){havePrevious=false;continue;}
                        Vector2 point=new Vector2(X(at),bottom+height*(float)position);
                        if(havePrevious)CurveLine(vh,previousScript,point,new Color32(255,191,88,255));
                        previousScript=point;havePrevious=true;
                    }
                }
            }
            MotionCurve.Point previous=null;
            foreach(var point in Curve.Points)
            {
                if(previous!=null&&Numbers.Finite(point.Values[CurveAxis])&&Numbers.Finite(previous.Values[CurveAxis])&&point.Time-previous.Time<=.101&&point.Source==previous.Source&&previous.Time>=View.Start&&point.Time<=View.Start+View.Span)
                {
                    var a=new Vector2(X(previous.Time),bottom+height*(float)Numbers.Clamp(previous.Values[CurveAxis],0,1));var b=new Vector2(X(point.Time),bottom+height*(float)Numbers.Clamp(point.Values[CurveAxis],0,1));
                    CurveLine(vh,a,b,new Color32(107,229,202,255));
                }
                previous=point;
            }
        }
        private static void CurveLine(VertexHelper vh,Vector2 a,Vector2 b,Color color)
        {
            Vector2 n=new Vector2(-(b-a).y,(b-a).x).normalized*1.3f;int k=vh.currentVertCount;
            vh.AddVert(a+n,color,Vector2.zero);vh.AddVert(a-n,color,Vector2.zero);vh.AddVert(b-n,color,Vector2.zero);vh.AddVert(b+n,color,Vector2.zero);
            vh.AddTriangle(k,k+1,k+2);vh.AddTriangle(k,k+2,k+3);
        }
        private void LateUpdate()
        {
            if(gesture==1&&Time.unscaledTime-pressTime>=.3){gesture=2;UpdatePreview();Redraw();}
            if(!captionsDirty||font==null||scene==null)return;captionsDirty=false;int n=0;Rect r=rectTransform.rect;double tick=View.Tick(r.width);
            for(double t=Math.Ceiling(View.Start/tick)*tick;t<=View.Start+View.Span+tick*.001;t+=tick)
            {float x=X(t)-r.xMin+4;if(x+78<=r.width)Caption(ref n,x,10,78,21,t.ToString(tick<1?"F2":"F1",CultureInfo.InvariantCulture)+" s",new Color32(186,202,220,255));}
            foreach(var s in scene.Segments)if(s.Id!=dragId||gesture!=2)BlockCaption(ref n,s);
            if(gesture==2){var s=scene.Segments.Find(x=>x.Id==dragId);if(s!=null)BlockCaption(ref n,s);}
            for(int i=n;i<captions.Count;i++)captions[i].gameObject.SetActive(false);
        }
        private void BlockCaption(ref int n,TimelineSegment s)
        {float a,b,lift;Bounds(s,out a,out b,out lift);if(b-a>=36)Caption(ref n,a-rectTransform.rect.xMin+6,51-lift,b-a-12,43,s.Title(),Color.white);}
        private void Caption(ref int i,float x,float y,float w,float h,string value,Color c)
        {
            Text text;if(i==captions.Count){var obj=new GameObject("Track caption",typeof(RectTransform));obj.transform.SetParent(transform,false);text=obj.AddComponent<Text>();text.font=font;text.fontSize=12;text.raycastTarget=false;text.supportRichText=false;captions.Add(text);}else text=captions[i];i++;
            text.gameObject.SetActive(true);text.transform.SetAsLastSibling();text.text=value;text.color=c;text.verticalOverflow=VerticalWrapMode.Truncate;
            RectTransform r=text.rectTransform;r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);
        }
        private static void Quad(VertexHelper vh,float x0,float x1,float y0,float y1,Color c)
        {if(x1<=x0||y1<=y0)return;int n=vh.currentVertCount;vh.AddVert(new Vector3(x0,y0),c,Vector2.zero);vh.AddVert(new Vector3(x0,y1),c,Vector2.zero);vh.AddVert(new Vector3(x1,y1),c,Vector2.zero);vh.AddVert(new Vector3(x1,y0),c,Vector2.zero);vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);}
        private Vector2 Local(PointerEventData e)
        {Vector2 p;RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,e.position,e.pressEventCamera??e.enterEventCamera,out p);return p;}
        private double Fraction(PointerEventData e){return Mathf.Clamp01((Local(e).x-rectTransform.rect.xMin)/rectTransform.rect.width);}
        public void OnPointerDown(PointerEventData e)
        {
            if(scene==null)return;CancelGesture();double t=View.At(Fraction(e));var hit=scene.At(t);
            if(EventSystem.current!=null&&e.button!=PointerEventData.InputButton.Right)EventSystem.current.SetSelectedGameObject(gameObject);
            if(ReadOnly){if(e.button==PointerEventData.InputButton.Left||e.button==PointerEventData.InputButton.Middle){gesture=6;if(Seek!=null)Seek(t);}e.Use();return;}
            if(ControllerPointer&&e.button==PointerEventData.InputButton.Left)
            {
                if(Local(e).y>rectTransform.rect.yMax-37){gesture=6;if(Seek!=null)Seek(t);e.Use();return;}
                bool twice=Time.unscaledTime-controllerClick<.4f&&controllerId==(hit==null?"":hit.Id);controllerClick=Time.unscaledTime;controllerId=hit==null?"":hit.Id;
                if(twice){controllerClick=-10;if(Context!=null)Context(t,e.position);e.Use();return;}
            }
            if(e.button==PointerEventData.InputButton.Right)
            {if(hit!=null&&!SelectedIds.Contains(hit.Id)){SelectedIds.Clear();SelectedIds.Add(hit.Id);if(Select!=null)Select(t);}if(Context!=null)Context(t,e.position);e.Use();return;}
            if(e.button==PointerEventData.InputButton.Middle){gesture=6;if(Seek!=null)Seek(t);e.Use();return;}
            if(e.button!=PointerEventData.InputButton.Left)return;
            EventSystem.current.SetSelectedGameObject(gameObject);
            SelectedIds.Clear();pressAt=dragAt=t;pressTime=Time.unscaledTime;
            if(hit!=null)
            {
                SelectedIds.Add(hit.Id);dragId=hit.Id;previewStart=hit.Start;previewEnd=hit.End;
                float x=Local(e).x,dl=Math.Abs(x-X(hit.Start)),dr=Math.Abs(x-X(hit.End));
                gesture=Math.Min(dl,dr)<=8?(dl<=dr?3:4):1;
            }
            else{gesture=5;SelectionStart=SelectionEnd=t;}
            if(Select!=null)Select(t);Redraw();e.Use();
        }
        public void OnDrag(PointerEventData e)
        {
            if(gesture==0)return;dragAt=View.At(Fraction(e));
            if(gesture==6){if(Seek!=null)Seek(dragAt);}
            else if(gesture==5)
            {
                if(!rectTransform.rect.Contains(Local(e))){CancelGesture();e.Use();return;}
                SelectionEnd=dragAt;SelectedIds.Clear();foreach(var s in scene.Segments)if(s.Start<Math.Max(SelectionStart,SelectionEnd)&&s.End>Math.Min(SelectionStart,SelectionEnd))SelectedIds.Add(s.Id);
            }
            else{if(gesture==1&&Time.unscaledTime-pressTime>=.3)gesture=2;UpdatePreview();}
            Redraw();e.Use();
        }
        private void UpdatePreview()
        {
            var s=scene==null?null:scene.Segments.Find(x=>x.Id==dragId);if(s==null)return;
            if(gesture==2){double length=s.End-s.Start;previewStart=Numbers.Clamp(s.Start+dragAt-pressAt,0,Math.Max(0,View.Duration-length));previewEnd=previewStart+length;}
            else if(gesture==3)previewStart=scene.ResizeLimit(s.Id,true,dragAt,View.Duration);
            else if(gesture==4)previewEnd=scene.ResizeLimit(s.Id,false,dragAt,View.Duration);
        }
        public void OnPointerUp(PointerEventData e)
        {
            try
            {
                if(scene!=null&&dragId!=null)
                {
                    string id=dragId;bool changed=gesture>=2&&gesture<=4;
                    if(gesture==2&&Math.Abs(dragAt-pressAt)>.0001)scene.Move(id,(previewStart+previewEnd)*.5,View.Duration);
                    if(gesture==3||gesture==4)scene.Resize(id,gesture==3,gesture==3?previewStart:previewEnd,View.Duration);
                    if(changed&&Edited!=null)Edited(id);
                }
            }
            finally{CancelGesture();e.Use();}
        }
        public void OnEndDrag(PointerEventData e){CancelGesture();}
        public void OnPointerExit(PointerEventData e){if(gesture==5)CancelGesture();}
        protected override void OnDisable(){CancelGesture();base.OnDisable();}
        private void OnApplicationFocus(bool focused){if(!focused)CancelGesture();}
        public void OnScroll(PointerEventData e)
        {
            if(gesture!=0){e.Use();return;}
            bool ctrl=ControlHeld!=null?ControlHeld():Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl);
            if(ctrl)ZoomAt(Fraction(e),e.scrollDelta.y);else{View.Pan(-e.scrollDelta.y*.08);Redraw();}e.Use();
        }
        protected override void OnRectTransformDimensionsChange(){base.OnRectTransformDimensionsChange();captionsDirty=true;}
    }
}

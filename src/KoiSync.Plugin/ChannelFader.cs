using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed class ChannelFader:MaskableGraphic,IPointerDownHandler,IPointerUpHandler,IInitializePotentialDragHandler,IBeginDragHandler,IDragHandler,IEndDragHandler
    {
        public float Lower,Upper=1,Value;
        public bool Valid;
        public bool Editing{get;private set;}
        internal Text ValueHint;
        internal Action<float,float> Changed;
        internal Action Started,Finished;
        private bool upper;
        internal void Set(float lower,float high,float value,bool valid)
        {Lower=lower;Upper=high;Value=value;Valid=valid;SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var r=rectTransform.rect;float bottom=r.yMin+6,height=r.height-12,center=r.center.x;
            Quad(vh,new Rect(center-17,bottom,34,height),new Color32(33,49,64,255));
            if(Valid){float y=bottom+Mathf.Clamp01(Value)*height;Quad(vh,new Rect(center-17,bottom,34,y-bottom),new Color32(52,175,163,255));Quad(vh,new Rect(center-23,y-1.5f,46,3),new Color32(255,244,174,255));}
            Quad(vh,new Rect(center-23,bottom+Lower*height-3,46,6),new Color32(104,228,201,255));
            Quad(vh,new Rect(center-23,bottom+Upper*height-3,46,6),new Color32(242,173,104,255));
        }
        private static void Quad(VertexHelper vh,Rect r,Color c)
        {int n=vh.currentVertCount;var v=UIVertex.simpleVert;v.color=c;foreach(var p in new[]{new Vector2(r.xMin,r.yMin),new Vector2(r.xMin,r.yMax),new Vector2(r.xMax,r.yMax),new Vector2(r.xMax,r.yMin)}){v.position=p;vh.AddVert(v);}vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);}
        private float Position(PointerEventData data)
        {Vector2 p;RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,data.position,data.pressEventCamera,out p);return Mathf.Clamp01((p.y-rectTransform.rect.yMin-6)/Mathf.Max(1,rectTransform.rect.height-12));}
        public void OnPointerDown(PointerEventData data)
        {if(data.button!=PointerEventData.InputButton.Left)return;float p=Position(data);upper=Mathf.Abs(p-Upper)<Mathf.Abs(p-Lower)||Mathf.Approximately(Lower,Upper)&&p>=Upper;Editing=true;if(Started!=null)Started();Apply(p);data.Use();}
        private void Apply(float p)
        {
            if(upper)Upper=Mathf.Clamp(p,Lower,1);else Lower=Mathf.Clamp(p,0,Upper);SetVerticesDirty();
            if(ValueHint!=null){ValueHint.gameObject.SetActive(true);ValueHint.text=Mathf.RoundToInt((upper?Upper:Lower)*999).ToString("000");ValueHint.rectTransform.anchoredPosition=new Vector2(0,-Mathf.Clamp((1-(upper?Upper:Lower))*(rectTransform.rect.height-12)+12,0,rectTransform.rect.height-21));}
            if(Changed!=null)Changed(Lower,Upper);
        }
        public void OnInitializePotentialDrag(PointerEventData data){data.useDragThreshold=false;}
        public void OnBeginDrag(PointerEventData data){}
        public void OnDrag(PointerEventData data){if(Editing)Apply(Position(data));}
        public void OnPointerUp(PointerEventData data){if(data.button==PointerEventData.InputButton.Left)Complete();}
        public void OnEndDrag(PointerEventData data){Complete();}
        private void Complete(){if(!Editing)return;Editing=false;if(ValueHint!=null)ValueHint.gameObject.SetActive(false);if(Finished!=null)Finished();}
        protected override void OnDisable(){Editing=false;if(ValueHint!=null)ValueHint.gameObject.SetActive(false);base.OnDisable();}
    }
}

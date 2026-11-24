using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KoiSync.Plugin
{
    public sealed class RangeSlider:Selectable,IInitializePotentialDragHandler,IBeginDragHandler,IDragHandler,IEndDragHandler
    {
        public float Lower{get;private set;}
        public float Upper{get;private set;}
        internal Action<float,float> Changed;
        internal Action Finished;
        internal RectTransform LowHandle,HighHandle,Fill;
        private bool upperSelected,editing;
        public void SetRange(float lower,float upper)
        {Lower=Mathf.Clamp01(lower);Upper=Mathf.Clamp(upper,Lower,1);Render();}
        private float Width{get{return ((RectTransform)transform).rect.width-16;}}
        private void Render()
        {
            if(LowHandle==null)return;
            LowHandle.anchoredPosition=new Vector2(8+Width*Lower,0);HighHandle.anchoredPosition=new Vector2(8+Width*Upper,0);
            Fill.anchoredPosition=new Vector2(8+Width*Lower,0);Fill.sizeDelta=new Vector2(Width*(Upper-Lower),4);
        }
        protected override void OnRectTransformDimensionsChange(){base.OnRectTransformDimensionsChange();Render();}
        private bool Position(PointerEventData data,out float value)
        {
            Vector2 local;var rect=(RectTransform)transform;value=0;
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect,data.position,data.pressEventCamera,out local))return false;
            value=Mathf.Clamp01((local.x-rect.rect.xMin-8)/Mathf.Max(1,Width));return true;
        }
        public override void OnPointerDown(PointerEventData data)
        {
            base.OnPointerDown(data);if(!IsActive()||!IsInteractable()||data.button!=PointerEventData.InputButton.Left)return;
            float value;if(!Position(data,out value))return;
            upperSelected=Mathf.Abs(value-Upper)<Mathf.Abs(value-Lower)||(Mathf.Approximately(Lower,Upper)&&value>=Upper);
            editing=true;Apply(value);
        }
        private void Apply(float value)
        {
            float lo=Lower,hi=Upper;
            if(upperSelected)hi=Mathf.Clamp(value,lo,1);else lo=Mathf.Clamp(value,0,hi);
            SetRange(lo,hi);if(Changed!=null)Changed(lo,hi);
        }
        public void OnInitializePotentialDrag(PointerEventData data){data.useDragThreshold=false;}
        public void OnBeginDrag(PointerEventData data){}
        public void OnDrag(PointerEventData data){float value;if(editing&&IsInteractable()&&Position(data,out value))Apply(value);}
        public override void OnPointerUp(PointerEventData data){base.OnPointerUp(data);if(data.button==PointerEventData.InputButton.Left)Complete();}
        public void OnEndDrag(PointerEventData data){Complete();}
        private void Complete(){if(!editing)return;editing=false;if(Finished!=null)Finished();}
        public override void OnMove(AxisEventData data)
        {
            if(!IsActive()||!IsInteractable())return;
            if(data.moveDir==MoveDirection.Up)upperSelected=true;
            else if(data.moveDir==MoveDirection.Down)upperSelected=false;
            else if(data.moveDir==MoveDirection.Left||data.moveDir==MoveDirection.Right)
            {Apply((upperSelected?Upper:Lower)+(data.moveDir==MoveDirection.Left?-.01f:.01f));if(Finished!=null)Finished();}
            else base.OnMove(data);
        }
    }
    public sealed partial class KoiSyncPanel
    {
        private RangeSlider Range(Transform parent,float x,float y,float width,float lower,float upper,Action<float,float> changed)
        {
            RectTransform root=Rect(parent,"Output range",x,y,width,28);Image(root,Color.clear,false).raycastTarget=true;
            Panel(root,8,12,width-16,4,new Color32(53,73,92,255));
            RangeSlider range=root.gameObject.AddComponent<RangeSlider>();
            range.Fill=Panel(root,0,0,0,4,Accent);range.Fill.anchorMin=range.Fill.anchorMax=new Vector2(0,.5f);range.Fill.pivot=new Vector2(0,.5f);
            range.LowHandle=RangeHandle(root,Accent);range.HighHandle=RangeHandle(root,new Color32(240,183,104,255));
            range.targetGraphic=root.GetComponent<Image>();StyleControl(range);range.SetRange(lower,upper);range.Changed=changed;
            return range;
        }
        private RectTransform RangeHandle(RectTransform parent,Color color)
        {
            RectTransform handle=Panel(parent,0,0,14,24,color);handle.anchorMin=handle.anchorMax=new Vector2(0,.5f);handle.pivot=new Vector2(.5f,.5f);return handle;
        }
    }
}

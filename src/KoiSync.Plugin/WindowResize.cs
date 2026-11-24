using UnityEngine;
using UnityEngine.EventSystems;

namespace KoiSync.Plugin
{
    public sealed class WindowResize:MonoBehaviour,IBeginDragHandler,IDragHandler
    {
        public RectTransform Target;
        public float Minimum=.55f,Maximum=1.65f;
        private Vector2 start;
        private float scale;
        public void OnBeginDrag(PointerEventData e){Target.SetAsLastSibling();RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)Target.parent,e.position,e.pressEventCamera,out start);scale=Target.localScale.x;}
        public void OnDrag(PointerEventData e)
        {
            if(e.button!=PointerEventData.InputButton.Left)return;Vector2 p;
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)Target.parent,e.position,e.pressEventCamera,out p))return;
            Vector2 d=p-start,available=((RectTransform)Target.parent).rect.size;
            float fit=Mathf.Min(Maximum,Mathf.Min(available.x/Target.rect.width,available.y/Target.rect.height));
            float next=Mathf.Clamp(scale+2*(d.x-d.y)/(Target.rect.width+Target.rect.height),Mathf.Min(Minimum,fit),fit);Target.localScale=new Vector3(next,next,1);
            Vector2 bounds=(available-Target.rect.size*next)*.5f;Target.anchoredPosition=new Vector2(Mathf.Clamp(Target.anchoredPosition.x,-Mathf.Max(0,bounds.x),Mathf.Max(0,bounds.x)),Mathf.Clamp(Target.anchoredPosition.y,-Mathf.Max(0,bounds.y),Mathf.Max(0,bounds.y)));e.Use();
        }
    }
}

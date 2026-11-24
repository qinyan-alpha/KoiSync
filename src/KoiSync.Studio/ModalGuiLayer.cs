using System;
using UnityEngine;

namespace KoiSync.Studio
{
    // Popups own a separate Unity window/control-ID namespace. Background
    // callbacks are still painted, but never see input while a popup is open.
    internal sealed class ModalGuiLayer
    {
        private bool blockBackground,wasOpen;
        private int heldButton=-1;
        public bool Capturing {get{return blockBackground||heldButton>=0;}}
        public bool AllowBackground
        {get{return !Capturing||Event.current.type==EventType.Layout||Event.current.type==EventType.Repaint;}}

        public void Begin(bool open){blockBackground=open||heldButton>=0;}
        public void Reset(){blockBackground=wasOpen=false;heldButton=-1;}

        public void Draw(int id,bool open,Rect owner,float width,float height,Action drawPopup)
        {
            if(!open&&heldButton<0){wasOpen=false;return;}
            if(open&&!wasOpen)
            {
                GUI.FocusControl(null);
                GUIUtility.keyboardControl=0;
                GUIUtility.hotControl=0;
            }
            wasOpen=open;
            GUI.ModalWindow(id,new Rect(0,0,width,height),delegate(int windowId)
            {
                var e=Event.current;var eventType=e.type;
                // An outside press may close the popup. Its matching release
                // must also stay in the modal layer, rather than hit a field.
                if(eventType==EventType.MouseDown)heldButton=e.button;
                GUI.BeginGroup(new Rect(owner.x,owner.y,width-owner.x,height-owner.y));
                try{if(open)drawPopup();}
                finally{GUI.EndGroup();}
                if(eventType==EventType.MouseUp&&e.button==heldButton)heldButton=-1;
                if(e.isMouse||e.isKey||e.type==EventType.ValidateCommand||e.type==EventType.ExecuteCommand)e.Use();
            },"",GUIStyle.none);
            GUI.BringWindowToFront(id);
        }
    }
}

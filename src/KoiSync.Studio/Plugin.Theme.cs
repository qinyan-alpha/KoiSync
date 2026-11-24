using System.Collections.Generic;
using UnityEngine;

namespace KoiSync.Studio
{
    // Eye-friendly soft satin-rose & warm-charcoal intimate luxury theme.
    // Uses procedural SDF anti-aliased rounded 9-slice textures and a scoped
    // GUISkin clone so KoiSync gets smooth rounded buttons, cards and sliders
    // without touching CharaStudio's own UI.
    public sealed partial class Plugin
    {
        private const float ContentTop=292;
        private static readonly Color WindowFill=new Color(.106f,.094f,.133f,.97f),HeaderFill=new Color(.137f,.122f,.176f,.99f);
        private static readonly Color SidebarFill=new Color(.090f,.078f,.118f,.99f),CardFill=new Color(.149f,.129f,.188f,.98f);
        private static readonly Color Edge=new Color(.247f,.212f,.306f),Accent=new Color(.870f,.545f,.651f),AccentDim=new Color(.330f,.195f,.265f);
        private static readonly Color Warm=new Color(.890f,.735f,.555f),PopupFill=new Color(.145f,.125f,.185f,.995f);
        private GUIStyle header,primaryButton,dangerButton,tabButton,tabSelected,windowStyle,cardStyle,popupStyle,pillStatusStyle,wrapSmall,wrapLabel;
        private GUISkin customSkin;
        private Texture2D pillAccentTex,statusDotTex,checkOffTex,checkOnTex;
        private readonly List<Texture2D> themeTextures=new List<Texture2D>();
        private readonly Dictionary<GUIStyle,GUIStyle> compactStyles=new Dictionary<GUIStyle,GUIStyle>();

        private Texture2D RoundedFrame(Color fill,Color border,int radius,bool verticalSheen=true)
        {
            int size=Mathf.Max(12,radius*2+8);
            var texture=new Texture2D(size,size,TextureFormat.ARGB32,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            float half=size*.5f,inner=half-radius-.5f;
            for(int y=0;y<size;y++)
            {
                float vNorm=(y+.5f)/size;
                Color body=verticalSheen?Color.Lerp(new Color(fill.r*.94f,fill.g*.94f,fill.b*.95f,fill.a),Color.Lerp(fill,Color.white,.045f),vNorm):fill;
                body.a=fill.a;
                for(int x=0;x<size;x++)
                {
                    float dx=Mathf.Max(Mathf.Abs(x+.5f-half)-inner,0f);
                    float dy=Mathf.Max(Mathf.Abs(y+.5f-half)-inner,0f);
                    float dist=Mathf.Sqrt(dx*dx+dy*dy)-radius;
                    float outerA=Mathf.Clamp01(.55f-dist);
                    float borderT=Mathf.Clamp01(dist+1.25f);
                    Color c=Color.Lerp(body,border,borderT);
                    c.a*=outerA;
                    texture.SetPixel(x,y,c);
                }
            }
            texture.Apply();themeTextures.Add(texture);return texture;
        }
        private Texture2D CircleTex(Color fill,Color border,int size)
        {
            var texture=new Texture2D(size,size,TextureFormat.ARGB32,false){filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp,hideFlags=HideFlags.HideAndDontSave};
            float half=size*.5f,r=half-1f;
            for(int y=0;y<size;y++)
            for(int x=0;x<size;x++)
            {
                float dx=x+.5f-half,dy=y+.5f-half;
                float dist=Mathf.Sqrt(dx*dx+dy*dy)-r;
                float outerA=Mathf.Clamp01(.55f-dist);
                float borderT=Mathf.Clamp01(dist+1.1f);
                Color body=Color.Lerp(fill,Color.white,.10f*(y/(float)size));
                Color c=Color.Lerp(body,border,borderT);
                c.a=fill.a*outerA;
                texture.SetPixel(x,y,c);
            }
            texture.Apply();themeTextures.Add(texture);return texture;
        }
        private GUIStyle FlatButton(Color fill,Color border,Color hover,Color pressed,Color text,int radius=6)
        {
            int b=radius+2;
            var style=new GUIStyle(GUI.skin.button){font=uiFont,alignment=TextAnchor.MiddleCenter,border=new RectOffset(b,b,b,b),padding=new RectOffset(8,8,2,2)};
            style.normal.background=RoundedFrame(fill,border,radius);style.hover.background=RoundedFrame(hover,Color.Lerp(border,Accent,.45f),radius);
            style.active.background=RoundedFrame(pressed,Accent,radius);style.focused.background=style.normal.background;
            style.onNormal.background=RoundedFrame(AccentDim,Accent,radius);style.onHover.background=RoundedFrame(Color.Lerp(AccentDim,Color.white,.07f),Accent,radius);style.onActive.background=style.active.background;
            style.normal.textColor=style.focused.textColor=text;style.hover.textColor=style.active.textColor=new Color(.99f,.97f,.99f);
            style.onNormal.textColor=style.onHover.textColor=style.onActive.textColor=new Color(.99f,.97f,.99f);
            return style;
        }
        private void BuildTheme()
        {
            button=FlatButton(new Color(.196f,.172f,.244f),new Color(.315f,.272f,.385f),new Color(.245f,.214f,.302f),new Color(.34f,.20f,.27f),new Color(.937f,.918f,.953f),6);
            primaryButton=FlatButton(new Color(.545f,.275f,.380f),new Color(.78f,.49f,.60f),new Color(.63f,.33f,.45f),new Color(.44f,.22f,.31f),new Color(.99f,.96f,.98f),7);
            dangerButton=FlatButton(new Color(.49f,.22f,.27f),new Color(.74f,.40f,.46f),new Color(.58f,.27f,.33f),new Color(.39f,.17f,.21f),new Color(.99f,.96f,.97f),7);
            tabButton=FlatButton(new Color(.135f,.118f,.172f),new Color(.24f,.205f,.295f),new Color(.185f,.162f,.232f),new Color(.29f,.18f,.24f),new Color(.73f,.68f,.79f),6);
            tabSelected=FlatButton(new Color(.255f,.165f,.235f),new Color(.72f,.45f,.56f),new Color(.295f,.19f,.27f),new Color(.23f,.15f,.21f),new Color(.98f,.95f,.97f),6);
            tabSelected.fontStyle=FontStyle.Bold;
            textField=new GUIStyle(GUI.skin.textField){font=uiFont,border=new RectOffset(7,7,7,7),padding=new RectOffset(9,9,4,4),alignment=TextAnchor.MiddleLeft};
            textField.normal.background=RoundedFrame(new Color(.095f,.084f,.122f),new Color(.29f,.25f,.36f),5,false);
            textField.hover.background=RoundedFrame(new Color(.115f,.10f,.145f),new Color(.46f,.34f,.48f),5,false);
            textField.focused.background=textField.onFocused.background=RoundedFrame(new Color(.11f,.095f,.14f),Accent,5,false);
            textField.normal.textColor=textField.hover.textColor=textField.focused.textColor=textField.onFocused.textColor=new Color(.95f,.93f,.96f);
            toggle=new GUIStyle(GUI.skin.toggle){font=uiFont};
            toggle.normal.textColor=toggle.hover.textColor=new Color(.88f,.84f,.91f);
            toggle.onNormal.textColor=toggle.onHover.textColor=new Color(.94f,.69f,.78f);
            header=new GUIStyle(label){fontStyle=FontStyle.Bold};header.normal.textColor=new Color(.91f,.70f,.77f);
            wrapSmall=new GUIStyle(small){wordWrap=true};wrapLabel=new GUIStyle(label){wordWrap=true};
            windowStyle=new GUIStyle(GUIStyle.none){border=new RectOffset(12,12,12,12)};
            windowStyle.normal.background=windowStyle.onNormal.background=RoundedFrame(WindowFill,new Color(.36f,.29f,.43f),10,false);
            cardStyle=new GUIStyle(GUIStyle.none){border=new RectOffset(10,10,10,10)};
            cardStyle.normal.background=RoundedFrame(CardFill,Edge,8,true);
            popupStyle=new GUIStyle(GUIStyle.none){border=new RectOffset(10,10,10,10)};
            popupStyle.normal.background=RoundedFrame(PopupFill,new Color(.68f,.43f,.54f,.92f),8,false);
            pillStatusStyle=new GUIStyle(GUIStyle.none){border=new RectOffset(7,7,7,7)};
            pillStatusStyle.normal.background=RoundedFrame(new Color(.088f,.078f,.115f,.95f),new Color(.22f,.19f,.28f),5,false);
            pillAccentTex=RoundedFrame(Accent,Accent,2,false);
            statusDotTex=CircleTex(Color.white,Color.white,14);
            checkOffTex=RoundedFrame(new Color(.115f,.100f,.148f),new Color(.42f,.36f,.50f),4,false);
            checkOnTex=RoundedFrame(new Color(.68f,.35f,.47f),new Color(.95f,.68f,.78f),4,true);

            // Scoped GUISkin clone so sliders & scrollbars inside KoiSync get soft rounded styling
            customSkin=ScriptableObject.Instantiate(GUI.skin);
            customSkin.hideFlags=HideFlags.HideAndDontSave;
            var hTrack=new GUIStyle(customSkin.horizontalSlider){border=new RectOffset(5,5,3,3),fixedHeight=8,margin=new RectOffset(4,4,6,6)};
            hTrack.normal.background=RoundedFrame(new Color(.19f,.16f,.24f),new Color(.32f,.27f,.39f),3,false);
            var hThumb=new GUIStyle(customSkin.horizontalSliderThumb){fixedWidth=14,fixedHeight=14,border=new RectOffset(0,0,0,0),margin=new RectOffset(0,0,0,0),overflow=new RectOffset(0,0,3,3)};
            hThumb.normal.background=CircleTex(new Color(.91f,.61f,.72f),new Color(.98f,.84f,.89f),16);
            hThumb.hover.background=CircleTex(new Color(.96f,.70f,.80f),Color.white,16);
            hThumb.active.background=CircleTex(Warm,Color.white,16);
            customSkin.horizontalSlider=hTrack;
            customSkin.horizontalSliderThumb=hThumb;
            var vBar=new GUIStyle(customSkin.verticalScrollbar){border=new RectOffset(5,5,5,5),fixedWidth=10};
            vBar.normal.background=RoundedFrame(new Color(.11f,.095f,.14f),new Color(.20f,.17f,.25f),4,false);
            var vThumb=new GUIStyle(customSkin.verticalScrollbarThumb){border=new RectOffset(4,4,4,4),fixedWidth=10};
            vThumb.normal.background=RoundedFrame(new Color(.38f,.31f,.46f),new Color(.52f,.42f,.60f),4,false);
            vThumb.hover.background=RoundedFrame(new Color(.56f,.37f,.48f),Accent,4,false);
            vThumb.active.background=RoundedFrame(Accent,Color.white,4,false);
            customSkin.verticalScrollbar=vBar;
            customSkin.verticalScrollbarThumb=vThumb;
            ApplyFontSizes();
        }
        private void ApplyFontSizes()
        {
            if(button==null)return;
            int buttonSize=UiText.English?12:13;
            foreach(var style in new[]{button,primaryButton,dangerButton,tabButton,tabSelected})style.fontSize=buttonSize;
            label.fontSize=wrapLabel.fontSize=UiText.English?13:14;header.fontSize=14;
            toggle.fontSize=UiText.English?12:13;textField.fontSize=14;
            compactStyles.Clear();
        }
        private GUIStyle Fit(GUIStyle style,string text,float width)
        {
            if(style.CalcSize(new GUIContent(text)).x<=width-8)return style;
            GUIStyle compact;if(!compactStyles.TryGetValue(style,out compact)){compact=new GUIStyle(style){fontSize=Mathf.Max(10,style.fontSize-2)};compactStyles[style]=compact;}
            return compact;
        }
        private bool B(Rect rect,string text,GUIStyle style){return GUI.Button(rect,text,Fit(style,text,rect.width));}
        private bool Check(Rect rect,bool value,string text)
        {
            var e=Event.current;
            bool clicked=false;
            if(GUI.enabled&&e.type==EventType.MouseDown&&e.button==0&&rect.Contains(e.mousePosition)){clicked=true;e.Use();}
            float box=15f,by=rect.y+(rect.height-box)*.5f;
            var boxRect=new Rect(rect.x+1,by,box,box);
            var tex=value?checkOnTex:checkOffTex;
            if(tex!=null)GUI.DrawTexture(boxRect,tex);
            else Box(boxRect,value?Accent:Edge);
            if(value)DrawDot(boxRect.x+4.5f,boxRect.y+4.5f,6f,new Color(1f,.96f,.98f,GUI.enabled?1f:.55f));
            var prevColor=GUI.contentColor;
            GUI.contentColor=value?new Color(.96f,.76f,.83f):new Color(.88f,.84f,.91f);
            GUI.Label(new Rect(rect.x+box+8,rect.y,Mathf.Max(10,rect.width-box-8),rect.height),text,Fit(small,text,Mathf.Max(10,rect.width-box-8)));
            GUI.contentColor=prevColor;
            return clicked?!value:value;
        }
        private bool Tab(Rect rect,string text,bool selected)
        {
            bool clicked=GUI.Button(rect,text,Fit(selected?tabSelected:tabButton,text,rect.width));
            if(selected&&pillAccentTex!=null)GUI.DrawTexture(new Rect(rect.x+8,rect.yMax-3,Mathf.Max(4,rect.width-16),2),pillAccentTex);
            return clicked;
        }
        // Titled satin rounded card. Returns the y coordinate where its content starts.
        private float Card(Rect rect,string caption)
        {
            if(cardStyle!=null)GUI.Box(rect,GUIContent.none,cardStyle);
            else Box(rect,CardFill);
            if(string.IsNullOrEmpty(caption))return rect.y+10;
            if(pillAccentTex!=null)GUI.DrawTexture(new Rect(rect.x+8,rect.y+9,3,15),pillAccentTex);
            GUI.Label(new Rect(rect.x+17,rect.y+4,rect.width-28,25),caption,header);
            return rect.y+32;
        }
        private void DrawDot(float x,float y,float d,Color color)
        {
            var prev=GUI.color;GUI.color=color;
            if(statusDotTex!=null)GUI.DrawTexture(new Rect(x,y,d,d),statusDotTex);else Box(new Rect(x,y,d,d),color);
            GUI.color=prev;
        }
        private static void Divider(float x,float y,float width){Box(new Rect(x,y,width,1),Edge);}
        private void Popup(Rect area)
        {
            if(popupStyle!=null)GUI.Box(area,GUIContent.none,popupStyle);
            else{Box(new Rect(area.x-1,area.y-1,area.width+2,area.height+2),Accent);Box(area,PopupFill);}
        }
        private void DestroyTheme()
        {
            if(customSkin!=null){Destroy(customSkin);customSkin=null;}
            foreach(var texture in themeTextures)if(texture!=null)Destroy(texture);
            themeTextures.Clear();
        }
    }
}

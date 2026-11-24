using System;
using System.Globalization;
using BepInEx.Configuration;
using KoiSync.Core;
using UnityEngine;
using System.Runtime.InteropServices;
using System.Text;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> strokeMappingSetting;
        private ConfigEntry<float> playerLengthSetting,deviceStrokeSetting;
        private ConfigEntry<string> playerRegionSetting;
        private string playerLengthText="",gameLengthText="",deviceStrokeText="";
        private bool regionPicker;
        private Rect regionPickerArea=new Rect();
        private static readonly string[] Regions={"GLOBAL","RU","JP","KR","GB","EU","CN","TW","HK","MO"};
        private static readonly string[] RegionNames={"Global","Россия","日本","한국","United Kingdom","EU / Europe","中国大陆","台灣","香港","澳門"};
        private double PlayerLength {get{return MappingMode==AxisMappingMode.Manual&&Binding!=null?Binding.RestLength:StrokeMapping.ValidModelLength(playerLengthSetting.Value/1000.0)?playerLengthSetting.Value/1000.0:StrokeMapping.ReferenceLength;}}
        private double DeviceStroke {get{return StrokeMapping.ValidLength(deviceStrokeSetting.Value/1000.0)?deviceStrokeSetting.Value/1000.0:StrokeMapping.ReferenceStroke;}}
        private double PreviewLength {get{return MappingMode==AxisMappingMode.Manual?Binding.RestLength:strokeMappingSetting.Value?PlayerLength:Binding.RestLength;}}
        [DllImport("kernel32.dll")]private static extern uint GetUserDefaultLCID();
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]private static extern int GetUserDefaultGeoName(StringBuilder name,int capacity);
        private void InitializeStrokeMapping()
        {
            // Unity Console may set its thread culture to invariant. Read the
            // Windows user locale so that it cannot erase the initial profile.
            string region="GLOBAL";try{region=new RegionInfo(CultureInfo.GetCultureInfo((int)GetUserDefaultLCID()).Name).TwoLetterISORegionName;}catch{}
            try{var homeRegion=new StringBuilder(16);if(GetUserDefaultGeoName(homeRegion,homeRegion.Capacity)>0)region=homeRegion.ToString();}catch{}
            if(Array.IndexOf(Regions,region)<0&&(UiText.Language=="de"||UiText.Language=="fr"))region="EU";
            if(Array.IndexOf(Regions,region)<0)region="GLOBAL";
            strokeMappingSetting=Config.Bind("Stroke","Enabled",false,"Legacy player-length adaptation (disabled by default; full 0-99 L0 range is used).");
            playerRegionSetting=Config.Bind("Stroke","Region",region,"Legacy default length profile.");
            playerLengthSetting=Config.Bind("Stroke","PlayerLengthMm",0f,"Legacy player length setting (unused when Stroke.Enabled is false).");
            deviceStrokeSetting=Config.Bind("Stroke","DeviceTravelMm",120f,"Actual full L0 travel in mm.");
            playerLengthText=(PlayerLength*1000).ToString("G6",CultureInfo.InvariantCulture);deviceStrokeText=(DeviceStroke*1000).ToString("G6",CultureInfo.InvariantCulture);
        }
        private ChapterStrokeCalibration EnsureStrokeCalibration()
        {
            var chapter=RotationChapter();
            if(chapter==null)return DefaultStrokeCalibration();
            if(chapter.StrokeCalibration==null){chapter.StrokeCalibration=DefaultStrokeCalibration();MarkSceneDataDirty();}
            return chapter.StrokeCalibration;
        }
        private ChapterStrokeCalibration DefaultStrokeCalibration()
        {
            if(Binding!=null&&Binding.IsHandSource)return new ChapterStrokeCalibration{GameLength=Numbers.Clamp(Binding.ReadBaseModelLength(),.02,.5),BodyHeight=0,FromHeight=false,FromFinger=true};
            return ChapterStrokeCalibration.FromBody(Binding==null?0:Binding.ReadBodyHeight());
        }
        private void SetPlayerLength(double value,bool save)
        {
            if(!StrokeMapping.ValidModelLength(value))throw new ArgumentException(L("玩家长度须为 1–500 mm","Player length must be 1-500 mm"));
            double old=PlayerLength;playerLengthSetting.Value=(float)(value*1000);playerLengthText=(value*1000).ToString("G6",CultureInfo.InvariantCulture);
            if(linkLengthSetting!=null&&linkLengthSetting.Value&&Math.Abs(old-value)>1e-8)SetMappingRanges(Numbers.Clamp(l1HalfRange*value/old,.001,.5),Numbers.Clamp(l2HalfRange*value/old,.001,.5),save);
            Devices.Stop();if(save)Config.Save();
        }
        private void ApplyStrokeText()
        {
            try
            {
                double player=ParseMillimetres(playerLengthText)/1000,stroke=ParseMillimetres(deviceStrokeText)/1000,game=ParseMillimetres(gameLengthText)/1000;
                if(!StrokeMapping.ValidModelLength(player))throw new ArgumentException(L("玩家长度须为 1–500 mm","Player length must be 1-500 mm"));
                if(!StrokeMapping.ValidLength(stroke)||!StrokeMapping.ValidLength(game))throw new ArgumentException(L("长度须为 20–500 mm","Length must be 20-500 mm"));
                var chapter=RotationChapter();if(chapter==null)return;
                RememberChapters();var calibration=EnsureStrokeCalibration();calibration.GameLength=game;calibration.FromHeight=false;calibration.FromFinger=false;
                SetPlayerLength(player,false);deviceStrokeSetting.Value=(float)(stroke*1000);Config.Save();MarkSceneDataDirty();SaveSceneData();
            }
            catch(Exception error){status=LocalError(error.Message);}
        }
        private void DrawStrokeSettings(Rect card)
        {
            float top=Card(card,L("L0 行程范围（默认 0–99）","L0 travel range (default 0–99)"));float x=card.x+12,w=card.width-24;
            if(ManualCalibrationActive)
            {
                GUI.Label(new Rect(x,top,w,24),L("手动标尺（起点到末端的距离）","Manual span (distance between endpoints)"),small);
                GUI.Label(new Rect(x,top+24,w,26),(PlayerLength*1000).ToString("F2",CultureInfo.InvariantCulture)+" mm · "+L("随起点 / 末端自动更新","Updates with either endpoint"),label);
                GUI.Label(new Rect(x,top+56,w,24),L("L0：起点 → 0，末端 → 99；两点之间映射完整行程。","L0: origin → 0, tip → 99; the endpoint span maps to full travel."),small);
                GUI.Label(new Rect(x,top+80,w,24),L("缩短起末距离会放大动作，拉长则减小；入口位置保持跟随绑定点。","Shorter spans amplify motion; longer spans reduce it. Entry follows its binding."),small);
                GUI.Label(new Rect(x,top+104,w,40),L("校准随章节保存；六轴窗口的倍率、限位和平滑仍生效。","Calibration saves per chapter; axis gain, travel limits and smoothing still apply."),wrapSmall);
                return;
            }
            GUI.Label(new Rect(x,top,w,40),L("已取消玩家长度底限截断，默认使用完整 0–99 行程范围。各档手感会自动将章节动作映射到全幅黄金区间。","Player-length clipping removed; full 0–99 travel is active by default. Feel presets map each chapter smoothly onto the full range."),wrapSmall);
            GUI.Label(new Rect(x,top+46,w,24),L("当前 L0 输出范围：","Current L0 range: ")+Math.Round(Drive.Minimum[0]*99.0).ToString("0",CultureInfo.InvariantCulture)+" – "+Math.Round(Math.Min(Drive.Maximum[0],Feel.OuterCeiling)*99.0).ToString("0",CultureInfo.InvariantCulture)+" / 99",label);
            GUI.Label(new Rect(x,top+78,92,24),L("L0 外限：","Outer L0:"),small);
            float curCeil=(float)Math.Round(Feel.OuterCeiling*100.0,1);
            float nextCeil=GUI.HorizontalSlider(new Rect(x+92,top+84,w-200,20),curCeil,55f,99f);
            GUI.Label(new Rect(x+w-102,top+78,44,24),curCeil.ToString("0",CultureInfo.InvariantCulture)+"%",small);
            bool resetCeil=B(new Rect(x+w-54,top+76,54,26),"99");
            if(resetCeil||Math.Abs(nextCeil-curCeil)>=0.5f)
                SetFeelOuterCeiling((resetCeil?99.0:Math.Round(nextCeil))/100.0);
            if(B(new Rect(x,top+114,200,30),L("恢复默认 0–99 全幅","Reset full 0–99 range"),primaryButton))
            {
                strokeMappingSetting.Value=false;
                Drive.Minimum[0]=0;Drive.Maximum[0]=1;
                savedMin[0].Value=0;savedMax[0].Value=1;
                SetFeelOuterCeiling(FeelTranslator.DefaultOuterCeiling);
                Config.Save();
                status=L("L0 已恢复 0–99 全幅行程","L0 restored to full 0–99 range");
            }
        }
        private void DrawRegionPicker()
        {
            var bounds=regionPickerArea;
            if(Event.current.type==EventType.MouseDown&&!bounds.Contains(Event.current.mousePosition)||Event.current.type==EventType.KeyDown&&Event.current.keyCode==KeyCode.Escape){regionPicker=false;Event.current.Use();return;}
            GUI.enabled=true;Popup(bounds);
            for(int i=0;i<Regions.Length;i++)if(B(new Rect(bounds.x+4,bounds.y+4+i*28,bounds.width-8,26),(Regions[i]==playerRegionSetting.Value?"●  ":"")+RegionNames[i])){playerRegionSetting.Value=Regions[i];Config.Save();regionPicker=false;}
        }
    }
}

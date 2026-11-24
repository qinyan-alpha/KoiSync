using System;
using BepInEx.Configuration;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.MainGame
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> showEntryMarkers,showHandles,showDevicePose;
        private ContactPreviewPose geometryPose,devicePose;
        private double visibleContactDistance,visibleInsertion;
        private readonly Rect[] endpointHandles=new Rect[2];
        private MainBinding draggedBinding;
        private int draggedEndpoint;
        private Vector2 dragStart,pixelsPerMetre;
        private MotionVector dragOrigin,dragTip;
        private double dragLength;
        private ActionCalibration dragCalibration;

        private void InitializePreview()
        {
            showEntryMarkers=Config.Bind("View","EntryMarkers",true);
            showHandles=Config.Bind("View","EndpointHandles",true);
            showDevicePose=Config.Bind("View","ShowOutputPose",false);
        }

        private void ClearPreview()
        {
            geometryPose=devicePose=null;
            if(preview!=null)preview.Hide();
        }

        private void UpdatePreview()
        {
            if(Binding==null||Sample==null||Measured==null){ClearPreview();return;}
            bool endpoints=Binding.UsesEndpoints(Profile);
            var angles=Profile.Mode==AxisMappingMode.Manual?Binding.Calibration.PlaneDegrees:new MotionVector();
            geometryPose=ManualCalibrationMotion.Preview(Sample,Measured,angles);
            visibleContactDistance=Measured.ContactDistance;visibleInsertion=Measured.MaterialInsertion;
            if(endpoints&&Sample.ShaftPath!=null&&Measured.VisibleLength>1e-7)
            {
                visibleContactDistance=StudioShaftMotion.ClosestDistance(Sample.ShaftPath,Sample.TargetReferenceFrame.Origin);
                visibleInsertion=StudioShaftMotion.InsertionFraction(Sample.ShaftPath,Sample.TargetReferenceFrame.Origin,Sample.TargetReferenceFrame.Y);
            }

            devicePose=null;
            if(outputValid)
            {
                bool adapt=lengthAdaptation.Value&&!endpoints;
                double registration=adapt?PlayerLength:Measured.Length;
                double travel=adapt||endpoints?DeviceStroke:Measured.Length;
                devicePose=ContactPreviewMotion.Solve(Sample,GameDrive.PreviewAxes(outputValues),registration,Measured,travel);
            }
            preview.Display(showDevicePose.Value?devicePose:geometryPose,previewEnabled.Value,xray.Value,
                showDevicePose.Value?L("输出模型 · 发送延迟前六轴","Output pose · pre-delay six axes"):L("几何模型 · 当前校准入口","Geometry pose · calibrated entry"));
        }

        private void DrawWorldPreview()
        {
            endpointHandles[0]=endpointHandles[1]=new Rect();
            var camera=Camera.main;
            if(!showWindow||mainPage!=1||Binding==null||geometryPose==null||camera==null)
            {EndEndpointDrag();return;}

            if(showEntryMarkers.Value)
            {
                var raw=WorldPoint(Binding.Entry.Observed.Origin);
                var calibrated=WorldPoint(Binding.Entry.Calibrated.Origin);
                DrawMarker(camera,raw,L("观测入口","Observed"),Accent);
                if((raw-calibrated).sqrMagnitude>1e-8f)DrawMarker(camera,calibrated,L("校准入口","Calibrated"),CalibratedColor);
                if(devicePose!=null)
                {
                    var output=WorldPoint(devicePose.Sleeve.Origin);
                    WorldLine(camera,calibrated,output,Warm);
                    var outputScreen=camera.WorldToScreenPoint(output);
                    var inputScreen=camera.WorldToScreenPoint(calibrated);
                    if((outputScreen-inputScreen).sqrMagnitude>81)DrawMarker(camera,output,L("输出重建","Output"),Warm);
                }
            }
            if(workbenchTab!=1||!showHandles.Value||!Binding.UsesEndpoints(Profile))
            {EndEndpointDrag();return;}
            if(draggedBinding!=null&&draggedBinding!=Binding)EndEndpointDrag();
            var root=WorldPoint(geometryPose.RodRoot);
            var direction=WorldPoint(geometryPose.RodDirection);
            var tip=root+direction*(float)geometryPose.RodLength;
            WorldLine(camera,root,tip,Accent);
            DrawEndpoint(camera,0,root,direction,L("起点","Origin"));
            DrawEndpoint(camera,1,tip,direction,L("末端","Tip"));
            var e=Event.current;
            if(draggedBinding!=null&&(e.type==EventType.MouseDrag||e.rawType==EventType.MouseUp))
            {
                ApplyEndpointDrag(e.mousePosition);
                if(e.rawType==EventType.MouseUp)EndEndpointDrag();
                if(e.type!=EventType.Used)e.Use();
            }
        }

        private void DrawEndpoint(Camera camera,int index,Vector3 world,Vector3 direction,string caption)
        {
            var point=camera.WorldToScreenPoint(world);
            if(point.z<=0)return;
            var screen=ScreenPoint(point);
            var rect=new Rect(screen.x-9,screen.y-9,18,18);
            endpointHandles[index]=rect;
            Box(rect,Accent);Box(new Rect(rect.x+3,rect.y+3,12,12),WindowFill);
            GUI.Label(new Rect(rect.x+21,rect.y-4,70,22),caption,small);
            var e=Event.current;
            if(draggedBinding!=null||e.type!=EventType.MouseDown||e.button!=0||!rect.Contains(e.mousePosition)||InsideWindows(e.mousePosition))return;
            draggedBinding=Binding;draggedEndpoint=index;dragStart=e.mousePosition;
            dragCalibration=Binding.Calibration.Copy();
            dragOrigin=dragCalibration.Origin;dragTip=dragCalibration.ManualEnd.Value;dragLength=dragCalibration.Span(Binding.Length);
            pixelsPerMetre=(ScreenPoint(camera.WorldToScreenPoint(world+direction*.1f))-screen)*10;
            if(pixelsPerMetre.sqrMagnitude<10000)pixelsPerMetre=new Vector2(0,-1000);
            BeginDriveEdit("endpoint drag");e.Use();
        }

        private void ApplyEndpointDrag(Vector2 mouse)
        {
            double delta=Vector2.Dot(mouse-dragStart,pixelsPerMetre)/pixelsPerMetre.sqrMagnitude;
            var next=dragCalibration.Copy();
            var origin=dragOrigin;
            var end=dragTip;
            if(draggedEndpoint==0)origin=ManualCalibrationMotion.MoveOriginAlongAxis(dragOrigin,dragTip,delta);
            else end=dragOrigin+(dragTip-dragOrigin).Unit()*Numbers.Clamp(dragLength+delta,ManualCalibrationMotion.MinimumLength,.5);
            try{next.SetEndpoints(origin,end,dragLength);ApplyCalibration(next,true,setsEndpoints:true);}
            catch(ArgumentException error){uiNotice=error.Message;}
        }

        private void EndEndpointDrag()
        {
            if(draggedBinding==null)return;
            draggedBinding=null;FinishDriveEdit();
        }
        private static Vector3 WorldPoint(MotionVector point){return new Vector3((float)point.X,(float)point.Y,(float)point.Z);}
        private static Vector2 ScreenPoint(Vector3 point){return new Vector2(point.x,Screen.height-point.y);}
        private static void WorldLine(Camera camera,Vector3 a,Vector3 b,Color color)
        {
            var start=camera.WorldToScreenPoint(a);var finish=camera.WorldToScreenPoint(b);
            if(start.z<=0||finish.z<=0)return;
            var p=ScreenPoint(start);var d=ScreenPoint(finish)-p;
            if(d.sqrMagnitude<.25f)return;
            var matrix=GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg,p);
            Box(new Rect(p.x,p.y-1,d.magnitude,2),color);
            GUI.matrix=matrix;
        }
        private void DrawMarker(Camera camera,Vector3 world,string caption,Color color)
        {
            var point=camera.WorldToScreenPoint(world);
            if(point.z<=0)return;
            var p=ScreenPoint(point);
            Box(new Rect(p.x-5,p.y-1,11,3),color);Box(new Rect(p.x-1,p.y-5,3,11),color);
            float bw=Mathf.Max(70f,small.CalcSize(new GUIContent(caption)).x+10f);
            Box(new Rect(p.x+8,p.y-11,bw,21),WindowFill);
            GUI.Label(new Rect(p.x+12,p.y-12,bw-4,23),caption,small);
        }
    }
}

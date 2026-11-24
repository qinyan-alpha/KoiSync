using System;
using System.Globalization;
using KoiSync.Core;
using UnityEngine;

namespace KoiSync.Studio
{
    public sealed partial class Plugin
    {
        // 0: tip/length, 1: origin, 2..3: automatic-mode origin L1/L2,
        // 4..5: L1/L2 half ranges. Manual origin and tip share one axis.
        private readonly Rect[] modelHandles=new Rect[6];
        private bool draggingRod;
        private int modelDragHandle;
        private StudioBinding draggedBinding;
        private Vector2 rodDragStart,rodPixelsPerMetre;
        private double rodDragLength;
        private MotionVector originDragStart,originDragEnd;

        private bool ModelHandleContains(Vector2 point)
        {foreach(var handle in modelHandles)if(handle.width>0&&handle.Contains(point))return true;return false;}
        private void ClearModelHandles(){for(int i=0;i<modelHandles.Length;i++)modelHandles[i]=new Rect();}
        private static Vector3 WorldPoint(MotionVector point){return new Vector3((float)point.X,(float)point.Y,(float)point.Z);}
        private static Vector2 ScreenPoint(Vector3 point){return new Vector2(point.x,Screen.height-point.y);}

        private void DrawRodHandle()
        {
            if(!guiVisible||!showPreview||!showCalibrationHandles||Binding==null||Measured==null||Sample==null||Camera.main==null)
            {draggingRod=false;ClearModelHandles();return;}
            if(draggingRod&&draggedBinding!=Binding)draggingRod=false;
            Styles();var camera=Camera.main;var root=WorldPoint(Sample.Origin);
            var axial=WorldPoint(Measured.X);var l1=WorldPoint(Measured.Y);var l2=WorldPoint(Measured.Z);
            Vector3 planeNormal=axial;
            if(ManualCalibrationActive)
            {
                // The calibration rectangle is the editable R0/R1/R2 target
                // plane, centred on the configured penis tip. Its two in-plane
                // axes remain the L1/L2 range directions.
                var plane=ManualCalibrationMotion.Preview(Sample,Measured,ManualPlaneDegrees).Sleeve;
                l1=WorldPoint(plane.Z);l2=WorldPoint(plane.X);planeNormal=WorldPoint(plane.Y);
            }
            var tip=root+axial*(float)PreviewLength;
            if(ManualCalibrationActive)DrawWorldLine(camera,root,tip,new Color(.3f,.9f,.9f,.9f));
            // The rectangle is the full L1/L2 input range at the neutral tip.
            if(showCalibration)
            {
                var a=tip+l1*(float)l1HalfRange+l2*(float)l2HalfRange;
                var b=tip-l1*(float)l1HalfRange+l2*(float)l2HalfRange;
                var c=tip-l1*(float)l1HalfRange-l2*(float)l2HalfRange;
                var d=tip+l1*(float)l1HalfRange-l2*(float)l2HalfRange;
                DrawWorldLine(camera,a,b,new Color(.45f,.65f,.68f,.7f));DrawWorldLine(camera,b,c,new Color(.45f,.65f,.68f,.7f));
                DrawWorldLine(camera,c,d,new Color(.45f,.65f,.68f,.7f));DrawWorldLine(camera,d,a,new Color(.45f,.65f,.68f,.7f));
                if(ManualCalibrationActive)DrawWorldLine(camera,tip,tip+planeNormal*.04f,new Color(.3f,.9f,.9f,.85f));
            }
            DrawModelHandle(0,camera,tip,axial,L("阴茎末端 · ","Penis tip · ")+(PreviewLength*1000).ToString("F1")+" mm",new Color(.3f,.9f,.9f));
            if(ManualCalibrationActive)
            {
                DrawModelHandle(1,camera,root,axial,L("阴茎起点","Penis origin"),new Color(.3f,.9f,.9f));
                modelHandles[2]=modelHandles[3]=new Rect();
            }
            else if(showCalibration)
            {
                var originAxial=WorldPoint(Sample.ReferenceFrame.Y);var originL1=WorldPoint(Sample.ReferenceFrame.Z);var originL2=WorldPoint(Sample.ReferenceFrame.X);
                DrawWorldLine(camera,root,root+originAxial*.035f,new Color(.3f,.9f,.9f));
                DrawWorldLine(camera,root,root+originL1*.035f,new Color(.98f,.72f,.3f));
                DrawWorldLine(camera,root,root+originL2*.035f,new Color(.72f,.55f,.98f));
                DrawModelHandle(1,camera,root+originAxial*.035f,originAxial,L("起点 · 轴向","Origin · Axial"),new Color(.3f,.9f,.9f));
                DrawModelHandle(2,camera,root+originL1*.035f,originL1,L("起点 · L1","Origin · L1"),new Color(.98f,.72f,.3f));
                DrawModelHandle(3,camera,root+originL2*.035f,originL2,L("起点 · L2","Origin · L2"),new Color(.72f,.55f,.98f));
            }
            else for(int i=1;i<=3;i++)modelHandles[i]=new Rect();
            if(showCalibration)
            {
                DrawModelHandle(4,camera,tip+l1*(float)l1HalfRange,l1,"L1 ±"+(l1HalfRange*1000).ToString("F1")+" mm",new Color(.98f,.72f,.3f));
                DrawModelHandle(5,camera,tip+l2*(float)l2HalfRange,l2,"L2 ±"+(l2HalfRange*1000).ToString("F1")+" mm",new Color(.72f,.55f,.98f));
            }
            else{modelHandles[4]=new Rect();modelHandles[5]=new Rect();}
            var e=Event.current;
            if(draggingRod&&(e.type==EventType.MouseDrag||e.rawType==EventType.MouseUp))
            {UpdateRodDrag(e.mousePosition,e.rawType==EventType.MouseUp);e.Use();}
        }
        private void DrawModelHandle(int index,Camera camera,Vector3 world,Vector3 direction,string caption,Color color)
        {
            var point=camera.WorldToScreenPoint(world);
            if(point.z<=0){modelHandles[index]=new Rect();return;}
            var screen=ScreenPoint(point);var rect=new Rect(screen.x-9,screen.y-9,18,18);modelHandles[index]=rect;
            Box(rect,color);Box(new Rect(rect.x+3,rect.y+3,12,12),new Color(.07f,.1f,.13f,.95f));
            GUI.Label(new Rect(rect.x+23,rect.y-3,270,24),caption,small);
            var e=Event.current;
            if(!draggingRod&&e.type==EventType.MouseDown&&e.button==0&&rect.Contains(e.mousePosition)&&!InsideWindows(e.mousePosition))
            {
                draggingRod=true;modelDragHandle=index;draggedBinding=Binding;rodDragStart=e.mousePosition;
                rodDragLength=index==0?PreviewLength:index==4?l1HalfRange:index==5?l2HalfRange:0;originDragStart=Binding.OriginOffset;
                originDragEnd=Binding.ManualEndOffset??Binding.OriginOffset+new MotionVector(Binding.RestLength,0,0);
                var farther=camera.WorldToScreenPoint(world+direction*.1f);rodPixelsPerMetre=(ScreenPoint(farther)-screen)*10;
                // An end-on view uses vertical motion at one millimetre/pixel.
                if(rodPixelsPerMetre.magnitude<100)rodPixelsPerMetre=new Vector2(0,-1000);
                Devices.Stop();e.Use();
            }
        }
        private void UpdateRodDrag(Vector2 mouse,bool released)
        {
            double delta=Vector2.Dot(mouse-rodDragStart,rodPixelsPerMetre)/rodPixelsPerMetre.sqrMagnitude;
            if(modelDragHandle==0)
            {
                double metres=Math.Max(ManualCalibrationMotion.MinimumLength,Math.Min(.5,rodDragLength+delta));
                if(ManualCalibrationActive)SetManualLength(metres,false);
                else if(strokeMappingSetting.Value)SetPlayerLength(metres,false);
                else{Binding.ManualEndOffset=null;Binding.SetLength(metres);SyncMappingLength(metres,false);rodMillimetres=(metres*1000).ToString("F3",CultureInfo.InvariantCulture);Drive.Reset();}
            }
            else if(modelDragHandle>=4)
            {
                double metres=Math.Round(Math.Max(1,Math.Min(500,(rodDragLength+delta)*1000)),1)/1000;
                SetMappingRanges(modelDragHandle==4?metres:l1HalfRange,modelDragHandle==5?metres:l2HalfRange,false);
            }
            else
            {
                var offset=originDragStart;
                if(ManualCalibrationActive&&modelDragHandle==1)offset=ManualCalibrationMotion.MoveOriginAlongAxis(originDragStart,originDragEnd,delta);
                else if(modelDragHandle==1)offset.X=Numbers.Clamp(offset.X+delta,-.5,.5);
                if(modelDragHandle==2)offset.Y=Numbers.Clamp(offset.Y+delta,-.5,.5);
                if(modelDragHandle==3)offset.Z=Numbers.Clamp(offset.Z+delta,-.5,.5);
                try{SetOriginOffset(offset);}catch(ArgumentException){status=L("起点与末端距离须为 1–500 mm","Endpoint distance must be 1-500 mm");}
            }
            if(released){draggingRod=false;if(modelDragHandle==0){if(ManualCalibrationActive)SetManualLength(Binding.RestLength,true);else if(strokeMappingSetting.Value){Config.Save();if(mappingDirty)SaveMappingRanges();}else ApplyLength();}else if(modelDragHandle>=4)SaveMappingRanges();}
        }
        private static void DrawWorldLine(Camera camera,Vector3 a,Vector3 b,Color color)
        {
            var pa=camera.WorldToScreenPoint(a);var pb=camera.WorldToScreenPoint(b);if(pa.z<=0||pb.z<=0)return;
            var start=ScreenPoint(pa);var delta=ScreenPoint(pb)-start;if(delta.sqrMagnitude<.01f)return;
            var matrix=GUI.matrix;GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg,start);
            Box(new Rect(start.x,start.y-1,delta.magnitude,2),color);GUI.matrix=matrix;
        }
    }
}

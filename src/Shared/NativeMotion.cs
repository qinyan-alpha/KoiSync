// Adapted from illusive-groax/KKS_SexRobotController, RobotMovement.cs.
// Copyright (c) 2021 hs2robotics; Copyright (c) 2026 illusive-groax.
// MIT license: embedded KoiSync.Licenses.KKS-SexRobotController-MIT.txt.
using System;

namespace KoiSync.Core
{
    public struct MotionVector
    {
        public double X,Y,Z;
        public MotionVector(double x,double y,double z){X=x;Y=y;Z=z;}
        public static MotionVector operator +(MotionVector a,MotionVector b){return new MotionVector(a.X+b.X,a.Y+b.Y,a.Z+b.Z);}
        public static MotionVector operator -(MotionVector a,MotionVector b){return new MotionVector(a.X-b.X,a.Y-b.Y,a.Z-b.Z);}
        public static MotionVector operator *(MotionVector a,double b){return new MotionVector(a.X*b,a.Y*b,a.Z*b);}
        public double Length{get{return Math.Sqrt(Dot(this,this));}}
        public MotionVector Unit(){double n=Length;if(!Numbers.Finite(n)||n<1e-7)throw new ArgumentException("Degenerate native frame");return this*(1/n);}
        public static double Dot(MotionVector a,MotionVector b){return a.X*b.X+a.Y*b.Y+a.Z*b.Z;}
        public static MotionVector Cross(MotionVector a,MotionVector b){return new MotionVector(a.Y*b.Z-a.Z*b.Y,a.Z*b.X-a.X*b.Z,a.X*b.Y-a.Y*b.X);}
        public static double Angle(MotionVector a,MotionVector b){return Math.Acos(Numbers.Clamp(Dot(a.Unit(),b.Unit()),-1,1))*180/Math.PI;}
    }
    public sealed class NativeMotionSample
    {
        public MotionVector Origin,End,PlanePoint,Target,TargetX,TargetZ;
        public double Gain=1;
        public NativeStrokeProfile Stroke;
        public string AnimationState="";
        public double AnimationProgress;
        public double AnimationDuration;
        public int AnimationSeekRevision;
        public bool StableAnimation;
        public bool Studio;
        public bool FixedRange,Adjusted;
        public double RangeStart,RangeEnd=1;
        public MotionVector DisplayPlane;
        public bool HasBodyFrame,UseReferenceUp;
        public AlignmentFrame BodyFrame,ReferenceFrame;
        public bool HasTargetFrame;
        public bool HasExplicitContact;
        // A game adapter can confirm that the visible tip is pinned by the
        // current native action. This does not imply any material crossed the plane.
        public bool HasNativeTipContact;
        public double ContactTolerance;
        public AlignmentFrame TargetBodyFrame,TargetReferenceFrame;
        public MotionVector AnchorRoot,AnchorEnd,AnchorTarget;
        public string FrameLabel="";
        public bool RootDistance;
        public bool HasShaftPose;
        public MotionVector SourceRootDirection,SourceDeformedDirection;
        // The visible, possibly bent/squashed chain and its fixed material length.
        // BetterPenetration may shorten the visible chain, while an SR6 and the
        // physical shaft represented by it retain a constant length.
        public MotionVector[] ShaftPath;
        public double ShaftRestLength;
        public ContinuousAngle ShaftYaw;
        public bool ShaftTwistHalfTurn;
        internal ShaftContactTracker ShaftContact;
        internal long ContactSequence=-1;
        internal double ContactDeltaTime;
        public double PlaneWidth,PlaneDepth;
        public double LateralSpan=1;
        public int HiddenObjects;
        public readonly double[] Multipliers={1,1,1,1,1,1};
        public bool ObjectVisible(int part){return (HiddenObjects&(1<<part))==0;}
        private double ReferenceLength{get{return HasShaftPose&&Numbers.Finite(ShaftRestLength)&&ShaftRestLength>1e-7?ShaftRestLength:(End-Origin).Length;}}
        public double Width{get{return PlaneWidth>0?PlaneWidth:ReferenceLength;}}
        public double Depth{get{return PlaneDepth>0?PlaneDepth:ReferenceLength;}}
        public StudioContactReference Contact;
        public string RotationBasis="";
        public string RotationLabel{get{return Contact==null?"盆骨线":Contact.Name;}}
        public MotionVector TargetHip;
        public string EndLabel="计算末端";
        public ContinuousAngle R0Continuity;
        public object RotationContext;
        public bool ShapePreview;
        public double ShapeL0=double.NaN;
        public readonly double[] Minimum=new double[6],Maximum={1,1,1,1,1,1};
    }
    public sealed class NativeMotionResult
    {
        public MotionVector X,Y,Z;
        public AlignmentFrame DeviceFrame;
        public double Length;
        // Shaft/contact diagnostics. They remain zero for the legacy root solver.
        public double VisibleLength,Engagement,GeometricEngagement,ContactDistance;
        public double MaterialInsertion,TargetDepth;
        public double[] Raw,Normalized;
    }
    public static class NativeMotion
    {
        public static NativeMotionResult Solve(NativeMotionSample sample)
        {
            if(sample.HasShaftPose&&sample.Contact==null)
                return StudioShaftMotion.Solve(sample,sample.SourceRootDirection,sample.SourceDeformedDirection,sample.ShaftYaw);
            if(sample.RootDistance)
            {
                var result=sample.Contact!=null?StudioContactMotion.Solve(sample):StudioRootMotion.Solve(sample);
                result.Normalized[1]=.5+(result.Normalized[1]-.5)*result.Length/(sample.Width*sample.LateralSpan);
                result.Normalized[2]=.5+(result.Normalized[2]-.5)*result.Length/(sample.Depth*sample.LateralSpan);
                result.Normalized[4]=.5+(result.Normalized[4]-.5)*result.Length/sample.Width;
                return result;
            }
            MotionVector x=(sample.End-sample.Origin).Unit();
            MotionVector up=sample.UseReferenceUp?sample.ReferenceFrame.Y:sample.PlanePoint-sample.Origin;
            MotionVector cross=MotionVector.Cross(x,up);
            if(sample.UseReferenceUp&&cross.Length<.00001)cross=MotionVector.Cross(x,sample.ReferenceFrame.Z);
            MotionVector z=cross.Unit();
            MotionVector y=MotionVector.Cross(x,z).Unit();
            double length=(sample.End-sample.Origin).Length;
            MotionVector delta=sample.Target-sample.Origin;
            // Unity transforms are single precision. Do not flip the frame at a perpendicular boundary due to roundoff.
            bool match=MotionVector.Dot(z,sample.TargetZ.Unit())>=-1e-5;
            double r0=MotionVector.Angle(z,sample.TargetZ),r1=MotionVector.Angle(z,sample.TargetX)-90;
            if(!match){r0=180-r0;r1=-r1;}
            double r2=MotionVector.Angle(y,sample.TargetX)-90;
            bool waist=sample.UseReferenceUp&&sample.HasTargetFrame;
            if(waist){r0=sample.Studio?PelvisTwist.Resolve(sample):BodyTwist.Measure(sample.ReferenceFrame,sample.TargetReferenceFrame,x);if(!Numbers.Finite(r0))r0=0;}
            double[] raw={MotionVector.Dot(x,delta),MotionVector.Dot(y,delta),MotionVector.Dot(z,delta),r0,r1,r2};
            double[] normalized={raw[0]/length,.5+raw[1]/length,.5+raw[2]/length,waist&&sample.Studio?PelvisTwist.Position(r0):.5+r0/(waist?360:180),.5+r1/180,.5+r2/180};
            foreach(double v in normalized)if(!Numbers.Finite(v))throw new ArgumentException("Invalid native sample");
            return new NativeMotionResult{X=x,Y=y,Z=z,Length=length,Raw=raw,Normalized=normalized};
        }
    }
}

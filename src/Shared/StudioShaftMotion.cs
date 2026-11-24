using System;

namespace KoiSync.Core
{
    // ReferenceFrame is the calibrated pelvis/device mounting frame (+Y shaft).
    // The deformed chain observes contact; it must not redefine the device axes.
    public static class StudioShaftMotion
    {
        public static double[] BodyAngles(AlignmentFrame reference,AlignmentFrame target)
        {
            MotionVector x=reference.Local(target.X),y=reference.Local(target.Y),z=reference.Local(target.Z);
            double pitch=Math.Asin(Numbers.Clamp(y.Z,-1,1));
            double yaw=Math.Atan2(-x.Z,z.Z),roll=Math.Atan2(-y.X,y.Y);
            if(Math.Abs(Math.Cos(pitch))<1e-6){yaw=Math.Atan2(z.X,x.X);roll=0;}
            return new[]{yaw*180/Math.PI,roll*180/Math.PI,pitch*180/Math.PI};
        }
        public static double LateralAngle(AlignmentFrame reference,MotionVector direction)
        {
            // Inclination toward either side remains finite when the deformed
            // segment folds backward inside the model. atan2(x,y) would jump
            // by 180 degrees there although no lateral motion occurred.
            MotionVector d=reference.Local(direction.Unit());
            return -Math.Asin(Numbers.Clamp(d.X,-1,1))*180/Math.PI;
        }
        public static AlignmentFrame TwistFrame(AlignmentFrame target,bool halfTurn)
        {
            if(!halfTurn)return target;
            // Bone lateral axes may oppose one another in an untwisted pose.
            // Reverse the two transverse axes, retaining the channel direction.
            return new AlignmentFrame{Origin=target.Origin,X=target.X*-1,Y=target.Y,Z=target.Z*-1};
        }
        public static double PathLength(MotionVector[] path)
        {
            if(path==null||path.Length<2)return 0;
            double total=0;
            for(int i=1;i<path.Length;i++)
            {
                double length=(path[i]-path[i-1]).Length;
                if(!Numbers.Finite(length))throw new ArgumentException("Invalid shaft path");
                total+=length;
            }
            return total;
        }
        public static double InsertionFraction(MotionVector[] path,MotionVector entry,MotionVector inward)
        {
            if(path==null||path.Length<2)throw new ArgumentException("Missing shaft path");
            MotionVector normal=inward.Unit();double total=0,inside=0;
            for(int i=1;i<path.Length;i++)
            {
                MotionVector a=path[i-1],b=path[i];double length=(b-a).Length;
                if(!Numbers.Finite(length))throw new ArgumentException("Invalid shaft path");
                if(length<1e-9)continue;
                double da=MotionVector.Dot(a-entry,normal),db=MotionVector.Dot(b-entry,normal);
                if(!Numbers.Finite(da)||!Numbers.Finite(db))throw new ArgumentException("Invalid entry plane");
                total+=length;
                if(da>=0&&db>=0)inside+=length;
                else if(da>=0||db>=0)
                {
                    double denominator=Math.Abs(da)+Math.Abs(db);
                    if(denominator>1e-12)inside+=length*(da>=0?Math.Abs(da):Math.Abs(db))/denominator;
                }
            }
            if(total<1e-7)throw new ArgumentException("Degenerate source chain");
            return Numbers.Clamp(inside/total,0,1);
        }
        public static void DeviceBasis(AlignmentFrame reference,MotionVector shaftDirection,out MotionVector x,out MotionVector y,out MotionVector z)
        {
            // SR6 X follows the physical shaft.  Project the male pelvis lateral
            // axis onto the plane normal to it, then complete a right-handed
            // basis.  This retains the old X=shaft, Y=POV-forward, Z=POV-right
            // convention without assuming the shaft is exactly pelvis-up.
            x=shaftDirection.Unit();
            MotionVector lateral=reference.X-x*MotionVector.Dot(reference.X,x);
            if(lateral.Length<1e-7)lateral=reference.Z-x*MotionVector.Dot(reference.Z,x);
            z=lateral.Unit();y=MotionVector.Cross(z,x).Unit();
        }
        public static double ClosestDistance(MotionVector[] path,MotionVector point)
        {
            if(path==null||path.Length<2)throw new ArgumentException("Missing shaft path");
            double best=double.MaxValue;
            for(int i=1;i<path.Length;i++)
            {
                MotionVector a=path[i-1],segment=path[i]-a;double square=MotionVector.Dot(segment,segment);
                if(!Numbers.Finite(square))throw new ArgumentException("Invalid shaft path");
                double t=square<1e-14?0:Numbers.Clamp(MotionVector.Dot(point-a,segment)/square,0,1);
                best=Math.Min(best,(point-(a+segment*t)).Length);
            }
            if(!Numbers.Finite(best))throw new ArgumentException("Invalid shaft contact distance");
            return best;
        }
        public static double ContactEngagement(MotionVector[] path,MotionVector entry,MotionVector inward,double length,out double distance)
        {return ContactEngagement(path,entry,inward,length,out distance,0);}
        public static double ContactEngagement(MotionVector[] path,MotionVector entry,MotionVector inward,double length,out double distance,double contactTolerance)
        {
            // BP's curved, discretized chain can graze the entrance without an
            // inward plane crossing. Measure the nearest FINITE segment, with
            // its endpoints included. The plane separately gates insertion;
            // a distant point on the infinite shaft axis cannot engage.
            distance=ClosestDistance(path,entry);
            double near=Math.Max(Math.Max(.010,length*.055),contactTolerance*.5);
            double far=Math.Max(Math.Max(.035,length*.20),contactTolerance);
            if(distance<=near)return 1;
            if(distance>=far)return 0;
            double t=1-(distance-near)/(far-near);
            return t*t*(3-2*t);
        }
        // A matching BP/constraint binding supplies contact intent, but never
        // bypasses finite chain distance or the visible entry-plane check.
        // Use a continuous release envelope instead of the unbound dropout timer.
        public static double BoundContactEngagement(double distance,double length)
        {
            double near=Math.Max(.045,length*.25),far=Math.Max(near+.015,length*.40);
            double t=Numbers.Clamp((far-distance)/(far-near),0,1);
            return t*t*(3-2*t);
        }
        public static bool NativeTipReachesEntry(double distance,double length)
        {return Numbers.Finite(distance)&&distance>=0&&distance<=Math.Max(.020,length*.15);}

        public static NativeMotionResult Solve(NativeMotionSample s,MotionVector rootDirection,MotionVector deformedDirection,ContinuousAngle yawContinuity)
        {
            MotionVector[] path=s.ShaftPath;
            if(path==null||path.Length<2)path=new[]{s.Origin,s.End};
            double visibleLength=PathLength(path);
            double length=Numbers.Finite(s.ShaftRestLength)&&s.ShaftRestLength>1e-7?s.ShaftRestLength:visibleLength;
            if(!Numbers.Finite(length)||length<1e-7)throw new ArgumentException("Degenerate source chain");
            MotionVector x,y,z;DeviceBasis(s.ReferenceFrame,s.ReferenceFrame.Y,out x,out y,out z);
            MotionVector root=s.Origin,tip=root+x*length,entry=s.TargetReferenceFrame.Origin;
            var deviceFrame=new AlignmentFrame{Origin=root,X=z,Y=x,Z=y};
            double measuredInsertion=InsertionFraction(path,entry,s.TargetReferenceFrame.Y),contactDistance;
            double geometric=ContactEngagement(path,entry,s.TargetReferenceFrame.Y,length,out contactDistance,s.ContactTolerance);
            // Deep bottom-out (e.g. cowgirl SLoop): when entry slides past root with
            // material fully inside, finite-segment distance to path[0] inflates by |axial|
            // even though the shaft is buried inside the opening. Use radial distance to
            // the shaft axis so full insertion never collapses to Engagement=0.
            double axialEntry=MotionVector.Dot(entry-root,x);
            double maxOverInsert=Math.Min(.065,length*.60);
            if(axialEntry<=.015&&axialEntry>=-Math.Max(maxOverInsert,length*.55))
            {
                double radial=((entry-root)-x*axialEntry).Length;
                bool deepExplicit=(s.HasNativeTipContact||s.HasExplicitContact)&&measuredInsertion>=.45&&axialEntry>=-length*.55;
                bool deepStudioBuried=s.Studio&&s.ShaftContact!=null&&measuredInsertion>=.85&&axialEntry>=-maxOverInsert&&radial<=Math.Min(.032,length*.25);
                if((deepExplicit||deepStudioBuried)&&radial<contactDistance)
                {
                    contactDistance=radial;
                    geometric=BoundContactEngagement(contactDistance,length);
                }
            }
            bool pinned=s.HasNativeTipContact&&NativeTipReachesEntry(contactDistance,length);
            if(s.HasExplicitContact)geometric=measuredInsertion<=1e-7?(s.ContactTolerance>0?geometric:0):BoundContactEngagement(contactDistance,length);
            if(pinned)geometric=BoundContactEngagement(contactDistance,length);
            double engagement=pinned||s.HasExplicitContact||s.ShaftContact==null?geometric:s.ShaftContact.Peek(s,geometric,measuredInsertion,contactDistance,length);
            double targetDepth=length-MotionVector.Dot(entry-root,x);
            // A shortened visible tip that has not reached the entrance is still
            // withdrawn, even if the calibrated straight rod would reach it.
            double primary=measuredInsertion<=1e-7&&!pinned?0:Numbers.Clamp(targetDepth/length,0,1)*engagement;
            MotionVector lateral=entry-tip;
            // y = UserFwd (TCode L1 > 0.5 = Forward), z = UserRight (TCode L2 > 0.5 = UserLeft = -z).
            double l1=MotionVector.Dot(lateral,y)*engagement,l2=-MotionVector.Dot(lateral,z)*engagement;
            var twistFrame=TwistFrame(s.TargetReferenceFrame,s.ShaftTwistHalfTurn);
            // Correct the complete frame before decomposing or blending it.
            // Folding yaw alone modulo 180 corrupts the Euler triple when
            // pitch crosses 90 degrees, and invents a half-turn of R0. The
            // inverse solver consumes a full pose, not an unwrapped yaw axis.
            double[] body=BodyAngles(deviceFrame,FrameRotation.Blend(deviceFrame,twistFrame,engagement));
            double yaw=body[0];
            double roll=body[1],pitch=body[2];
            double[] raw={primary*length,l1,l2,yaw,roll,pitch};
            double normalizedL1=.5+raw[1]/(s.Width*s.LateralSpan),normalizedL2=.5+raw[2]/(s.Depth*s.LateralSpan);
            var rotation=engagement<=0?new[]{.5,.5,.5}:SR6DeviceRotation.Positions(yaw,roll,pitch,1-primary,normalizedL1,normalizedL2);
            double[] normalized={primary,normalizedL1,normalizedL2,rotation[0],rotation[1],rotation[2]};
            foreach(double n in normalized)if(!Numbers.Finite(n))throw new ArgumentException("Invalid shaft pose");
            return new NativeMotionResult{X=x,Y=y,Z=z,DeviceFrame=deviceFrame,Length=length,VisibleLength=visibleLength,Engagement=engagement,GeometricEngagement=geometric,ContactDistance=contactDistance,MaterialInsertion=measuredInsertion,TargetDepth=targetDepth,Raw=raw,Normalized=normalized};
        }
    }
}

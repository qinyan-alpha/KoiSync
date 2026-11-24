using System;

namespace KoiSync.Core
{
    // SR6 carrier kinematics adapted from the MIT nb-3d-simulator model.
    // Source: nbnb9527/nb-3d-simulator, bundled reference retrieved 2026-10-05.
    // Keep its servo constants and linkage dimensions: the coupled rotation
    // must match the reference model, including its small numerical offsets.
    public static class SR6DeviceRotation
    {
        private static readonly MotionVector UnitX=new MotionVector(1,0,0),UnitY=new MotionVector(0,1,0),UnitZ=new MotionVector(0,0,1);
        private static double Clamp(double value){return Numbers.Clamp(value,0,.9999);}
        private static double Travel(double value,double span){return (Clamp(value)/.9999*2-1)*span;}
        private static double Servo(double horizontal,double vertical)
        {
            horizontal/=100;vertical/=100;double square=horizontal*horizontal+vertical*vertical;
            return 637*(Math.Atan2(horizontal,vertical)+Math.Acos(Numbers.Clamp((square-28125)/(100*Math.Sqrt(square)),-1,1))-3.14159);
        }
        private static double ServoAngle(double pulse,double sign){return (pulse/(1000000.0/330)*2-1)*Math.PI*sign;}
        private static MotionVector Arm(double x,double y,double angle,double side)
        {return new MotionVector(x+14.5*side,y-50*Math.Cos(angle),49.92-50*Math.Sin(angle));}
        private static MotionVector Intersection(MotionVector upper,MotionVector lower,out MotionVector center,out double radius)
        {
            center=(upper+lower)*.5;var delta=upper-lower;
            radius=Math.Sqrt(Math.Max(0,175*175-MotionVector.Dot(delta,delta)*.25));
            return center+MotionVector.Cross(delta,UnitX).Unit()*radius;
        }
        private static MotionVector TurnY(MotionVector point,double angle)
        {return new MotionVector(point.X*Math.Cos(angle)+point.Z*Math.Sin(angle),point.Y,-point.X*Math.Sin(angle)+point.Z*Math.Cos(angle));}
        private static MotionVector FromTo(MotionVector point,MotionVector cross,double dot)
        {return point+MotionVector.Cross(cross,point)+MotionVector.Cross(cross,MotionVector.Cross(cross,point))*(1/(1+dot));}
        public static AlignmentFrame Carrier(double l0,double l1,double l2,double r1)
        {
            double roll=Travel(r1,3000),surge=Travel(l1,3000),stroke=Travel(l0,6000);
            double a=Servo(16248-surge,1500+stroke+roll),b=Servo(16248-surge,1500-stroke-roll);
            double c=Servo(16248-surge,1500-stroke+roll),d=Servo(16248-surge,1500+stroke-roll);
            double lowerLeft=ServoAngle(1515.105-a,.5)-Math.PI,upperLeft=ServoAngle(1515.105+b,.5);
            double lowerRight=ServoAngle(1515.105+d,-.5)-Math.PI,upperRight=ServoAngle(1515.105-c,-.5);
            MotionVector leftCenter,rightCenter;double leftRadius,rightRadius;
            var left=Intersection(Arm(58.5,0,upperLeft,1),Arm(58.5,30,lowerLeft,1),out leftCenter,out leftRadius);
            var right=Intersection(Arm(-58.5,0,upperRight,-1),Arm(-58.5,30,lowerRight,-1),out rightCenter,out rightRadius);
            double distance=(right-left).Length;
            if(distance>145.5){var adjustment=(right-left).Unit()*((distance-145.5)*.5);left=left+adjustment;right=right-adjustment;}
            double sway=60*(Clamp(l2)-.5);
            left=leftCenter+TurnY(left-leftCenter,sway/leftRadius);right=rightCenter+TurnY(right-rightCenter,sway/rightRadius);
            var from=UnitX*-1;var to=(right-left).Unit();var cross=MotionVector.Cross(from,to);double dot=MotionVector.Dot(from,to);
            return new AlignmentFrame{X=FromTo(UnitX,cross,dot),Y=FromTo(UnitY,cross,dot),Z=FromTo(UnitZ,cross,dot)};
        }
        public static AlignmentFrame Frame(double l0,double l1,double l2,double r0,double r1,double r2)
        {
            var carrier=Carrier(l0,l1,l2,r1);
            var controls=ContactPreviewMotion.BodyFrame(new AlignmentFrame{X=UnitX,Y=UnitY,Z=UnitZ},new MotionVector(),(.5-Clamp(r0))*240,0,(Clamp(r2)-.5)*50);
            return new AlignmentFrame{X=carrier.World(controls.X),Y=carrier.World(controls.Y),Z=carrier.World(controls.Z)};
        }
        public static double[] Positions(double yaw,double roll,double pitch,double l0,double l1,double l2)
        {
            var desired=ContactPreviewMotion.BodyFrame(new AlignmentFrame{X=UnitX,Y=UnitY,Z=UnitZ},new MotionVector(),yaw,roll,pitch);
            double low=0,high=.9999;
            double left=SideTiltError(Carrier(l0,l1,l2,low),desired.Y),right=SideTiltError(Carrier(l0,l1,l2,high),desired.Y);
            double r1;
            if(left*right>0)
            {
                // Retain the unreachable roll request for gain/soft limiting.
                // Carrier itself always clamps to the reference mechanism. Use
                // its local angular slope at the closest endpoint to extend the
                // command coordinate, not the physical servo travel. Evaluating
                // at the current L0/L1/L2 also retains their coupled roll range.
                double edge=Math.Abs(left)<Math.Abs(right)?low:high;
                double inner=edge==low?.01:high-.01;
                double error=SideTiltError(Carrier(l0,l1,l2,edge),desired.Y);
                double slope=(SideTiltError(Carrier(l0,l1,l2,inner),desired.Y)-error)/(inner-edge);
                r1=Math.Abs(slope)>1e-7?edge-error/slope:edge;
                // The extension must remain on the selected side of the range.
                r1=edge==low?Math.Min(low,r1):Math.Max(high,r1);
            }
            else
            {
                for(int i=0;i<26;i++)
                {double mid=(low+high)*.5,value=SideTiltError(Carrier(l0,l1,l2,mid),desired.Y);if(left*value>0){low=mid;left=value;}else high=mid;}
                r1=(low+high)*.5;
            }
            var carrier=Carrier(l0,l1,l2,r1);var y=carrier.Local(desired.Y);var x=carrier.Local(desired.X);var z=carrier.Local(desired.Z);
            double correctedPitch=Math.Atan2(y.Z,y.Y)*180/Math.PI,correctedYaw=Math.Atan2(z.X,x.X)*180/Math.PI;
            return new[]{.5-correctedYaw/240,r1,.5+correctedPitch/50};
        }
        private static double SideTiltError(AlignmentFrame carrier,MotionVector desired)
        {
            var local=carrier.Local(desired);
            // Match the observed normal's lateral tilt, not its azimuth in XY.
            // That azimuth becomes ill-conditioned near +/-90 degrees of pitch:
            // a small sideways motion can otherwise demand the entire R1 range.
            // R1 must not compensate for pitch the mechanism cannot execute.
            // Only this forward comparison uses the available R2 travel. The
            // original full frame and the unbounded R0/R2 requests stay intact.
            double pitch=Numbers.Clamp(Math.Atan2(local.Z,local.Y),-.5*50*Math.PI/180,(.9999-.5)*50*Math.PI/180);
            double lateral=carrier.Y.X*Math.Cos(pitch)+carrier.Z.X*Math.Sin(pitch);
            return Math.Asin(Numbers.Clamp(desired.X,-1,1))-Math.Asin(Numbers.Clamp(lateral,-1,1));
        }
    }
}

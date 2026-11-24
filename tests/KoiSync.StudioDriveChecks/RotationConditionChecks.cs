using KoiSync.Core;

static class RotationConditionChecks
{
    public static void Run(Action<bool,string> check)
    {
        var identity=AlignmentFrame.Build(new(),new(1,0,0),new(0,1,0));
        double[] Solve(AlignmentFrame frame,double[] linear)
        {
            var a=StudioShaftMotion.BodyAngles(identity,frame);
            return SR6DeviceRotation.Positions(a[0],a[1],a[2],linear[0],linear[1],linear[2]);
        }
        AlignmentFrame Tilt(double side,double pitch)
        {
            // Keep actual lateral inclination fixed while crossing the Euler
            // pitch singularity. Normal.X remains observable at +/-90 degrees.
            side*=Math.PI/180;pitch*=Math.PI/180;
            return AlignmentFrame.Build(new(),new(1,0,0),new(-Math.Sin(side),Math.Cos(side)*Math.Cos(pitch),Math.Cos(side)*Math.Sin(pitch)));
        }
        foreach(var linear in new[]{new[]{.5,.5,.5},new[]{.1,.1,.2},new[]{.8,.9,.7}})
        foreach(double sign in new[]{-1.0,1})
        {
            foreach(double side in new[]{-3.0,0,3})
            {
                var requests=new[]{80.0,89.9,90,90.1,100,110}.Select(p=>Solve(Tilt(side,sign*p),linear)).ToArray();
                check(requests.All(r=>r.All(Numbers.Finite)),"Pitch singularity has finite rotation demands");
                check(requests.Max(r=>r[1])-requests.Min(r=>r[1])<1e-6,
                    "Fixed lateral inclination does not sweep R1 as unreachable pitch crosses 90 degrees");
                check(requests.All(r=>sign<0?r[2]<0:r[2]>1),"Unreachable pitch remains available before gain and mapping");
                var small=Solve(Tilt(side-.1,sign*90),linear)[1]-Solve(Tilt(side+.1,sign*90),linear)[1];
                check(small>0&&small<.02,"Small real side tilt remains responsive and bounded at the pitch singularity");
            }
        }
        // The new residual must still invert the physical mechanism exactly,
        // including the coupled carrier at edge combinations of all six axes.
        foreach(double l0 in new[]{0.0,.5,.9999})
        foreach(double l1 in new[]{0.0,.5,.9999})
        foreach(double l2 in new[]{0.0,.5,.9999})
        foreach(double r0 in new[]{.15,.5,.85})
        foreach(double r1 in new[]{0.0,.5,.9999})
        foreach(double r2 in new[]{0.0,.5,.9999})
        {
            var target=SR6DeviceRotation.Frame(l0,l1,l2,r0,r1,r2);
            var r=Solve(target,new[]{l0,l1,l2});
            var restored=SR6DeviceRotation.Frame(l0,l1,l2,r[0],r[1],r[2]);
            check((restored.X-target.X).Length<1e-7&&(restored.Y-target.Y).Length<1e-7&&(restored.Z-target.Z).Length<1e-7,
                "Reachable six-axis frame survives the conditioned inverse: "+string.Join(",",new[]{l0,l1,l2,r0,r1,r2}));
        }
    }
}

using KoiSync.Core;
using KoiSync.Studio;

internal static class RotationLimitChecks
{
    public static void Run(Action<bool,string> check)
    {
        void Near(double a,double b,string label)=>check(Math.Abs(a-b)<1e-8,label);
        var drive=new StudioDrive();
        double Run(int axis,double request)
        {
            var values=new[]{.7,.4,.6,.5,.5,.5};values[axis]=request;
            drive.Reset();drive.Update(values,.02);return drive.Values[axis];
        }
        foreach(int axis in new[]{3,4,5})
        {
            check(new[]{.3,.4,.5,.6,.7}.All(v=>Math.Abs(Run(axis,v)-v)<1e-10),"R"+(axis-3)+" central rotation range stays exact");
            var requested=new[]{-2.0,-1,-.5,-.1,0,.25,.5,.75,1,1.1,1.5,2,3};
            var mapped=requested.Select(v=>Run(axis,v)).ToArray();
            check(mapped.All(v=>v>0&&v<1)&&mapped.Zip(mapped.Skip(1),(a,b)=>b>a).All(v=>v),"R"+(axis-3)+" keeps signed over-range motion ordered inside endpoints");
            Near(Run(axis,-.6)+Run(axis,1.6),1,"R"+(axis-3)+" symmetric shoulder");
            drive.Gain[axis]=.1;Near(Run(axis,-1),.35,"R"+(axis-3)+" gain acts on full request before clipping");
            drive.Gain[axis]=0;Near(Run(axis,3),.5,"R"+(axis-3)+" zero gain is neutral");drive.Gain[axis]=1;
            drive.Minimum[axis]=.2;drive.Maximum[axis]=.8;
            Near(Run(axis,-2),.2,"R"+(axis-3)+" manual lower limit retained");Near(Run(axis,3),.8,"R"+(axis-3)+" manual upper limit retained");
            drive.Minimum[axis]=0;drive.Maximum[axis]=1;
        }
        drive.SoftTwistLimit=drive.SoftRollLimit=drive.SoftPitchLimit=false;
        foreach(int axis in new[]{3,4,5})Near(Run(axis,-1),0,"R"+(axis-3)+" direct mapping remains selectable");
        drive.SoftTwistLimit=drive.SoftRollLimit=drive.SoftPitchLimit=true;
        drive.Enabled[3]=false;Run(3,-1);check(!drive.Enabled[3],"Mapping never enables a disabled output channel");
        var script=new[]{.2,.3,.4,0.0,1,0};drive.UpdateScript(script);
        check(drive.Values.SequenceEqual(script),"All final script rotations bypass soft mapping");
        drive.Reset();drive.Update(new[]{.5,.5,.5,-1.0,-1,-1},.02);
        var previous=(double[])drive.Values.Clone();drive.Update(new[]{.5,.5,.5,2.0,2,2},.02);
        check(new[]{3,4,5}.All(axis=>drive.Values[axis]>previous[axis]&&drive.Values[axis]-previous[axis]<=.040000001),"All rotations retain bounded transitions");

        var identity=AlignmentFrame.Build(new(),new(1,0,0),new(0,1,0));
        foreach(var linear in new[]{new[]{.5,.5,.5},new[]{.1,.1,.2},new[]{.8,.9,.7}})
        {
            foreach(double pitch in new[]{-65.0,-20,0,20,65})
            {
                var rolls=new[]{-65.0,-40,-25,-15,-5,0,5,15,25,40,65};
                var requests=rolls.Select(roll=>SR6DeviceRotation.Positions(0,roll,pitch,linear[0],linear[1],linear[2])[1]).ToArray();
                check(requests.All(Numbers.Finite)&&requests.Zip(requests.Skip(1),(a,b)=>b<a).All(v=>v),"Travel-dependent roll inverse preserves outside motion at pitch "+pitch+" / L0 "+linear[0]);
            }
            var forty=SR6DeviceRotation.Positions(0,40,0,linear[0],linear[1],linear[2]);
            var seventy=SR6DeviceRotation.Positions(0,70,0,linear[0],linear[1],linear[2]);
            check(seventy[1]<forty[1]&&forty[1]<0,"Two formerly clipped roll poses retain distinct unbounded requests");
            check(Run(4,seventy[1])<Run(4,forty[1]),"Roll differences survive the full inverse and drive pipeline");
            foreach(double edge in new[]{0.0,.9999})
            {
                var frame=SR6DeviceRotation.Frame(linear[0],linear[1],linear[2],.5,edge,.5);
                var angles=StudioShaftMotion.BodyAngles(identity,frame);
                var request=SR6DeviceRotation.Positions(angles[0],angles[1]+(edge==0?.001:-.001),angles[2],linear[0],linear[1],linear[2]);
                check(Math.Abs(request[1]-edge)<.001&&(edge==0?request[1]<0:request[1]>.9999),"Roll request is continuous across a physical endpoint");
                var clamped=SR6DeviceRotation.Frame(linear[0],linear[1],linear[2],.5,edge==0?-2:3,.5);
                check((clamped.Y-frame.Y).Length<1e-12,"Unbounded request never expands physical reference-model travel");
            }
        }
    }
}

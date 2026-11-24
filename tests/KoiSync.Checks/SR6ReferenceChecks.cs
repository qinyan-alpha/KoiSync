using System.Text.Json;
using KoiSync.Core;

internal static class SR6ReferenceChecks
{
    public static void Run(Action<bool,string> check)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixtures","sr6-reference-rotations.json")));
        int index=0;
        foreach(var fixture in document.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            var a=fixture.GetProperty("axes").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
            var q=fixture.GetProperty("quaternion").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
            double x=q[0],y=q[1],z=q[2],w=q[3];
            var expected=new AlignmentFrame{X=new(1-2*(y*y+z*z),2*(x*y+w*z),2*(x*z-w*y)),Y=new(2*(x*y-w*z),1-2*(x*x+z*z),2*(y*z+w*x)),Z=new(2*(x*z+w*y),2*(y*z-w*x),1-2*(x*x+y*y))};
            var actual=SR6DeviceRotation.Frame(a[0],a[1],a[2],a[3],a[4],a[5]);
            check((actual.X-expected.X).Length<1e-10&& (actual.Y-expected.Y).Length<1e-10&& (actual.Z-expected.Z).Length<1e-10,"SR6 original-model quaternion fixture "+index);
            var identity=new AlignmentFrame{X=new(1,0,0),Y=new(0,1,0),Z=new(0,0,1)};
            var angles=StudioShaftMotion.BodyAngles(identity,expected);
            var encoded=SR6DeviceRotation.Positions(angles[0],angles[1],angles[2],a[0],a[1],a[2]);
            var restored=SR6DeviceRotation.Frame(a[0],a[1],a[2],encoded[0],encoded[1],encoded[2]);
            check((restored.X-expected.X).Length<1e-7&&(restored.Y-expected.Y).Length<1e-7&&(restored.Z-expected.Z).Length<1e-7,"SR6 coupled inverse restores original-model pose "+index);
            index++;
        }
    }
}

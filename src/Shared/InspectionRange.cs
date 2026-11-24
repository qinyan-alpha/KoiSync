using System;

namespace KoiSync.Core
{
    // Read-only observation history; does not normalize or drive device output.
    public sealed class InspectionRange
    {
        private string identity;
        private long sequence=-1;
        public readonly double[] Minimum=new double[6],Maximum=new double[6];
        public int Samples {get;private set;}
        public InspectionRange(){Reset();}
        public void Reset(){Samples=0;sequence=-1;for(int i=0;i<6;i++){Minimum[i]=double.PositiveInfinity;Maximum[i]=double.NegativeInfinity;}}
        public void Observe(string key,long sample,double[] values)
        {
            if(key!=identity){Reset();identity=key;}
            if(sample<=sequence||values==null||values.Length!=6)return;
            for(int i=0;i<6;i++)if(!Numbers.Finite(values[i]))return;
            sequence=sample;Samples++;
            for(int i=0;i<6;i++){Minimum[i]=Math.Min(Minimum[i],values[i]);Maximum[i]=Math.Max(Maximum[i],values[i]);}
        }
    }
}

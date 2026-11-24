namespace KoiSync.Core
{
    public static class ShapeReference
    {
        public static double Normalize(double weight,double zero,double full)
        {
            if(!Numbers.Finite(weight)||!Numbers.Finite(zero)||!Numbers.Finite(full)||System.Math.Abs(full-zero)<.000001)return double.NaN;
            return (weight-zero)/(full-zero);
        }
    }
}

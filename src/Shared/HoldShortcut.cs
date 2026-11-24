namespace KoiSync.Core
{
    public sealed class HoldShortcut
    {
        private double began=-1;
        private bool fired;
        public bool Update(bool held,double now,double duration)
        {
            if(!held||!Numbers.Finite(now)){began=-1;fired=false;return false;}
            if(began<0||now<began)began=now;
            if(fired||now-began<duration)return false;
            fired=true;return true;
        }
    }
}

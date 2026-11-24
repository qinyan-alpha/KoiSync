using System;
namespace KoiSync.Studio
{
    public static class CameraPairRanking
    {
        public static double ContactLimit(double length)
        {return Math.Max(.035,Math.Min(.07,length*.3));}
        public static bool AttachmentMatchesContact(double distance,double length)
        {return distance>=0&&distance<=ContactLimit(length);}
        public static bool KeepContact(double currentDistance,double bestDistance,double limit)
        {return currentDistance<limit&&currentDistance-bestDistance<.0075;}
        public static bool AcquireCavity(double distance,double length,double insertion)
        {return distance>=0&&distance<=Math.Max(.018,Math.Min(.026,length*.12))&&(insertion>.02||distance<=.005);}
        public static bool PreferCavityToGrip(double distance,double length,double insertion)
        {return insertion>.02&&AcquireCavity(distance,length,insertion);}
        public static bool KeepCavityOverGrip(double distance,double length,double insertion)
        {return distance>=0&&(distance<=.018||insertion>.02&&distance<=Math.Max(.035,length*.20));}
        public static bool PreferContact(int kind,bool confirmedCavity,double distance,int previousKind,bool previousConfirmedCavity,double previousDistance)
        {
            if(previousKind>=3&&confirmedCavity)return true;
            if(kind>=3&&previousConfirmedCavity)return false;
            return distance<previousDistance;
        }
        public static bool KeepCavity(double currentDistance,double bestDistance,double length,double insertion)
        {
            // Adjacent entrances can both be close to a deformed shaft. Keep
            // an ongoing insertion unless it leaves the channel or the other
            // entrance has a much clearer centre-line contact.
            return insertion>.02&&currentDistance<=Math.Max(.035,length*.20)&&!(bestDistance<=.005&&currentDistance>=.025);
        }
        public static double Score(double x,double y,double depth,double aspect)
        {
            if(depth<=0||x<0||x>1||y<0||y>1)return -1;
            double dx=(x-.5)*aspect,dy=y-.5;
            return 1/(1+8*(dx*dx+dy*dy));
        }
    }
}

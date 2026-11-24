using System;

namespace KoiSync.Core
{
    // Hysteresis belongs to the sampled output stream, not diagnostic solves.
    // Only a confirmed entrance may bridge a brief, bounded BP-chain dropout.
    internal sealed class ShaftContactTracker
    {
        internal const double HoldSeconds=2.0;
        private bool engaged;
        private double missedSeconds,committedWeight;
        private NativeMotionSample committedSample;
        private long committedSequence=-1;

        internal void Reset()
        {engaged=false;missedSeconds=0;committedSample=null;committedSequence=-1;}

        internal double Peek(NativeMotionSample sample,double geometric,double material,double distance,double length)
        {
            if(ReferenceEquals(sample,committedSample)&&sample.ContactSequence==committedSequence)return committedWeight;
            bool next;double missed;
            return Evaluate(sample,geometric,material,distance,length,out next,out missed);
        }

        internal void Commit(NativeMotionSample sample,NativeMotionResult measured)
        {
            if(ReferenceEquals(sample,committedSample)&&sample.ContactSequence==committedSequence)return;
            bool next;double missed;
            Evaluate(sample,measured.GeometricEngagement,measured.MaterialInsertion,measured.ContactDistance,measured.Length,out next,out missed);
            engaged=next;missedSeconds=missed;
            committedSample=sample;committedSequence=sample.ContactSequence;committedWeight=measured.Engagement;
        }

        private double Evaluate(NativeMotionSample sample,double geometric,double material,double distance,double length,out bool next,out double missed)
        {
            next=false;missed=0;
            // No visible material inside: withdraw immediately, even during the
            // grace interval. A distant target never inherits contact history.
            if(material<=1e-7)return geometric;
            if(geometric>=.9){next=true;return 1;}
            double far=Math.Max(.035,length*.20);
            if(!engaged||distance>=2*far)return geometric;
            missed=distance>far?missedSeconds+Math.Max(0,sample.ContactDeltaTime):0;
            if(missed>HoldSeconds+1e-9){missed=0;return geometric;}
            next=true;return 1;
        }
    }
}

using System;

namespace KoiSync.Studio
{
    // Only continuous playback can confirm a new chapter. A paused/seeked pose
    // may change the preview immediately, without becoming a recorded boundary.
    public sealed class ChapterSelection
    {
        public string Identity {get;private set;}="";
        public double Start {get;private set;}
        public bool Continuous {get;private set;}
        private string pending="";
        private double pendingAt,lastTime=-1,lastWall;
        private int observations;
        private bool wasPlaying;
        public void Reset(){Identity=pending="";lastTime=-1;wasPlaying=Continuous=false;observations=0;}
        public string Choose(string candidate,double time,double wall,bool playing,double rate,bool immediate)
        {
            candidate=candidate??"";
            double elapsed=Math.Max(0,wall-lastWall),expected=elapsed*Math.Max(0,rate),delta=time-lastTime;
            Continuous=lastTime>=0&&wasPlaying&&playing&&delta>=-.001&&elapsed<1.5&&Math.Abs(delta-expected)<=Math.Max(.12,expected*.6);
            lastTime=time;lastWall=wall;wasPlaying=playing;
            if(!Continuous||Identity.Length==0||immediate)
            {
                if(Identity!=candidate||!Continuous)Start=time;
                Identity=candidate;pending="";observations=0;return Identity;
            }
            if(candidate==Identity){pending="";observations=0;return Identity;}
            if(pending!=candidate){pending=candidate;pendingAt=time;observations=1;}
            else observations++;
            if(observations>=3&&time-pendingAt>=.65)
            {Identity=candidate;Start=pendingAt;pending="";observations=0;}
            return Identity;
        }
    }
}

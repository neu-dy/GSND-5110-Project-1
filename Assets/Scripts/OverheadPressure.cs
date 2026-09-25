using System;

// Shared progression and immutable lane plan for drops and ceiling tentacles.
public static class OverheadPressure
{
    [Serializable]
    public sealed class Settings
    {
        public bool enabled = true;
        public int maximumCount = 3;
        public float doubleAtMeters = 160f, tripleAtMeters = 420f;
        public float doubleAtSeconds = 60f, tripleAtSeconds = 150f;
        public float warningRampMeters = 800f, warningRampSeconds = 240f;
        public float minimumWarningSeconds = 1.1f;
        public float simultaneousChance = .45f;
        public float staggerSeconds = .24f;
        public float laneGap = .12f;
        public void Validate()
        {
            maximumCount=Math.Max(1,Math.Min(3,maximumCount));
            doubleAtMeters=Math.Max(1,doubleAtMeters);tripleAtMeters=Math.Max(doubleAtMeters+1,tripleAtMeters);
            doubleAtSeconds=Math.Max(1,doubleAtSeconds);tripleAtSeconds=Math.Max(doubleAtSeconds+1,tripleAtSeconds);
            warningRampMeters=Math.Max(1,warningRampMeters);warningRampSeconds=Math.Max(1,warningRampSeconds);
            minimumWarningSeconds=Math.Max(1f,minimumWarningSeconds);
            simultaneousChance=Math.Max(0,Math.Min(1,simultaneousChance));
            staggerSeconds=Math.Max(.12f,Math.Min(.6f,staggerSeconds));laneGap=Math.Max(.05f,Math.Min(.5f,laneGap));
        }
    }
    public sealed class Plan
    {
        public readonly float[] Centers;
        public readonly float Warning, Stagger, TotalSeconds;
        public Plan(float[] centers,float warning,float stagger,float activeSeconds)
        {Centers=(float[])centers.Clone();Warning=warning;Stagger=stagger;TotalSeconds=warning+stagger*(centers.Length-1)+activeSeconds;}
    }
    public static int DesiredCount(Settings settings,double meters,float seconds)
    {
        if(!settings.enabled)return 1;
        int n=meters>=settings.tripleAtMeters||seconds>=settings.tripleAtSeconds?3
            :meters>=settings.doubleAtMeters||seconds>=settings.doubleAtSeconds?2:1;
        return Math.Min(n,settings.maximumCount);
    }
    public static float Warning(Settings s,double meters,float seconds,float original)
    {
        if(!s.enabled)return original;
        float progress=(float)Math.Max(meters/s.warningRampMeters,seconds/s.warningRampSeconds);
        progress=Math.Max(0,Math.Min(1,progress));
        return original+(Math.Min(original,s.minimumWarningSeconds)-original)*progress;
    }
    public static Plan Choose(Settings s,double meters,float seconds,float start,float min,float max,
        float width,float originalWarning,float activeSeconds,bool simultaneous,bool reverse,
        Func<float[],float,float,bool> safe)
    {
        s.Validate();float initial=Warning(s,meters,seconds,originalWarning);
        int desired=DesiredCount(s,meters,seconds);
        for(int count=desired;count>=1;count--)
        for(int direction=0;direction<2;direction++)
        {
            float sign=(direction==0?1:-1)*(reverse?-1:1),step=width+s.laneGap;
            var centers=new float[count];bool fits=true;
            for(int i=0;i<count;i++)
            {
                centers[i]=start+sign*i*step;
                if(centers[i]<min-.001f||centers[i]>max+.001f)fits=false;
            }
            if(!fits)continue;
            float stagger=simultaneous?0:s.staggerSeconds;
            int attempts=(int)Math.Ceiling(Math.Max(0,originalWarning-initial)/.05f);
            for(int attempt=0;attempt<=attempts;attempt++)
            {
                float warning=Math.Min(originalWarning,initial+attempt*.05f);
                var plan=new Plan(centers,warning,stagger,activeSeconds);
                if(safe(centers,warning,plan.TotalSeconds))return plan;
            }
        }
        return null;
    }
}

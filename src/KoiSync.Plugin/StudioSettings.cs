using BepInEx.Configuration;
using KoiSync.Core;

namespace KoiSync.Plugin
{
    internal sealed class StudioSettings
    {
        internal readonly ConfigEntry<float> Gain,LengthScale;
        internal readonly ConfigEntry<bool> ReverseCollider;
        internal readonly ConfigEntry<bool> AutoSource;
        internal readonly ConfigEntry<NativeTarget> Target;
        internal StudioSettings(ConfigFile config)
        {
            AutoSource=config.Bind("Studio","AutoSelectSource",true,"Follow visible playing bindings by camera direction and distance; nearby ties prefer BP, then vagina. Manual selection disables this option.");
            Gain=config.Bind("Studio","L0Gain",1f,new ConfigDescription("Bound source L0 midpoint multiplier",new AcceptableValueRange<float>(.25f,5f)));
            LengthScale=config.Bind("Studio","ReferenceLengthScale",1f,new ConfigDescription("Effective reference length multiplier",new AcceptableValueRange<float>(.25f,2f)));
            ReverseCollider=config.Bind("Studio","ReverseColliderAxis",false,"Reverse the source endpoints of a DynamicBoneCollider");
            Target=config.Bind("Studio","TargetProfile",NativeTarget.AUTO,"Explicit character-shaft bindings always use their attached target; this setting selects a fallback profile for generic collider/helper sources");
        }
    }
}

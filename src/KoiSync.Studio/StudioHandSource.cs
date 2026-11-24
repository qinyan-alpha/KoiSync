using System.Collections.Generic;
using KoiSync.KK;
using UnityEngine;

namespace KoiSync.Studio
{
    internal sealed class StudioHandSource : KkHandSource
    {
        internal readonly OpeningKind Kind;
        internal StudioHandSource(Dictionary<string,Transform> bones,OpeningKind kind)
            : base(bones,kind==OpeningKind.LeftHandVaginal) {Kind=kind;}
        internal static bool IsHand(OpeningKind kind)
        {return kind==OpeningKind.LeftHandVaginal||kind==OpeningKind.RightHandVaginal;}
    }
}

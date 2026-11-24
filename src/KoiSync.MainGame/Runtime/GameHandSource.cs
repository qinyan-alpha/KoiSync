using System.Collections.Generic;
using KoiSync.KK;
using UnityEngine;

namespace KoiSync.MainGame
{
    internal sealed class GameHandSource : KkHandSource
    {
        internal GameHandSource(Dictionary<string,Transform> bones,OpeningKind kind)
            : base(bones,kind==OpeningKind.LeftHandVaginal) {}
        internal static bool IsHand(OpeningKind kind)
        {return kind==OpeningKind.LeftHandVaginal||kind==OpeningKind.RightHandVaginal;}
    }
}

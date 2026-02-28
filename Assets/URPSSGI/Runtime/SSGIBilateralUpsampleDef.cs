namespace URPSSGI
{

    internal static class SSGIBilateralUpsampleDef
    {

        internal static readonly float[] DistanceBasedWeights_2x2 =
        {
            0.324652f, 0.535261f, 0.535261f, 0.882497f,
            0.535261f, 0.324652f, 0.882497f, 0.535261f,
            0.535261f, 0.882497f, 0.324652f, 0.535261f,
            0.882497f, 0.535261f, 0.535261f, 0.324652f
        };

        internal static readonly float[] TapOffsets_2x2 =
        {
            -1f, -1f, 0f, -1f, -1f, 0f, 0f, 0f,
             0f, -1f, 1f, -1f,  0f, 0f, 1f, 0f,
            -1f,  0f, 0f,  0f, -1f, 1f, 0f, 1f,
             0f,  0f, 1f,  0f,  0f, 1f, 1f, 1f
        };
    }
}

namespace URPSSGI
{

    public struct SSGIRuntimeStats
    {
        public readonly int workingWidth;
        public readonly int workingHeight;
        public readonly bool isFullResolution;
        public readonly int activeRTCount;
        public readonly bool denoiseEnabled;
        public readonly bool secondPassEnabled;
        public readonly SSGIDebugMode currentDebugMode;

        public readonly IndirectDiffuseMode currentGIMode;
        public readonly bool rtasAvailable;
        public readonly int rtgiRayCount;

        public SSGIRuntimeStats(
            int w,
            int h,
            bool fullRes,
            int rtCount,
            bool denoise,
            bool secondPass,
            SSGIDebugMode debugMode,
            IndirectDiffuseMode giMode = IndirectDiffuseMode.ScreenSpace,
            bool rtasAvail = false,
            int rayCount = 0)
        {
            workingWidth = w;
            workingHeight = h;
            isFullResolution = fullRes;
            activeRTCount = rtCount;
            denoiseEnabled = denoise;
            secondPassEnabled = secondPass;
            currentDebugMode = debugMode;
            currentGIMode = giMode;
            rtasAvailable = rtasAvail;
            rtgiRayCount = rayCount;
        }
    }
}

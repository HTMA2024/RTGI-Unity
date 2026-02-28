using UnityEngine;

namespace DDGI
{

    public static class DDGISetupHelper
    {

        public static DDGIVolume CreateDDGISystem(
            Transform parent = null,
            Vector3Int? probeCounts = null,
            Vector3? probeSpacing = null)
        {

            GameObject ddgiObject = new GameObject("DDGI Volume");

            if (parent != null)
            {
                ddgiObject.transform.SetParent(parent);
            }

            ddgiObject.transform.localPosition = Vector3.zero;
            ddgiObject.transform.localRotation = Quaternion.identity;
            ddgiObject.transform.localScale = Vector3.one;

            DDGIVolume volume = ddgiObject.AddComponent<DDGIVolume>();

            DDGIVolumeDescriptor desc = DDGIVolumeDescriptor.Default;

            if (probeCounts.HasValue)
            {
                desc.probeCounts = probeCounts.Value;
            }
            else
            {
                desc.probeCounts = new Vector3Int(4, 2, 4);
            }

            if (probeSpacing.HasValue)
            {
                desc.probeSpacing = probeSpacing.Value;
            }

            volume.Descriptor = desc;

            ddgiObject.AddComponent<DDGIProbeVisualizer>();

            DDGIProbeUpdater updater = ddgiObject.AddComponent<DDGIProbeUpdater>();

#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("DDGIProbeUpdate t:ComputeShader");
            if (guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                ComputeShader shader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(path);

                var field = typeof(DDGIProbeUpdater).GetField("m_UpdateShader",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    field.SetValue(updater, shader);
                }
            }
#endif

            Debug.Log($"[DDGISetupHelper] Created DDGI system with {desc.TotalProbeCount} probes");

            return volume;
        }

        public static DDGIVolume CreateDDGISystemForBounds(Bounds bounds, float targetSpacing = 2f)
        {

            Vector3Int probeCounts = new Vector3Int(
                Mathf.Max(2, Mathf.CeilToInt(bounds.size.x / targetSpacing) + 1),
                Mathf.Max(2, Mathf.CeilToInt(bounds.size.y / targetSpacing) + 1),
                Mathf.Max(2, Mathf.CeilToInt(bounds.size.z / targetSpacing) + 1)
            );

            Vector3 actualSpacing = new Vector3(
                bounds.size.x / (probeCounts.x - 1),
                bounds.size.y / (probeCounts.y - 1),
                bounds.size.z / (probeCounts.z - 1)
            );

            DDGIVolume volume = CreateDDGISystem(null, probeCounts, actualSpacing);

            DDGIVolumeDescriptor desc = volume.Descriptor;
            desc.origin = bounds.min;
            volume.Descriptor = desc;

            volume.transform.position = bounds.min;

            return volume;
        }
    }
}

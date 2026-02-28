using UnityEngine;
using UnityEditor;

namespace DDGI
{

    public static class DDGIEditorMenu
    {
        [MenuItem("GameObject/Light/DDGI Volume", false, 10)]
        public static void CreateDDGIVolume(MenuCommand menuCommand)
        {

            DDGIVolume volume = DDGISetupHelper.CreateDDGISystem();

            GameObjectUtility.SetParentAndAlign(volume.gameObject, menuCommand.context as GameObject);

            Undo.RegisterCreatedObjectUndo(volume.gameObject, "Create DDGI Volume");

            Selection.activeObject = volume.gameObject;
        }

        [MenuItem("GameObject/Light/DDGI Volume (Fit to Scene)", false, 11)]
        public static void CreateDDGIVolumeFitToScene(MenuCommand menuCommand)
        {

            Bounds sceneBounds = CalculateSceneBounds();

            if (sceneBounds.size == Vector3.zero)
            {

                sceneBounds = new Bounds(Vector3.zero, new Vector3(20, 10, 20));
            }

            sceneBounds.Expand(1f);

            DDGIVolume volume = DDGISetupHelper.CreateDDGISystemForBounds(sceneBounds, 2f);

            Undo.RegisterCreatedObjectUndo(volume.gameObject, "Create DDGI Volume (Fit to Scene)");

            Selection.activeObject = volume.gameObject;

            Debug.Log($"[DDGI] Created volume fitting scene bounds: {sceneBounds}");
        }

        [MenuItem("DDGI/Create DDGI Volume")]
        public static void CreateDDGIVolumeMenu()
        {
            DDGIVolume volume = DDGISetupHelper.CreateDDGISystem();
            Selection.activeObject = volume.gameObject;
            Undo.RegisterCreatedObjectUndo(volume.gameObject, "Create DDGI Volume");
        }

        [MenuItem("DDGI/Documentation")]
        public static void OpenDocumentation()
        {

            string[] guids = AssetDatabase.FindAssets("DDGI_LightProbe_技术规范");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<Object>(path));
            }
            else
            {
                Debug.Log("[DDGI] Documentation not found. Please check DDGILightProbe/Docs folder.");
            }
        }

        private static Bounds CalculateSceneBounds()
        {
            Renderer[] renderers = Object.FindObjectsOfType<Renderer>();

            if (renderers.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.zero);
            }

            Bounds bounds = renderers[0].bounds;

            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }
    }
}

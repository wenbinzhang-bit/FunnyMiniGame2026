using System;
using UnityEngine;

namespace Brawl
{
    /// <summary>
    /// MiniGame_01-specific camera collision setup for the imported scene.
    /// Gameplay layers stay unchanged outside the map's Scene branch.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class MiniGame01CameraBlockers : MonoBehaviour
    {
        const string CameraBlockerLayerName = "CameraBlocker";

        [SerializeField] string mapRootNameMarker = "Scene";
        [SerializeField] string wallNameMarker = "wall";
        [SerializeField] string[] additionalWallNameMarkers = { "qiangmian" };
        [SerializeField, Min(0f)] float minimumBlockerHeight = 2f;

        void OnEnable()
        {
            if (!Application.isPlaying) ApplyCameraBlockerLayer();
        }

        void Awake()
        {
            if (Application.isPlaying) ApplyCameraBlockerLayer();
        }

        void ApplyCameraBlockerLayer()
        {
            int cameraBlockerLayer = LayerMask.NameToLayer(CameraBlockerLayerName);
            if (cameraBlockerLayer < 0)
            {
                Debug.LogWarning($"{nameof(MiniGame01CameraBlockers)}: layer '{CameraBlockerLayerName}' does not exist.");
                return;
            }

            int appliedCount = 0;
            Collider[] colliders = FindObjectsOfType<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || collider.isTrigger || collider.gameObject.scene != gameObject.scene)
                    continue;

                if (!HasParentName(collider.transform, mapRootNameMarker))
                    continue;

                bool isNamedWall = HasNameMarker(collider.gameObject.name, wallNameMarker) ||
                                   HasAnyNameMarker(collider.gameObject.name, additionalWallNameMarkers);
                bool isTallStaticObject = collider.bounds.size.y >= minimumBlockerHeight;
                if (!isNamedWall && !isTallStaticObject)
                    continue;

                collider.gameObject.layer = cameraBlockerLayer;
                appliedCount++;
            }

            if (Application.isPlaying)
                Debug.Log($"MINIGAME_01_CAMERA: assigned {CameraBlockerLayerName} to {appliedCount} scene wall/tall-object colliders.");
        }

        static bool HasParentName(Transform transform, string marker)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (current.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        static bool HasAnyNameMarker(string name, string[] markers)
        {
            if (markers == null) return false;

            for (int i = 0; i < markers.Length; i++)
            {
                if (HasNameMarker(name, markers[i])) return true;
            }

            return false;
        }

        static bool HasNameMarker(string name, string marker)
        {
            return !string.IsNullOrWhiteSpace(marker) &&
                   name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}

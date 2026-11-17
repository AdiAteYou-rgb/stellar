using System;
using UnityEngine;
using UnityEngine.Events;

namespace Stellar.Combat
{
    /// <summary>
    /// Data payload produced by a camera-center hitscan raycast, passed to visual effect listeners.
    /// </summary>
    [Serializable]
    public struct HitscanResult
    {
        public string abilityId;
        public Ray aimRay;
        public Vector3 startPoint;
        public Vector3 endPoint;
        public bool didHit;
        public bool hitOpponent;
        public Collider hitCollider;
        public Vector3 hitNormal;
        public float hitDistance;
    }

    [Serializable]
    public sealed class HitscanFiredUnityEvent : UnityEvent<HitscanResult>
    {
    }

    /// <summary>
    /// Serializable configuration for a hitscan ray attack.
    /// Separates raycast hitscan logic (including optional maximum range) from any visual effect.
    /// </summary>
    [Serializable]
    public sealed class HitscanAttackConfig
    {
        [SerializeField] private string abilityName = "Ray Attack";

        [Tooltip("When enabled, the hitscan ray stops at Max Range. When disabled, the ray has unlimited range.")]
        [SerializeField] private bool hasMaxRange = true;

        [Tooltip("Maximum distance of the hitscan ray when Has Max Range is enabled.")]
        [SerializeField] private float maxRange = 65f;

        [Tooltip("Optional visual effect prefab spawned when the hitscan fires. Leave empty to drive visuals via OnHitscanFired or code events.")]
        [SerializeField] private GameObject visualEffectPrefab;

        [Tooltip("Lifetime in seconds before an instantiated visualEffectPrefab is destroyed (0 = keep alive).")]
        [SerializeField] private float visualEffectLifetime = 1.5f;

        [SerializeField] private HitscanFiredUnityEvent onHitscanFired = new HitscanFiredUnityEvent();

        public HitscanAttackConfig()
        {
        }

        public HitscanAttackConfig(string abilityName, bool hasMaxRange, float maxRange)
        {
            this.abilityName = abilityName;
            this.hasMaxRange = hasMaxRange;
            this.maxRange = maxRange;
        }

        public string AbilityName => abilityName;
        public bool HasMaxRange => hasMaxRange;
        public float MaxRange => Mathf.Max(0.01f, maxRange);
        public float EffectiveRayDistance => hasMaxRange ? MaxRange : Mathf.Infinity;
        public GameObject VisualEffectPrefab => visualEffectPrefab;
        public HitscanFiredUnityEvent OnHitscanFired => onHitscanFired;

        /// <summary>
        /// Triggers developer-configured visual effects without coupling them to hit detection logic.
        /// </summary>
        public void TriggerVisuals(HitscanResult result)
        {
            if (visualEffectPrefab != null)
            {
                Vector3 dir = result.endPoint - result.startPoint;
                Quaternion rot = dir.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(dir.normalized) : Quaternion.identity;
                GameObject fxInstance = UnityEngine.Object.Instantiate(visualEffectPrefab, result.startPoint, rot);
                if (visualEffectLifetime > 0f)
                {
                    UnityEngine.Object.Destroy(fxInstance, visualEffectLifetime);
                }
            }

            onHitscanFired?.Invoke(result);
        }
    }

    /// <summary>
    /// Performs pure camera-center raycast hitscan detection without spawning collision meshes.
    /// </summary>
    public static class HitscanRayUtility
    {
        public static Ray GetCameraCenterRay(Camera cam)
        {
            return cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        }

        public static HitscanResult PerformHitscan(
            Camera cam,
            Vector3 visualStartPoint,
            HitscanAttackConfig config,
            Func<Collider, bool> isOpponentCollider,
            Func<Collider, bool> shouldIgnoreCollider = null,
            float unlimitedVisualFallbackDistance = 250f)
        {
            Physics.SyncTransforms();

            Ray aimRay = GetCameraCenterRay(cam);
            bool hasMaxRange = config == null || config.HasMaxRange;
            float rayDistance = config != null ? config.EffectiveRayDistance : Mathf.Infinity;
            float fallbackDist = hasMaxRange
                ? (config != null ? config.MaxRange : unlimitedVisualFallbackDistance)
                : unlimitedVisualFallbackDistance;

            RaycastHit[] hits = Physics.RaycastAll(aimRay, rayDistance);

            bool foundClosest = false;
            RaycastHit closestHit = default;
            float closestDist = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                Collider col = hits[i].collider;
                if (col == null) continue;
                if (shouldIgnoreCollider != null && shouldIgnoreCollider(col)) continue;

                if (hits[i].distance < closestDist)
                {
                    closestDist = hits[i].distance;
                    closestHit = hits[i];
                    foundClosest = true;
                }
            }

            HitscanResult result = new HitscanResult
            {
                abilityId = config != null ? config.AbilityName : string.Empty,
                aimRay = aimRay,
                startPoint = visualStartPoint,
                didHit = foundClosest,
                hitCollider = foundClosest ? closestHit.collider : null,
                hitOpponent = foundClosest && isOpponentCollider != null && isOpponentCollider(closestHit.collider),
                endPoint = foundClosest ? closestHit.point : aimRay.GetPoint(fallbackDist),
                hitNormal = foundClosest ? closestHit.normal : -aimRay.direction,
                hitDistance = foundClosest ? closestHit.distance : fallbackDist
            };

            config?.TriggerVisuals(result);
            return result;
        }
    }
}

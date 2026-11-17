using System;
using UnityEngine;

namespace Stellar.Combat
{
    /// <summary>
    /// Renders Unity ParticleSystem effects for all character hitscan ray abilities.
    /// Completely decoupled from hitscan raycast logic: subscribes to GridManager.OnArenaHitscanFiredLocal
    /// (or can be called directly via PlayRayEffect).
    /// </summary>
    public sealed class HitscanRayParticleEffects : MonoBehaviour
    {
        [Serializable]
        public struct RayParticleStyle
        {
            public Color primaryColor;
            public Color secondaryColor;
            public float beamWidth;
            public float particleSize;
            public float particlesPerMeter;
            public float beamLifetime;
            public int impactBurstCount;
            public float impactSpeed;
            public float impactSize;
        }

        [Header("Target GridManager (Auto-resolved if empty)")]
        [SerializeField] private GridManager gridManager;

        [Header("Character Ray Particle Styles")]
        [SerializeField] private RayParticleStyle solarBoltStyle = new RayParticleStyle
        {
            primaryColor = new Color(1.0f, 0.92f, 0.30f, 1.0f),
            secondaryColor = new Color(0.30f, 0.95f, 1.0f, 1.0f),
            beamWidth = 0.08f,
            particleSize = 0.22f,
            particlesPerMeter = 4.5f,
            beamLifetime = 0.24f,
            impactBurstCount = 16,
            impactSpeed = 5.5f,
            impactSize = 0.24f
        };

        [SerializeField] private RayParticleStyle roseThornStyle = new RayParticleStyle
        {
            primaryColor = new Color(1.0f, 0.16f, 0.42f, 1.0f),
            secondaryColor = new Color(1.0f, 0.55f, 0.72f, 1.0f),
            beamWidth = 0.06f,
            particleSize = 0.18f,
            particlesPerMeter = 5.0f,
            beamLifetime = 0.22f,
            impactBurstCount = 14,
            impactSpeed = 5.0f,
            impactSize = 0.20f
        };

        [SerializeField] private RayParticleStyle roseHeavyVolleyStyle = new RayParticleStyle
        {
            primaryColor = new Color(1.0f, 0.06f, 0.52f, 1.0f),
            secondaryColor = new Color(1.0f, 0.40f, 0.85f, 1.0f),
            beamWidth = 0.14f,
            particleSize = 0.30f,
            particlesPerMeter = 6.5f,
            beamLifetime = 0.30f,
            impactBurstCount = 26,
            impactSpeed = 7.0f,
            impactSize = 0.30f
        };

        [SerializeField] private RayParticleStyle hazmatAcidShotStyle = new RayParticleStyle
        {
            primaryColor = new Color(0.32f, 1.0f, 0.22f, 1.0f),
            secondaryColor = new Color(0.75f, 1.0f, 0.35f, 1.0f),
            beamWidth = 0.09f,
            particleSize = 0.22f,
            particlesPerMeter = 4.8f,
            beamLifetime = 0.26f,
            impactBurstCount = 18,
            impactSpeed = 4.8f,
            impactSize = 0.25f
        };

        [SerializeField] private RayParticleStyle hazmatBioDisruptorStyle = new RayParticleStyle
        {
            primaryColor = new Color(0.45f, 1.0f, 0.25f, 1.0f),
            secondaryColor = new Color(0.15f, 0.90f, 0.55f, 1.0f),
            beamWidth = 0.16f,
            particleSize = 0.32f,
            particlesPerMeter = 6.5f,
            beamLifetime = 0.32f,
            impactBurstCount = 28,
            impactSpeed = 6.8f,
            impactSize = 0.34f
        };

        private static Material s_SharedParticleMaterial;
        private static Texture2D s_SoftCircleTexture;

        private ParticleSystem beamParticleSystem;
        private ParticleSystem impactParticleSystem;

        private void Awake()
        {
            if (gridManager == null)
            {
                gridManager = GetComponent<GridManager>();
                if (gridManager == null)
                {
                    gridManager = FindFirstObjectByType<GridManager>();
                }
            }
            EnsureParticleSystems();
        }

        private void OnEnable()
        {
            if (gridManager == null)
            {
                gridManager = GetComponent<GridManager>();
                if (gridManager == null)
                {
                    gridManager = FindFirstObjectByType<GridManager>();
                }
            }

            if (gridManager != null)
            {
                gridManager.OnArenaHitscanFiredLocal -= HandleHitscanFired;
                gridManager.OnArenaHitscanFiredLocal += HandleHitscanFired;
            }
        }

        private void OnDisable()
        {
            if (gridManager != null)
            {
                gridManager.OnArenaHitscanFiredLocal -= HandleHitscanFired;
            }
        }

        private void HandleHitscanFired(HitscanResult result)
        {
            PlayRayEffect(result);
        }

        public void PlayRayEffect(HitscanResult result)
        {
            EnsureParticleSystems();

            RayParticleStyle style = GetStyleForAbility(result.abilityId);
            Vector3 start = result.startPoint;
            Vector3 end = result.endPoint;
            Vector3 delta = end - start;
            float distance = delta.magnitude;
            if (distance < 0.01f) return;

            Vector3 dir = delta / distance;
            float clampedVisualDist = Mathf.Min(distance, 120f);
            int count = Mathf.Clamp(Mathf.CeilToInt(clampedVisualDist * style.particlesPerMeter), 8, 220);

            // 1. Emit ray stream particles from start to end
            for (int i = 0; i < count; i++)
            {
                float t = (count > 1) ? (float)i / (count - 1) : 0f;
                Vector3 pos = Vector3.Lerp(start, start + dir * clampedVisualDist, t);
                Vector3 jitter = UnityEngine.Random.insideUnitSphere * style.beamWidth;

                Color col = Color.Lerp(style.primaryColor, style.secondaryColor, (i % 2 == 0) ? 0.15f : 0.85f);
                float size = style.particleSize * UnityEngine.Random.Range(0.75f, 1.25f);

                ParticleSystem.EmitParams emit = new ParticleSystem.EmitParams
                {
                    position = pos + jitter,
                    velocity = dir * UnityEngine.Random.Range(1.5f, 4.5f) + jitter * 2.0f,
                    startSize = size,
                    startLifetime = style.beamLifetime * UnityEngine.Random.Range(0.8f, 1.15f),
                    startColor = col
                };
                beamParticleSystem.Emit(emit, 1);
            }

            // 2. Muzzle flash particles at start
            for (int i = 0; i < 6; i++)
            {
                Vector3 spread = (dir + UnityEngine.Random.insideUnitSphere * 0.45f).normalized;
                ParticleSystem.EmitParams muzzleEmit = new ParticleSystem.EmitParams
                {
                    position = start,
                    velocity = spread * UnityEngine.Random.Range(2.0f, 5.0f),
                    startSize = style.particleSize * 1.35f,
                    startLifetime = style.beamLifetime * 0.65f,
                    startColor = style.secondaryColor
                };
                impactParticleSystem.Emit(muzzleEmit, 1);
            }

            // 3. Impact burst particles at hit point (or endpoint)
            if (result.didHit || result.hitOpponent)
            {
                Vector3 normal = result.hitNormal.sqrMagnitude > 0.001f ? result.hitNormal.normalized : -dir;
                for (int i = 0; i < style.impactBurstCount; i++)
                {
                    Vector3 burstDir = (normal + UnityEngine.Random.insideUnitSphere * 0.85f).normalized;
                    Color col = (i % 2 == 0) ? style.primaryColor : style.secondaryColor;

                    ParticleSystem.EmitParams impactEmit = new ParticleSystem.EmitParams
                    {
                        position = end,
                        velocity = burstDir * UnityEngine.Random.Range(style.impactSpeed * 0.45f, style.impactSpeed * 1.2f),
                        startSize = style.impactSize * UnityEngine.Random.Range(0.65f, 1.35f),
                        startLifetime = style.beamLifetime * UnityEngine.Random.Range(0.8f, 1.4f),
                        startColor = col
                    };
                    impactParticleSystem.Emit(impactEmit, 1);
                }
            }
        }

        private RayParticleStyle GetStyleForAbility(string abilityId)
        {
            if (string.IsNullOrEmpty(abilityId)) return solarBoltStyle;

            switch (abilityId)
            {
                case "Solar Bolt":
                    return solarBoltStyle;
                case "Rose Thorn":
                    return roseThornStyle;
                case "Rose Heavy Volley":
                    return roseHeavyVolleyStyle;
                case "Hazmat Acid Shot":
                    return hazmatAcidShotStyle;
                case "Hazmat Bio-Disruptor":
                    return hazmatBioDisruptorStyle;
                default:
                    return solarBoltStyle;
            }
        }

        private void EnsureParticleSystems()
        {
            Material mat = GetOrCreateParticleMaterial();

            if (beamParticleSystem == null)
            {
                beamParticleSystem = CreateChildParticleSystem("HitscanBeamParticles", mat, stretch: true);
            }

            if (impactParticleSystem == null)
            {
                impactParticleSystem = CreateChildParticleSystem("HitscanImpactParticles", mat, stretch: false);
            }
        }

        private ParticleSystem CreateChildParticleSystem(string childName, Material mat, bool stretch)
        {
            Transform existing = transform.Find(childName);
            GameObject go = existing != null ? existing.gameObject : new GameObject(childName);
            if (existing == null)
            {
                go.transform.SetParent(transform, false);
            }

            ParticleSystem ps = go.GetComponent<ParticleSystem>();
            if (ps == null)
            {
                ps = go.AddComponent<ParticleSystem>();
            }

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 1000;
            main.gravityModifier = stretch ? 0f : 0.35f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            ParticleSystem.ColorOverLifetimeModule colOverLife = ps.colorOverLifetime;
            colOverLife.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(0.85f, 0.35f),
                    new GradientAlphaKey(0f, 1f)
                });
            colOverLife.color = new ParticleSystem.MinMaxGradient(grad);

            ParticleSystem.SizeOverLifetimeModule sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 1f),
                new Keyframe(0.5f, 0.85f),
                new Keyframe(1f, 0.1f));
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            ParticleSystemRenderer renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = mat;
                renderer.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
                if (stretch)
                {
                    renderer.lengthScale = 2.4f;
                    renderer.velocityScale = 0.08f;
                }
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return ps;
        }

        private static Material GetOrCreateParticleMaterial()
        {
            if (s_SharedParticleMaterial != null) return s_SharedParticleMaterial;

            if (s_SoftCircleTexture == null)
            {
                const int size = 64;
                s_SoftCircleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };

                float center = (size - 1) * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x - center) / center;
                        float dy = (y - center) / center;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        float alpha = Mathf.Clamp01(1f - dist);
                        alpha *= alpha;
                        s_SoftCircleTexture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                }
                s_SoftCircleTexture.Apply();
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Particles/Standard Unlit")
                         ?? Shader.Find("Sprites/Default");

            s_SharedParticleMaterial = new Material(shader);
            s_SharedParticleMaterial.mainTexture = s_SoftCircleTexture;
            if (s_SharedParticleMaterial.HasProperty("_BaseMap"))
            {
                s_SharedParticleMaterial.SetTexture("_BaseMap", s_SoftCircleTexture);
            }
            if (s_SharedParticleMaterial.HasProperty("_Surface"))
            {
                s_SharedParticleMaterial.SetFloat("_Surface", 1f); // Transparent in URP
            }
            if (s_SharedParticleMaterial.HasProperty("_Blend"))
            {
                s_SharedParticleMaterial.SetFloat("_Blend", 2f); // Additive in URP Particles/Unlit
            }
            s_SharedParticleMaterial.renderQueue = 3000;

            return s_SharedParticleMaterial;
        }
    }
}

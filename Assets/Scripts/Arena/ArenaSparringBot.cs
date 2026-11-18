using System;
using UnityEngine;
using Stellar.Combat;

/// <summary>
/// Solo sparring AI controller for testing the 3D 1v1 Arena when no remote peer is connected.
/// </summary>
public class ArenaSparringBot
{
    public Vector3 knockbackVelocity = Vector3.zero;
    private float actionTimer = 2.2f;
    private float strafePhase = 0f;

    public void Reset()
    {
        knockbackVelocity = Vector3.zero;
        actionTimer = 2.2f;
        strafePhase = 0f;
    }

    public void UpdateBot(
        PlayerController pc,
        GameObject remoteAvatar,
        Transform arenaRoot,
        bool localIsZombie,
        PlantType activePlant,
        ZombieType activeZombie,
        bool localBunnySliding,
        bool localBucketParrying,
        bool localDurianRolling,
        bool localChargingRose,
        float remoteImmobilizedTimer,
        float remoteAbilityLockedTimer,
        int localArenaMaxHP,
        float moonflowerBurstRadius,
        int moonflowerBurstDamage,
        float hazmatDisruptDuration,
        HitscanAttackConfig moonflowerSolarBoltHitscan,
        HitscanAttackConfig roseThornHitscan,
        HitscanAttackConfig hazmatAcidShotHitscan,
        HitscanAttackConfig hazmatSludgeBlastHitscan,
        ref bool remoteIsBucketParrying,
        ref bool remoteIsBunnySliding,
        Func<Vector3, Vector3, (bool hasLos, Vector3 beamEnd)> raycastLos,
        Action<HitscanAttackConfig, Vector3, Vector3, bool> dispatchVisuals,
        Action<int, float> applyDamageToLocal,
        Action<float> applyDisruptToLocal,
        Action<string> showDodgedLabel)
    {
        if (remoteAvatar == null || pc == null) return;

        if (knockbackVelocity.sqrMagnitude > 0.001f || remoteAvatar.transform.position.y > 1.501f)
        {
            knockbackVelocity.y += -9.81f * Time.deltaTime;
            Vector3 nextPos = remoteAvatar.transform.position + knockbackVelocity * Time.deltaTime;
            nextPos.x = Mathf.Clamp(nextPos.x, -20.5f, 20.5f);
            nextPos.z = Mathf.Clamp(nextPos.z, -46.5f, -5.5f);
            if (nextPos.y <= 1.5f)
            {
                nextPos.y = 1.5f;
                knockbackVelocity.y = 0f;
                knockbackVelocity.x = Mathf.Lerp(knockbackVelocity.x, 0f, Time.deltaTime * 10f);
                knockbackVelocity.z = Mathf.Lerp(knockbackVelocity.z, 0f, Time.deltaTime * 10f);
                if (new Vector2(knockbackVelocity.x, knockbackVelocity.z).sqrMagnitude < 0.01f)
                {
                    knockbackVelocity = Vector3.zero;
                }
            }
            else
            {
                knockbackVelocity.x = Mathf.Lerp(knockbackVelocity.x, 0f, Time.deltaTime * 2.4f);
                knockbackVelocity.z = Mathf.Lerp(knockbackVelocity.z, 0f, Time.deltaTime * 2.4f);
            }
            remoteAvatar.transform.position = nextPos;
        }

        if (remoteImmobilizedTimer > 0f) return;

        strafePhase += Time.deltaTime * 1.6f;
        Vector3 toPlayer = pc.transform.position - remoteAvatar.transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.05f)
        {
            Quaternion lookRot = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
            remoteAvatar.transform.rotation = Quaternion.Slerp(remoteAvatar.transform.rotation, lookRot, Time.deltaTime * 8f);
        }

        if (remoteAbilityLockedTimer > 0f) return;

        actionTimer -= Time.deltaTime;
        if (actionTimer > 0f) return;

        if (localIsZombie)
        {
            float distToZombie = Vector3.Distance(pc.transform.position, remoteAvatar.transform.position);
            if (activePlant == PlantType.Moonflower && distToZombie <= moonflowerBurstRadius)
            {
                actionTimer = 4.2f;
                Vector3 forwardFromBot = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : remoteAvatar.transform.forward;
                Vector3 burstOrigin = remoteAvatar.transform.position + forwardFromBot * 0.45f;
                ArenaVfxFactory.SpawnMoonExplosion(arenaRoot, burstOrigin);
                knockbackVelocity = (-forwardFromBot * 9.8f) + (Vector3.up * 6.4f);

                if (!localBucketParrying)
                {
                    Vector3 pushZombieDir = pc.transform.position - burstOrigin;
                    pushZombieDir.y = 0f;
                    pushZombieDir = pushZombieDir.sqrMagnitude > 0.01f ? pushZombieDir.normalized : forwardFromBot;
                    pc.ApplyKnockback(pushZombieDir * 10.5f, 6.5f);
                }
                applyDamageToLocal(moonflowerBurstDamage, 0f);
                return;
            }

            int dmg = (activePlant == PlantType.Moonflower) ? 18 : ((activePlant == PlantType.Durian) ? 35 : 25);
            actionTimer = (activePlant == PlantType.Moonflower) ? 2.4f : 2.3f;

            Vector3 start = remoteAvatar.transform.position + new Vector3(0f, 0.6f, 0f);
            Vector3 target = pc.transform.position + new Vector3(0f, localBunnySliding ? 0.45f : 0.1f, 0f);

            var (hasLos, beamEnd) = raycastLos(start, target);
            HitscanAttackConfig botConfig = (activePlant == PlantType.Moonflower) ? moonflowerSolarBoltHitscan : roseThornHitscan;
            dispatchVisuals(botConfig, start, beamEnd, hasLos && !localBunnySliding);

            if (hasLos)
            {
                if (localBunnySliding)
                {
                    showDodgedLabel("💨 DODGED SHOT WITH BUNNY SLIDE! 💨");
                }
                else
                {
                    applyDamageToLocal(dmg, 0f);
                }
            }
        }
        else
        {
            if (activeZombie == ZombieType.Buckethead)
            {
                remoteIsBucketParrying = !remoteIsBucketParrying;
                actionTimer = remoteIsBucketParrying ? 1.0f : 3.5f;
            }
            else if (activeZombie == ZombieType.Bunny)
            {
                remoteIsBunnySliding = !remoteIsBunnySliding;
                actionTimer = remoteIsBunnySliding ? 0.9f : 2.5f;
            }
            else if (activeZombie == ZombieType.Hazmat)
            {
                actionTimer = 2.1f;
                Vector3 start = remoteAvatar.transform.position + new Vector3(0f, 0.55f, 0f);
                Vector3 target = pc.transform.position + new Vector3(0f, 0.20f, 0f);
                var (hasLos, beamEnd) = raycastLos(start, target);
                bool shouldDisrupt = localDurianRolling || localChargingRose;
                if (shouldDisrupt)
                {
                    dispatchVisuals(hazmatSludgeBlastHitscan, start, beamEnd, hasLos);
                    ArenaVfxFactory.SpawnAcidExplosion(arenaRoot, beamEnd);
                    if (hasLos)
                    {
                        applyDisruptToLocal(hazmatDisruptDuration);
                    }
                }
                else
                {
                    dispatchVisuals(hazmatAcidShotHitscan, start, beamEnd, hasLos);
                    if (hasLos)
                    {
                        int dmg = 20 + Mathf.RoundToInt(localArenaMaxHP * 0.04f);
                        applyDamageToLocal(dmg, 0f);
                    }
                }
            }
            else
            {
                actionTimer = 2.5f;
            }
        }
    }
}

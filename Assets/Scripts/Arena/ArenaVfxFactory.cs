using UnityEngine;

/// <summary>
/// Factory for spawning 3D Arena combat VFX (explosions, parry bursts, durian spikes, zombie slashes).
/// </summary>
public static class ArenaVfxFactory
{
    public static void SpawnMoonExplosion(Transform arenaRoot, Vector3 center)
    {
        SpawnExpandingBurstExplosion(
            arenaRoot,
            "MoonflowerOutburstFX",
            center,
            new Color(1.0f, 0.94f, 0.48f),
            new Color(0.32f, 0.95f, 1.0f),
            new Color(0.65f, 0.42f, 1.0f),
            endScale: 3.8f);
    }

    public static void SpawnAcidExplosion(Transform arenaRoot, Vector3 center)
    {
        SpawnExpandingBurstExplosion(
            arenaRoot,
            "HazmatAcidBurstFX",
            center,
            new Color(0.76f, 1.0f, 0.42f),
            new Color(0.30f, 1.0f, 0.22f),
            new Color(0.14f, 0.65f, 0.12f),
            endScale: 3.2f);
    }

    public static void SpawnParryBurst(Transform arenaRoot, Vector3 center)
    {
        SpawnExpandingBurstExplosion(
            arenaRoot,
            "BucketParryFX",
            center,
            new Color(0.55f, 0.95f, 1.0f),
            new Color(0.25f, 0.85f, 1.0f),
            new Color(0.85f, 0.95f, 1.0f),
            endScale: 2.2f);
    }

    public static void SpawnExpandingBurstExplosion(
        Transform arenaRoot,
        string fxName,
        Vector3 center,
        Color coreColor,
        Color ringColor,
        Color shardAltColor,
        float endScale)
    {
        if (arenaRoot == null) return;

        GameObject root = new GameObject(fxName);
        root.transform.SetParent(arenaRoot, true);
        root.transform.position = center;

        Material coreMat = MaterialCache.GetColoredMaterial(coreColor);
        Material ringMat = MaterialCache.GetColoredMaterial(ringColor);
        Material altMat = MaterialCache.GetColoredMaterial(shardAltColor);

        GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.transform.SetParent(root.transform, false);
        core.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);
        MaterialCache.DestroyCollider(core);
        core.GetComponent<Renderer>().sharedMaterial = coreMat;

        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.transform.SetParent(root.transform, false);
        ring.transform.localPosition = new Vector3(0f, -0.35f, 0f);
        ring.transform.localScale = new Vector3(1.1f, 0.04f, 1.1f);
        MaterialCache.DestroyCollider(ring);
        ring.GetComponent<Renderer>().sharedMaterial = ringMat;

        const int sparkCount = 8;
        for (int i = 0; i < sparkCount; i++)
        {
            float rad = (i * (360f / sparkCount)) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(rad), 0.25f, Mathf.Sin(rad)).normalized;
            GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.transform.SetParent(root.transform, false);
            shard.transform.localPosition = dir * 0.42f;
            shard.transform.localRotation = Quaternion.LookRotation(dir);
            shard.transform.localScale = new Vector3(0.11f, 0.11f, 0.34f);
            MaterialCache.DestroyCollider(shard);
            shard.GetComponent<Renderer>().sharedMaterial = (i % 2 == 0) ? ringMat : altMat;
        }

        ExpandingBurstFx anim = root.AddComponent<ExpandingBurstFx>();
        anim.duration = 0.36f;
        anim.startScale = 0.45f;
        anim.endScale = endScale;
    }

    public static void SpawnDurianSpikeBurst(Transform arenaRoot, Vector3 center, Quaternion viewRot)
    {
        if (arenaRoot == null) return;

        GameObject burstRoot = new GameObject("DurianSpikeBashFX");
        burstRoot.transform.SetParent(arenaRoot, true);
        burstRoot.transform.SetPositionAndRotation(center, viewRot);

        Material shellMat = MaterialCache.GetColoredMaterial(new Color(0.68f, 0.56f, 0.16f));
        Material spikeGoldMat = MaterialCache.GetColoredMaterial(new Color(0.95f, 0.84f, 0.22f));
        Material spikeLimeMat = MaterialCache.GetColoredMaterial(new Color(0.55f, 0.85f, 0.24f));

        GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.transform.SetParent(burstRoot.transform, false);
        core.transform.localPosition = Vector3.zero;
        core.transform.localScale = new Vector3(0.52f, 0.52f, 0.52f);
        MaterialCache.DestroyCollider(core);
        core.GetComponent<Renderer>().sharedMaterial = shellMat;

        GameObject cageA = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cageA.transform.SetParent(burstRoot.transform, false);
        cageA.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);
        cageA.transform.localScale = new Vector3(0.46f, 0.46f, 0.46f);
        MaterialCache.DestroyCollider(cageA);
        cageA.GetComponent<Renderer>().sharedMaterial = spikeGoldMat;

        GameObject cageB = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cageB.transform.SetParent(burstRoot.transform, false);
        cageB.transform.localRotation = Quaternion.Euler(0f, 45f, 45f);
        cageB.transform.localScale = new Vector3(0.46f, 0.46f, 0.46f);
        MaterialCache.DestroyCollider(cageB);
        cageB.GetComponent<Renderer>().sharedMaterial = spikeLimeMat;

        const int spikeCount = 8;
        for (int i = 0; i < spikeCount; i++)
        {
            float rad = i * (360f / spikeCount) * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(rad) * 0.65f, Mathf.Sin(rad) * 0.65f, 0.85f).normalized;

            GameObject thorn = GameObject.CreatePrimitive(PrimitiveType.Cube);
            thorn.transform.SetParent(burstRoot.transform, false);
            thorn.transform.localPosition = dir * 0.36f;
            thorn.transform.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, 45f);
            thorn.transform.localScale = new Vector3(0.11f, 0.11f, 0.52f);
            MaterialCache.DestroyCollider(thorn);
            thorn.GetComponent<Renderer>().sharedMaterial = (i % 2 == 0) ? spikeGoldMat : spikeLimeMat;
        }

        Object.Destroy(burstRoot, 0.24f);
    }

    public static void SpawnSlashEffect(Transform arenaRoot, Vector3 center, Quaternion viewRot)
    {
        if (arenaRoot == null) return;

        GameObject slashRoot = new GameObject("ZombieSlashFX");
        slashRoot.transform.SetParent(arenaRoot, true);
        slashRoot.transform.SetPositionAndRotation(center, viewRot);

        Material slashMat = MaterialCache.GetColoredMaterial(new Color(0.85f, 0.35f, 1.0f));
        for (int i = -1; i <= 1; i++)
        {
            GameObject claw = GameObject.CreatePrimitive(PrimitiveType.Cube);
            claw.transform.SetParent(slashRoot.transform, false);
            claw.transform.localPosition = new Vector3(i * 0.22f, 0f, 0f);
            claw.transform.localRotation = Quaternion.Euler(0f, 0f, -35f);
            claw.transform.localScale = new Vector3(0.08f, 0.95f, 0.08f);
            MaterialCache.DestroyCollider(claw);
            claw.GetComponent<Renderer>().sharedMaterial = slashMat;
        }

        Object.Destroy(slashRoot, 0.20f);
    }
}

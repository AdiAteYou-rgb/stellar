using UnityEngine;

/// <summary>
/// Instantiates 3D Arena character models (from Prefabs when available, or procedural fallback)
/// and first-person POV ability props.
/// </summary>
public static class ArenaModelFactory
{
    private static Mesh s_CachedDurian3DMesh;

    public static GameObject Create3DZombieModel(ZombieType type, string objectName, out GameObject parryShieldObj, GameConfigSO config = null)
    {
        parryShieldObj = null;

        GameObject prefab = config != null ? config.GetZombieDefinition(type)?.arenaVisualPrefab3D : null;
        if (prefab == null)
        {
            prefab = Resources.Load<GameObject>($"Prefabs/Arena3D/Arena3D_Zombie_{type}");
        }

        if (prefab != null)
        {
            GameObject instance = Object.Instantiate(prefab);
            instance.name = objectName;
            Transform parry = instance.transform.Find("ParryShieldVisual");
            if (parry != null)
            {
                parry.gameObject.SetActive(false);
                parryShieldObj = parry.gameObject;
            }
            return instance;
        }

        return BuildProcedural3DZombieModel(type, objectName, out parryShieldObj);
    }

    public static GameObject Create3DPlantModel(PlantType type, string objectName, GameConfigSO config = null)
    {
        PlantType normalizedType = type == PlantType.Roze ? PlantType.Rose : type;

        GameObject prefab = config != null ? config.GetPlantDefinition(normalizedType)?.arenaVisualPrefab3D : null;
        if (prefab == null)
        {
            prefab = Resources.Load<GameObject>($"Prefabs/Arena3D/Arena3D_Plant_{normalizedType}");
        }

        if (prefab != null)
        {
            GameObject instance = Object.Instantiate(prefab);
            instance.name = objectName;
            return instance;
        }

        return BuildProcedural3DPlantModel(normalizedType, objectName);
    }

    public static GameObject BuildProcedural3DZombieModel(ZombieType type, string objectName, out GameObject parryShieldObj)
    {
        parryShieldObj = null;
        GameObject root = new GameObject(objectName);

        bool isBuckethead = (type == ZombieType.Buckethead);
        bool isBunny = (type == ZombieType.Bunny);
        bool isHazmat = (type == ZombieType.Hazmat);

        Material skinMat = MaterialCache.GetColoredMaterial(isBunny ? new Color(0.56f, 0.80f, 0.36f) : new Color(0.50f, 0.74f, 0.30f));
        Material darkSkinMat = MaterialCache.GetColoredMaterial(new Color(0.38f, 0.58f, 0.22f));

        Material coatMat;
        Material pantsMat;
        Material shoeMat;
        if (isBuckethead)
        {
            coatMat = MaterialCache.GetColoredMaterial(new Color(0.24f, 0.27f, 0.30f));
            pantsMat = MaterialCache.GetColoredMaterial(new Color(0.18f, 0.22f, 0.26f));
            shoeMat = MaterialCache.GetColoredMaterial(new Color(0.16f, 0.14f, 0.12f));
        }
        else if (isBunny)
        {
            coatMat = MaterialCache.GetColoredMaterial(new Color(0.14f, 0.74f, 0.78f));
            pantsMat = MaterialCache.GetColoredMaterial(new Color(0.16f, 0.20f, 0.34f));
            shoeMat = MaterialCache.GetColoredMaterial(new Color(0.98f, 0.42f, 0.14f));
        }
        else if (isHazmat)
        {
            coatMat = MaterialCache.GetColoredMaterial(new Color(0.95f, 0.78f, 0.12f));
            pantsMat = MaterialCache.GetColoredMaterial(new Color(0.82f, 0.66f, 0.10f));
            shoeMat = MaterialCache.GetColoredMaterial(new Color(0.15f, 0.17f, 0.19f));
        }
        else
        {
            coatMat = MaterialCache.GetColoredMaterial(new Color(0.42f, 0.16f, 0.58f));
            pantsMat = MaterialCache.GetColoredMaterial(new Color(0.20f, 0.28f, 0.38f));
            shoeMat = MaterialCache.GetColoredMaterial(new Color(0.28f, 0.18f, 0.14f));
        }

        Material tieMat = MaterialCache.GetColoredMaterial(new Color(0.85f, 0.18f, 0.18f));
        Material eyeWhiteMat = MaterialCache.GetColoredMaterial(Color.white);
        Material pupilMat = MaterialCache.GetColoredMaterial(new Color(0.08f, 0.10f, 0.08f));

        CreatePart(PrimitiveType.Cylinder, "LeftLeg", root.transform, new Vector3(-0.18f, -0.55f, 0f), Quaternion.identity, new Vector3(0.22f, 0.35f, 0.22f), pantsMat);
        CreatePart(PrimitiveType.Cylinder, "RightLeg", root.transform, new Vector3(0.18f, -0.55f, 0f), Quaternion.identity, new Vector3(0.22f, 0.35f, 0.22f), pantsMat);
        CreatePart(PrimitiveType.Cube, "LeftShoe", root.transform, new Vector3(-0.18f, -0.88f, 0.06f), Quaternion.identity, new Vector3(0.24f, 0.14f, 0.36f), shoeMat);
        CreatePart(PrimitiveType.Cube, "RightShoe", root.transform, new Vector3(0.18f, -0.88f, 0.06f), Quaternion.identity, new Vector3(0.24f, 0.14f, 0.36f), shoeMat);

        Vector3 torsoScale = isBuckethead
            ? new Vector3(0.76f, 0.75f, 0.46f)
            : (isBunny ? new Vector3(0.62f, 0.70f, 0.36f) : new Vector3(0.68f, 0.72f, 0.40f));
        CreatePart(PrimitiveType.Cube, "Torso", root.transform, new Vector3(0f, -0.02f, 0f), Quaternion.identity, torsoScale, coatMat);

        if (isBuckethead)
        {
            CreatePart(PrimitiveType.Cube, "Belt", root.transform, new Vector3(0f, -0.32f, 0f), Quaternion.identity, new Vector3(0.78f, 0.10f, 0.48f), MaterialCache.GetColoredMaterial(new Color(0.22f, 0.14f, 0.08f)));
            CreatePart(PrimitiveType.Cube, "BeltBuckle", root.transform, new Vector3(0f, -0.32f, 0.25f), Quaternion.identity, new Vector3(0.16f, 0.12f, 0.04f), MaterialCache.GetColoredMaterial(new Color(0.85f, 0.78f, 0.28f)));
            CreatePart(PrimitiveType.Cube, "RedTie", root.transform, new Vector3(0f, 0.04f, 0.24f), Quaternion.identity, new Vector3(0.13f, 0.44f, 0.04f), tieMat);
        }
        else if (isBunny)
        {
            Material coralMat = MaterialCache.GetColoredMaterial(new Color(0.98f, 0.38f, 0.56f));
            Material whiteMat = MaterialCache.GetColoredMaterial(new Color(0.96f, 0.96f, 0.98f));
            CreatePart(PrimitiveType.Cube, "TracksuitStripe", root.transform, new Vector3(0f, 0.04f, 0.19f), Quaternion.identity, new Vector3(0.64f, 0.16f, 0.04f), coralMat);
            CreatePart(PrimitiveType.Cube, "ZipperLine", root.transform, new Vector3(0f, -0.02f, 0.20f), Quaternion.identity, new Vector3(0.06f, 0.68f, 0.04f), whiteMat);
            CreatePart(PrimitiveType.Sphere, "BunnyTail", root.transform, new Vector3(0f, -0.24f, -0.24f), Quaternion.identity, new Vector3(0.28f, 0.28f, 0.28f), whiteMat);
        }
        else if (isHazmat)
        {
            Material harnessMat = MaterialCache.GetColoredMaterial(new Color(0.16f, 0.18f, 0.21f));
            Material acidTankGlowMat = MaterialCache.GetColoredMaterial(new Color(0.25f, 1.0f, 0.20f));
            Material pipeMat = MaterialCache.GetColoredMaterial(new Color(0.32f, 0.36f, 0.40f));

            for (int side = -1; side <= 1; side += 2)
            {
                CreatePart(PrimitiveType.Cube, side < 0 ? "HarnessStrapL" : "HarnessStrapR", root.transform, new Vector3(side * 0.19f, 0f, 0f), Quaternion.identity, new Vector3(0.09f, 0.74f, 0.43f), harnessMat);
            }
            CreatePart(PrimitiveType.Cube, "HazardBelt", root.transform, new Vector3(0f, -0.28f, 0f), Quaternion.identity, new Vector3(0.72f, 0.10f, 0.44f), harnessMat);
            CreatePart(PrimitiveType.Cylinder, "BiohazardCoreRing", root.transform, new Vector3(0f, 0.06f, 0.21f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.26f, 0.03f, 0.26f), harnessMat);
            CreatePart(PrimitiveType.Cylinder, "BiohazardCoreGlow", root.transform, new Vector3(0f, 0.06f, 0.22f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.18f, 0.035f, 0.18f), acidTankGlowMat);

            for (int side = -1; side <= 1; side += 2)
            {
                float tx = side * 0.17f;
                CreatePart(PrimitiveType.Cylinder, side < 0 ? "AcidTankShellL" : "AcidTankShellR", root.transform, new Vector3(tx, 0.04f, -0.28f), Quaternion.identity, new Vector3(0.24f, 0.36f, 0.24f), harnessMat);
                CreatePart(PrimitiveType.Cylinder, side < 0 ? "AcidTankLiquidL" : "AcidTankLiquidR", root.transform, new Vector3(tx, 0.04f, -0.30f), Quaternion.identity, new Vector3(0.19f, 0.28f, 0.22f), acidTankGlowMat);
                CreatePart(PrimitiveType.Cylinder, side < 0 ? "AcidTankCapL" : "AcidTankCapR", root.transform, new Vector3(tx, 0.42f, -0.28f), Quaternion.identity, new Vector3(0.13f, 0.05f, 0.13f), pipeMat);
            }
            CreatePart(PrimitiveType.Cylinder, "TankManifoldPipe", root.transform, new Vector3(0f, 0.45f, -0.28f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.08f, 0.20f, 0.08f), pipeMat);
        }
        else
        {
            CreatePart(PrimitiveType.Cube, "RedTie", root.transform, new Vector3(0f, 0.02f, 0.21f), Quaternion.identity, new Vector3(0.12f, 0.46f, 0.04f), tieMat);
        }

        CreatePart(PrimitiveType.Cylinder, "LeftArm", root.transform, new Vector3(-0.42f, 0.16f, 0.38f), Quaternion.Euler(82f, 0f, 0f), new Vector3(0.18f, 0.38f, 0.18f), isHazmat ? coatMat : skinMat);

        if (isHazmat)
        {
            CreatePart(PrimitiveType.Sphere, "LeftHazmatGlove", root.transform, new Vector3(-0.42f, 0.20f, 0.72f), Quaternion.identity, new Vector3(0.20f, 0.18f, 0.22f), shoeMat);

            GameObject rightArmPivot = new GameObject("RightArm");
            rightArmPivot.transform.SetParent(root.transform, false);
            rightArmPivot.transform.localPosition = new Vector3(0.42f, 0.14f, 0.38f);
            rightArmPivot.transform.localRotation = Quaternion.Euler(85f, 0f, 0f);

            CreatePart(PrimitiveType.Cylinder, "RightArmSleeve", rightArmPivot.transform, Vector3.zero, Quaternion.identity, new Vector3(0.18f, 0.38f, 0.18f), coatMat);
            CreatePart(PrimitiveType.Sphere, "RightHazmatGlove", rightArmPivot.transform, new Vector3(0f, 0.36f, 0f), Quaternion.identity, new Vector3(0.20f, 0.20f, 0.20f), shoeMat);

            Material sprayerMat = MaterialCache.GetColoredMaterial(new Color(0.20f, 0.23f, 0.27f));
            Material acidTipMat = MaterialCache.GetColoredMaterial(new Color(0.30f, 1.0f, 0.22f));

            CreatePart(PrimitiveType.Cylinder, "AcidSprayerBarrel", rightArmPivot.transform, new Vector3(0f, 0.56f, -0.02f), Quaternion.identity, new Vector3(0.14f, 0.26f, 0.14f), sprayerMat);
            CreatePart(PrimitiveType.Cylinder, "AcidSprayerVial", rightArmPivot.transform, new Vector3(0f, 0.50f, -0.12f), Quaternion.identity, new Vector3(0.10f, 0.13f, 0.10f), acidTipMat);
            CreatePart(PrimitiveType.Sphere, "AcidSprayerNozzle", rightArmPivot.transform, new Vector3(0f, 0.83f, -0.02f), Quaternion.identity, new Vector3(0.18f, 0.12f, 0.18f), acidTipMat);
        }
        else
        {
            CreatePart(PrimitiveType.Cylinder, "RightArm", root.transform, new Vector3(0.42f, 0.14f, 0.38f), Quaternion.Euler(85f, 0f, 0f), new Vector3(0.18f, 0.38f, 0.18f), skinMat);
        }

        CreatePart(PrimitiveType.Sphere, "Head", root.transform, new Vector3(0f, 0.62f, 0.04f), Quaternion.identity,
            isHazmat ? new Vector3(0.76f, 0.76f, 0.74f) : new Vector3(0.66f, 0.68f, 0.64f),
            isHazmat ? coatMat : skinMat);

        if (isHazmat)
        {
            Material maskDarkMat = MaterialCache.GetColoredMaterial(new Color(0.16f, 0.18f, 0.21f));
            Material filterTrimMat = MaterialCache.GetColoredMaterial(new Color(0.32f, 0.36f, 0.40f));
            Material visorGlowMat = MaterialCache.GetColoredMaterial(new Color(0.28f, 1.0f, 0.22f));

            CreatePart(PrimitiveType.Sphere, "HazmatVisor", root.transform, new Vector3(0f, 0.68f, 0.26f), Quaternion.identity, new Vector3(0.56f, 0.28f, 0.32f), visorGlowMat);
            CreatePart(PrimitiveType.Cylinder, "RespiratorSnout", root.transform, new Vector3(0f, 0.45f, 0.38f), Quaternion.Euler(105f, 0f, 0f), new Vector3(0.22f, 0.16f, 0.22f), maskDarkMat);

            for (int side = -1; side <= 1; side += 2)
            {
                GameObject filter = CreatePart(PrimitiveType.Cylinder, side < 0 ? "FilterL" : "FilterR", root.transform, new Vector3(side * 0.24f, 0.42f, 0.33f), Quaternion.Euler(85f, side * 32f, 0f), new Vector3(0.18f, 0.09f, 0.18f), filterTrimMat);
                CreatePart(PrimitiveType.Cylinder, side < 0 ? "FilterCapL" : "FilterCapR", filter.transform, new Vector3(0f, 0.85f, 0f), Quaternion.identity, new Vector3(0.55f, 0.35f, 0.55f), visorGlowMat);
            }
        }
        else
        {
            CreatePart(PrimitiveType.Cube, "Jaw", root.transform, new Vector3(0f, 0.42f, 0.14f), Quaternion.identity, new Vector3(0.44f, 0.18f, 0.38f), darkSkinMat);
            CreatePart(PrimitiveType.Sphere, "LeftEye", root.transform, new Vector3(-0.14f, 0.66f, 0.31f), Quaternion.identity, new Vector3(0.18f, 0.18f, 0.10f), eyeWhiteMat);
            CreatePart(PrimitiveType.Sphere, "LeftPupil", root.transform, new Vector3(-0.14f, 0.66f, 0.35f), Quaternion.identity, new Vector3(0.08f, 0.08f, 0.06f), pupilMat);
            CreatePart(PrimitiveType.Sphere, "RightEye", root.transform, new Vector3(0.14f, 0.65f, 0.31f), Quaternion.identity, new Vector3(0.13f, 0.13f, 0.09f), eyeWhiteMat);
            CreatePart(PrimitiveType.Sphere, "RightPupil", root.transform, new Vector3(0.14f, 0.65f, 0.34f), Quaternion.identity, new Vector3(0.06f, 0.06f, 0.05f), pupilMat);
        }

        if (isBuckethead)
        {
            Material bucketSilver = MaterialCache.GetColoredMaterial(new Color(0.74f, 0.78f, 0.84f));
            Material bucketDark = MaterialCache.GetColoredMaterial(new Color(0.48f, 0.52f, 0.58f));
            Material paintSmudgeMat = MaterialCache.GetColoredMaterial(new Color(0.86f, 0.18f, 0.16f));

            GameObject bucket = CreatePart(PrimitiveType.Cylinder, "MetalBucket", root.transform, new Vector3(0f, 1.00f, 0.02f), Quaternion.Euler(-10f, 0f, 8f), new Vector3(0.70f, 0.32f, 0.70f), bucketSilver);
            CreatePart(PrimitiveType.Cylinder, "BucketRim", bucket.transform, new Vector3(0f, -0.82f, 0f), Quaternion.identity, new Vector3(1.08f, 0.14f, 1.08f), bucketDark);
            CreatePart(PrimitiveType.Sphere, "BucketPaintSmudge", bucket.transform, new Vector3(0.24f, 0.10f, 0.45f), Quaternion.identity, new Vector3(0.28f, 0.38f, 0.12f), paintSmudgeMat);

            GameObject parryShield = CreatePart(PrimitiveType.Sphere, "ParryShieldVisual", root.transform, new Vector3(0f, 0.25f, 0.45f), Quaternion.identity, new Vector3(1.45f, 1.65f, 0.35f), MaterialCache.GetColoredMaterial(new Color(0.35f, 0.92f, 1.0f)));
            parryShield.SetActive(false);
            parryShieldObj = parryShield;
        }
        else if (isBunny)
        {
            Material earWhiteMat = MaterialCache.GetColoredMaterial(new Color(0.96f, 0.96f, 0.99f));
            Material earPinkMat = MaterialCache.GetColoredMaterial(new Color(0.98f, 0.56f, 0.72f));
            Material headbandMat = MaterialCache.GetColoredMaterial(new Color(0.98f, 0.32f, 0.50f));

            CreatePart(PrimitiveType.Cylinder, "BunnySweatband", root.transform, new Vector3(0f, 0.80f, 0.04f), Quaternion.identity, new Vector3(0.66f, 0.06f, 0.65f), headbandMat);
            GameObject leftEar = CreatePart(PrimitiveType.Capsule, "LeftBunnyEar", root.transform, new Vector3(-0.18f, 1.24f, 0.02f), Quaternion.Euler(-6f, 0f, 10f), new Vector3(0.18f, 0.36f, 0.10f), earWhiteMat);
            CreatePart(PrimitiveType.Capsule, "LeftBunnyEarInner", leftEar.transform, new Vector3(0f, 0f, 0.32f), Quaternion.identity, new Vector3(0.58f, 0.76f, 0.55f), earPinkMat);
            GameObject rightEar = CreatePart(PrimitiveType.Capsule, "RightBunnyEar", root.transform, new Vector3(0.18f, 1.24f, 0.02f), Quaternion.Euler(8f, 0f, -12f), new Vector3(0.18f, 0.36f, 0.10f), earWhiteMat);
            CreatePart(PrimitiveType.Capsule, "RightBunnyEarInner", rightEar.transform, new Vector3(0f, 0f, 0.32f), Quaternion.identity, new Vector3(0.58f, 0.76f, 0.55f), earPinkMat);
        }

        return root;
    }

    public static GameObject BuildProcedural3DPlantModel(PlantType type, string objectName)
    {
        GameObject root = new GameObject(objectName);

        if (type == PlantType.Moonflower)
        {
            CreatePart(PrimitiveType.Cylinder, "Stem", root.transform, new Vector3(0f, -0.45f, 0f), Quaternion.identity, new Vector3(0.36f, 0.50f, 0.36f), MaterialCache.GetColoredMaterial(new Color(0.2f, 0.7f, 0.45f)));
            CreatePart(PrimitiveType.Sphere, "Petals", root.transform, new Vector3(0f, 0.25f, -0.04f), Quaternion.identity, new Vector3(1.15f, 1.15f, 0.40f), MaterialCache.GetColoredMaterial(new Color(0.45f, 0.85f, 1.0f)));
            CreatePart(PrimitiveType.Sphere, "Head", root.transform, new Vector3(0f, 0.25f, 0.08f), Quaternion.identity, new Vector3(0.78f, 0.78f, 0.50f), MaterialCache.GetColoredMaterial(new Color(1.0f, 0.95f, 0.4f)));
        }
        else if (type == PlantType.Rose || type == PlantType.Roze)
        {
            CreatePart(PrimitiveType.Cylinder, "Stem", root.transform, new Vector3(0f, -0.45f, 0f), Quaternion.identity, new Vector3(0.38f, 0.50f, 0.38f), MaterialCache.GetColoredMaterial(new Color(0.15f, 0.65f, 0.25f)));
            CreatePart(PrimitiveType.Sphere, "Head", root.transform, new Vector3(0f, 0.28f, 0f), Quaternion.identity, new Vector3(0.95f, 0.95f, 0.95f), MaterialCache.GetColoredMaterial(new Color(0.95f, 0.18f, 0.32f)));
            CreatePart(PrimitiveType.Cylinder, "Barrel", root.transform, new Vector3(0f, 0.28f, 0.52f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.42f, 0.30f, 0.42f), MaterialCache.GetColoredMaterial(new Color(0.8f, 0.12f, 0.25f)));
        }
        else
        {
            GameObject modelObj = new GameObject("DurianModel");
            modelObj.transform.SetParent(root.transform, false);

            Mesh durianMesh = GetDurian3DMesh();
            if (durianMesh != null)
            {
                MeshFilter mf = modelObj.AddComponent<MeshFilter>();
                mf.sharedMesh = durianMesh;
                MeshRenderer mr = modelObj.AddComponent<MeshRenderer>();
                mr.sharedMaterial = MaterialCache.GetColoredMaterial(new Color(0.74f, 0.67f, 0.22f));
            }
            else
            {
                CreatePart(PrimitiveType.Sphere, "Body", modelObj.transform, new Vector3(0f, -0.05f, 0f), Quaternion.identity, new Vector3(1.25f, 1.40f, 1.20f), MaterialCache.GetColoredMaterial(new Color(0.68f, 0.58f, 0.2f)));
                CreatePart(PrimitiveType.Cube, "Spikes", modelObj.transform, new Vector3(0f, -0.05f, 0f), Quaternion.Euler(45f, 0f, 45f), new Vector3(1.05f, 1.05f, 1.05f), MaterialCache.GetColoredMaterial(new Color(0.82f, 0.72f, 0.25f)));
            }
        }

        return root;
    }

    public static Mesh GetDurian3DMesh()
    {
        if (s_CachedDurian3DMesh != null) return s_CachedDurian3DMesh;

        GameObject modelPrefab = Resources.Load<GameObject>("Models/Durian");
        if (modelPrefab != null)
        {
            MeshFilter mf = modelPrefab.GetComponentInChildren<MeshFilter>(true);
            if (mf != null && mf.sharedMesh != null)
            {
                s_CachedDurian3DMesh = mf.sharedMesh;
                return s_CachedDurian3DMesh;
            }
        }

        Mesh directMesh = Resources.Load<Mesh>("Models/Durian");
        if (directMesh != null)
        {
            s_CachedDurian3DMesh = directMesh;
            return s_CachedDurian3DMesh;
        }

        return null;
    }

    public static GameObject CreateLocalFeralLungePovVisual(Transform parent, ZombieType zType)
    {
        GameObject root = new GameObject("LocalFeralLungePOV");
        root.transform.SetParent(parent, true);

        bool isBunny = (zType == ZombieType.Bunny);
        Material sleeveMat = MaterialCache.GetColoredMaterial(isBunny ? new Color(0.14f, 0.74f, 0.78f) : new Color(0.42f, 0.16f, 0.58f));
        Material stripeMat = MaterialCache.GetColoredMaterial(new Color(0.98f, 0.38f, 0.56f));
        Material skinMat = MaterialCache.GetColoredMaterial(isBunny ? new Color(0.56f, 0.80f, 0.36f) : new Color(0.50f, 0.74f, 0.30f));
        Material clawMat = MaterialCache.GetColoredMaterial(new Color(0.95f, 0.92f, 0.78f));

        for (int side = -1; side <= 1; side += 2)
        {
            GameObject armPivot = new GameObject(side < 0 ? "LeftDivingArm" : "RightDivingArm");
            armPivot.transform.SetParent(root.transform, false);
            armPivot.transform.localPosition = new Vector3(side * 0.34f, -0.05f, 0.05f);
            armPivot.transform.localRotation = Quaternion.Euler(0f, -side * 14f, -side * 12f);

            CreatePart(PrimitiveType.Cylinder, "Sleeve", armPivot.transform, new Vector3(0f, 0f, -0.10f), Quaternion.Euler(84f, 0f, 0f), new Vector3(0.18f, 0.24f, 0.18f), sleeveMat);
            if (isBunny)
            {
                CreatePart(PrimitiveType.Cylinder, "Cuff", armPivot.transform, new Vector3(0f, 0f, 0.08f), Quaternion.Euler(84f, 0f, 0f), new Vector3(0.19f, 0.05f, 0.19f), stripeMat);
            }
            CreatePart(PrimitiveType.Sphere, "Hand", armPivot.transform, new Vector3(0f, 0.02f, 0.24f), Quaternion.identity, new Vector3(0.20f, 0.15f, 0.24f), skinMat);

            for (int c = -1; c <= 1; c++)
            {
                CreatePart(PrimitiveType.Cube, $"Claw_{c}", armPivot.transform, new Vector3(c * 0.055f, 0.01f, 0.40f), Quaternion.Euler(18f, c * 8f, 0f), new Vector3(0.032f, 0.032f, 0.18f), clawMat);
            }
        }

        return root;
    }

    public static GameObject CreateLocalParryShieldVisual(Transform parent)
    {
        GameObject root = new GameObject("LocalBucketParryShield");
        root.transform.SetParent(parent, true);

        Material shieldMat = MaterialCache.GetColoredMaterial(new Color(0.35f, 0.92f, 1.0f));
        Material rimMat = MaterialCache.GetColoredMaterial(new Color(0.85f, 0.92f, 0.98f));

        CreatePart(PrimitiveType.Cube, "LeftArc", root.transform, new Vector3(-0.42f, 0f, 0f), Quaternion.Euler(0f, -18f, 8f), new Vector3(0.08f, 0.56f, 0.04f), shieldMat);
        CreatePart(PrimitiveType.Cube, "RightArc", root.transform, new Vector3(0.42f, 0f, 0f), Quaternion.Euler(0f, 18f, -8f), new Vector3(0.08f, 0.56f, 0.04f), shieldMat);
        CreatePart(PrimitiveType.Cube, "BottomRim", root.transform, new Vector3(0f, -0.28f, 0f), Quaternion.identity, new Vector3(0.76f, 0.06f, 0.04f), rimMat);

        return root;
    }

    public static GameObject CreateLocalBunnySlideLegsVisual(Transform parent)
    {
        GameObject root = new GameObject("LocalBunnySlideLegs");
        root.transform.SetParent(parent, true);

        Material pantsMat = MaterialCache.GetColoredMaterial(new Color(0.12f, 0.16f, 0.24f));
        Material shoeMat = MaterialCache.GetColoredMaterial(new Color(1.0f, 0.42f, 0.0f));
        Material stripeMat = MaterialCache.GetColoredMaterial(Color.white);

        for (int side = -1; side <= 1; side += 2)
        {
            CreatePart(PrimitiveType.Cylinder, "Leg", root.transform, new Vector3(side * 0.18f, 0f, 0.15f), Quaternion.Euler(82f, 0f, 0f), new Vector3(0.18f, 0.36f, 0.18f), pantsMat);
            GameObject shoe = CreatePart(PrimitiveType.Cube, "Shoe", root.transform, new Vector3(side * 0.18f, 0.08f, 0.52f), Quaternion.Euler(-15f, 0f, 0f), new Vector3(0.20f, 0.28f, 0.14f), shoeMat);
            CreatePart(PrimitiveType.Cube, "Sole", shoe.transform, new Vector3(0f, 0f, 0.45f), Quaternion.identity, new Vector3(1.02f, 1.02f, 0.3f), stripeMat);
        }

        return root;
    }

    public static GameObject CreateLocalHazmatGunPovVisual(Transform parent)
    {
        GameObject root = new GameObject("LocalHazmatAcidGunPOV");
        root.transform.SetParent(parent, true);

        Material yellowMat = MaterialCache.GetColoredMaterial(new Color(0.95f, 0.78f, 0.12f));
        Material darkMetalMat = MaterialCache.GetColoredMaterial(new Color(0.18f, 0.21f, 0.24f));
        Material acidMat = MaterialCache.GetColoredMaterial(new Color(0.30f, 1.0f, 0.22f));
        Material acidCoreMat = MaterialCache.GetColoredMaterial(new Color(0.75f, 1.0f, 0.45f));

        CreatePart(PrimitiveType.Cube, "Receiver", root.transform, Vector3.zero, Quaternion.identity, new Vector3(0.12f, 0.14f, 0.46f), darkMetalMat);
        CreatePart(PrimitiveType.Cube, "Shroud", root.transform, new Vector3(0f, 0.04f, -0.04f), Quaternion.identity, new Vector3(0.14f, 0.09f, 0.28f), yellowMat);
        CreatePart(PrimitiveType.Cylinder, "Canister", root.transform, new Vector3(0f, 0.11f, -0.04f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.09f, 0.14f, 0.09f), acidMat);
        CreatePart(PrimitiveType.Cylinder, "Barrel", root.transform, new Vector3(0f, 0.01f, 0.30f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.07f, 0.18f, 0.07f), darkMetalMat);
        CreatePart(PrimitiveType.Cylinder, "Muzzle", root.transform, new Vector3(0f, 0.01f, 0.47f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.095f, 0.03f, 0.095f), acidCoreMat);
        CreatePart(PrimitiveType.Sphere, "Glove", root.transform, new Vector3(0f, -0.09f, -0.06f), Quaternion.identity, new Vector3(0.12f, 0.12f, 0.14f), darkMetalMat);

        return root;
    }

    private static GameObject CreatePart(
        PrimitiveType primitive,
        string name,
        Transform parent,
        Vector3 localPos,
        Quaternion localRot,
        Vector3 localScale,
        Material mat)
    {
        GameObject part = GameObject.CreatePrimitive(primitive);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPos;
        part.transform.localRotation = localRot;
        part.transform.localScale = localScale;
        MaterialCache.DestroyCollider(part);
        part.GetComponent<Renderer>().sharedMaterial = mat;
        return part;
    }
}

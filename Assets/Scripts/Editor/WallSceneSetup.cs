#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class WallSceneSetup
{
    static WallSceneSetup()
    {
        EditorApplication.delayCall += EnsureWallAndCameraSetup;
    }

    [MenuItem("Tools/Setup 5x8 Wall & 16:9 Orthographic Camera")]
    public static void SetupWallAndCameraMenu()
    {
        SetupWallAndCamera(forceUpdate: true);
    }

    private static void EnsureWallAndCameraSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        GridManager existing = UnityEngine.Object.FindFirstObjectByType<GridManager>();
        MultiplayerMenuManager menuMgr = UnityEngine.Object.FindFirstObjectByType<MultiplayerMenuManager>();

        if (existing == null || menuMgr == null)
        {
            SetupWallAndCamera(forceUpdate: true);
        }
        else
        {
            existing.SyncWallScale();
            menuMgr.EnsureMenuBackdrop(force: true);
            menuMgr.SnapCameraToMenuWall();
            PlayerSettings.runInBackground = true;
            SetGameView16By9();
        }
    }

    public static void SetupWallAndCamera(bool forceUpdate)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        // 1. Find or create the Gameplay Wall GameObject at (0, 4.5, 0)
        GridManager grid = UnityEngine.Object.FindFirstObjectByType<GridManager>();
        GameObject wallObj;

        if (grid != null)
        {
            wallObj = grid.gameObject;
        }
        else
        {
            Transform existingWall = GameObject.Find("Wall")?.transform;
            if (existingWall != null)
            {
                wallObj = existingWall.gameObject;
            }
            else
            {
                wallObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wallObj.name = "Wall";
                Undo.RegisterCreatedObjectUndo(wallObj, "Create 5x8 Wall");
            }

            grid = wallObj.GetComponent<GridManager>();
            if (grid == null)
            {
                grid = wallObj.AddComponent<GridManager>();
            }
        }

        wallObj.transform.position = new Vector3(0f, 4.5f, 0f);
        wallObj.transform.rotation = Quaternion.identity;
        grid.rows = 5;
        grid.columns = 8;
        grid.wallWidth = 16.0f;
        grid.wallHeight = 9.0f;
        grid.wallThickness = 0.2f;
        grid.lockCameraToThisWall = false;

        // 2. Find or create the MenuWall further away at (50, 4.5, 0)
        MultiplayerMenuManager menuMgr = UnityEngine.Object.FindFirstObjectByType<MultiplayerMenuManager>();
        GameObject menuWallObj;

        if (menuMgr != null)
        {
            menuWallObj = menuMgr.gameObject;
        }
        else
        {
            Transform existingMenuWall = GameObject.Find("MenuWall")?.transform;
            if (existingMenuWall != null)
            {
                menuWallObj = existingMenuWall.gameObject;
            }
            else
            {
                menuWallObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                menuWallObj.name = "MenuWall";
                Undo.RegisterCreatedObjectUndo(menuWallObj, "Create MenuWall");
            }

            menuMgr = menuWallObj.GetComponent<MultiplayerMenuManager>();
            if (menuMgr == null)
            {
                menuMgr = menuWallObj.AddComponent<MultiplayerMenuManager>();
            }
        }

        menuWallObj.transform.position = new Vector3(50f, 4.5f, 0f);
        menuWallObj.transform.rotation = Quaternion.identity;
        menuWallObj.transform.localScale = new Vector3(16f, 9f, 0.2f);

        // 3. Configure Main Camera: Centered on MenuWall at (50, 4.5, -10), Orthographic, 16:9
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            GameObject camObj = GameObject.Find("Main Camera");
            if (camObj != null)
            {
                mainCam = camObj.GetComponent<Camera>();
            }
        }

        if (mainCam != null)
        {
            Undo.RecordObject(mainCam, "Configure Orthographic Camera");
            Undo.RecordObject(mainCam.transform, "Align Camera to MenuWall");

            mainCam.orthographic = true;
            mainCam.orthographicSize = 4.5f;
            mainCam.transform.position = new Vector3(50f, 4.5f, -10.0f);
            mainCam.transform.rotation = Quaternion.identity;

            grid.targetCamera = mainCam;
            menuMgr.mainCamera = mainCam;
        }

        menuMgr.menuWallTransform = menuWallObj.transform;
        menuMgr.gameplayWallGrid = grid;

        grid.SyncWallScale();
        menuMgr.EnsureMenuBackdrop(force: true);
        menuMgr.SnapCameraToMenuWall();
        SetGameView16By9();

        EditorUtility.SetDirty(wallObj);
        EditorUtility.SetDirty(menuWallObj);
        if (mainCam != null)
        {
            EditorUtility.SetDirty(mainCam);
        }

        if (!Application.isPlaying)
        {
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
        }
    }

    private static void SetGameView16By9()
    {
        try
        {
            Assembly editorAssembly = typeof(Editor).Assembly;
            Type gameViewType = editorAssembly.GetType("UnityEditor.GameView");
            if (gameViewType == null) return;

            EditorWindow[] gameViews = Resources.FindObjectsOfTypeAll(gameViewType) as EditorWindow[];
            if (gameViews == null || gameViews.Length == 0) return;

            PropertyInfo selectedSizeIndexProp = gameViewType.GetProperty(
                "selectedSizeIndex",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );

            Type sizesType = editorAssembly.GetType("UnityEditor.GameViewSizes");
            Type singletonType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            PropertyInfo instanceProp = singletonType.GetProperty("instance");
            object sizesInstance = instanceProp?.GetValue(null, null);
            MethodInfo currentGroupMethod = sizesType?.GetMethod("currentGroup");
            object currentGroup = currentGroupMethod?.Invoke(sizesInstance, null);

            if (currentGroup != null && selectedSizeIndexProp != null)
            {
                MethodInfo getTotalCount = currentGroup.GetType().GetMethod("GetTotalCount");
                MethodInfo getGameViewSize = currentGroup.GetType().GetMethod("GetGameViewSize");
                int total = (int)getTotalCount.Invoke(currentGroup, null);

                for (int i = 0; i < total; i++)
                {
                    object sizeObj = getGameViewSize.Invoke(currentGroup, new object[] { i });
                    PropertyInfo displayTextProp = sizeObj.GetType().GetProperty("displayText");
                    string text = displayTextProp?.GetValue(sizeObj, null) as string;
                    if (!string.IsNullOrEmpty(text) && text.Contains("16:9"))
                    {
                        foreach (var gv in gameViews)
                        {
                            selectedSizeIndexProp.SetValue(gv, i, null);
                            gv.Repaint();
                        }
                        break;
                    }
                }
            }
        }
        catch
        {
        }
    }
}
#endif

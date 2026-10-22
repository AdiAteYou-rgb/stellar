using System;
using UnityEngine;

[ExecuteAlways]
public class GridManager : MonoBehaviour
{
    [Header("Grid Dimensions (5x8 Plant Spots)")]
    public int rows = 5;
    public int columns = 8;

    [Header("Wall 16:9 Dimensions")]
    public float wallWidth = 16.0f;
    public float wallHeight = 9.0f;
    public float wallThickness = 0.2f;
    public float tilePadding = 0.06f;

    [Header("Camera Alignment")]
    public Camera targetCamera;
    public float cameraDistance = 10.0f;
    public bool enforce16By9Viewport = true;
    public bool lockCameraToThisWall = false;

    [Header("Gameplay State")]
    public bool gameplayActive = true;
    public bool canPlacePlants = true;

    [Header("Plant Placement")]
    [Tooltip("Optional custom plant prefab. If left empty, a placeholder plant is generated automatically.")]
    public GameObject plantPrefab;
    public bool isPlantSelected = false;

    [Header("Visual Colors")]
    public Color tileColorLight = new Color(0.22f, 0.42f, 0.24f, 1.0f);
    public Color tileColorDark = new Color(0.18f, 0.36f, 0.20f, 1.0f);
    public Color tileHoverColor = new Color(0.45f, 0.85f, 0.45f, 1.0f);
    public Color tileOccupiedColor = new Color(0.75f, 0.35f, 0.25f, 1.0f);
    public Color plantPlaceholderColor = new Color(0.2f, 0.9f, 0.3f, 1.0f);

    public event Action<int, int> OnPlantPlacedLocal;

    private PlaceholderPlant[,] placedPlants;
    private Renderer[,] tileRenderers;
    private GameObject tilesContainer;
    private GameObject previewPlantObj;

    private int hoveredRow = -1;
    private int hoveredCol = -1;
    private Material sharedBaseMaterial;

    // Top-left UI button rectangle (screen coordinates)
    private readonly Rect plantButtonRect = new Rect(20f, 20f, 250f, 54f);

    public float TileWidth => columns > 0 ? wallWidth / columns : 2.0f;
    public float TileHeight => rows > 0 ? wallHeight / rows : 1.8f;

    private void Awake()
    {
        SyncWallScale();
        if (Application.isPlaying)
        {
            InitializeRuntimeGrid();
        }
    }

    private void OnEnable()
    {
        SyncWallScale();
    }

    private void OnValidate()
    {
        rows = Mathf.Max(1, rows);
        columns = Mathf.Max(1, columns);
        wallWidth = Mathf.Max(1f, wallWidth);
        wallHeight = Mathf.Max(1f, wallHeight);
        SyncWallScale();
    }

    public void SyncWallScale()
    {
        transform.rotation = Quaternion.identity;
        transform.localScale = new Vector3(wallWidth, wallHeight, wallThickness);

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera != null && enforce16By9Viewport)
        {
            targetCamera.orthographic = true;
            targetCamera.orthographicSize = wallHeight * 0.5f;
            Apply16By9Viewport(targetCamera);
        }
    }

    /// <summary>
    /// Centers and aligns the Orthographic Main Camera directly in front of this wall in 16:9.
    /// </summary>
    [ContextMenu("Align Camera & Wall (16:9)")]
    public void SyncWallScaleAndCamera()
    {
        SyncWallScale();

        if (targetCamera != null)
        {
            targetCamera.orthographic = true;
            targetCamera.orthographicSize = wallHeight * 0.5f;
            targetCamera.transform.position = GetIdealCameraPosition();
            targetCamera.transform.rotation = Quaternion.LookRotation(transform.forward, Vector3.up);

            if (enforce16By9Viewport)
            {
                Apply16By9Viewport(targetCamera);
            }
        }
    }

    public Vector3 GetIdealCameraPosition()
    {
        return transform.position - transform.forward * cameraDistance;
    }

    public void Apply16By9Viewport(Camera cam)
    {
        float targetAspect = 16.0f / 9.0f;
        float windowAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
        float scaleHeight = windowAspect / targetAspect;

        if (Mathf.Abs(scaleHeight - 1.0f) < 0.01f)
        {
            cam.rect = new Rect(0f, 0f, 1f, 1f);
        }
        else if (scaleHeight < 1.0f)
        {
            Rect rect = cam.rect;
            rect.width = 1.0f;
            rect.height = scaleHeight;
            rect.x = 0f;
            rect.y = (1.0f - scaleHeight) * 0.5f;
            cam.rect = rect;
        }
        else
        {
            float scaleWidth = 1.0f / scaleHeight;
            Rect rect = cam.rect;
            rect.width = scaleWidth;
            rect.height = 1.0f;
            rect.x = (1.0f - scaleWidth) * 0.5f;
            rect.y = 0f;
            cam.rect = rect;
        }
    }

    private void InitializeRuntimeGrid()
    {
        placedPlants = new PlaceholderPlant[rows, columns];
        tileRenderers = new Renderer[rows, columns];

        Transform existing = transform.Find("TileSpotsContainer");
        if (existing != null)
        {
            Destroy(existing.gameObject);
        }

        tilesContainer = new GameObject("TileSpotsContainer");
        tilesContainer.transform.SetPositionAndRotation(transform.position, Quaternion.identity);
        tilesContainer.transform.localScale = Vector3.one;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                GameObject tileQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                tileQuad.name = $"Tile_{r}_{c}";
                tileQuad.transform.SetParent(tilesContainer.transform, true);

                Collider col = tileQuad.GetComponent<Collider>();
                if (col != null)
                {
                    Destroy(col);
                }

                Vector3 pos = GetTileWorldPosition(r, c, -0.01f);
                tileQuad.transform.position = pos;
                tileQuad.transform.rotation = Quaternion.identity;
                tileQuad.transform.localScale = new Vector3(
                    Mathf.Max(0.1f, TileWidth - tilePadding),
                    Mathf.Max(0.1f, TileHeight - tilePadding),
                    1.0f
                );

                Renderer rend = tileQuad.GetComponent<Renderer>();
                rend.material = CreateColoredMaterial(GetBaseTileColor(r, c));
                tileRenderers[r, c] = rend;
            }
        }

        CreatePreviewGhost();
    }

    private void CreatePreviewGhost()
    {
        if (previewPlantObj != null)
        {
            Destroy(previewPlantObj);
        }

        previewPlantObj = CreatePlaceholderPlantVisual("PlantPreviewGhost", new Color(0.4f, 1.0f, 0.5f, 0.6f));
        previewPlantObj.SetActive(false);
    }

    private void Update()
    {
        if (lockCameraToThisWall)
        {
            SyncWallScaleAndCamera();
        }
        else
        {
            SyncWallScale();
        }

        if (!Application.isPlaying)
        {
            return;
        }

        if (placedPlants == null || placedPlants.GetLength(0) != rows || placedPlants.GetLength(1) != columns)
        {
            InitializeRuntimeGrid();
        }

        if (!gameplayActive || !canPlacePlants)
        {
            if (previewPlantObj != null && previewPlantObj.activeSelf)
            {
                previewPlantObj.SetActive(false);
            }
            return;
        }

        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
        {
            isPlantSelected = !isPlantSelected;
        }
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            isPlantSelected = false;
        }

        UpdateHoveredTile();
        UpdateTileHighlightsAndGhost();
        HandleMouseClick();
    }

    private void UpdateHoveredTile()
    {
        hoveredRow = -1;
        hoveredCol = -1;

        if (targetCamera == null)
        {
            return;
        }

        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        Vector3 wallFrontCenter = transform.position - transform.forward * (wallThickness * 0.5f);
        Plane wallPlane = new Plane(-transform.forward, wallFrontCenter);

        if (wallPlane.Raycast(ray, out float enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            float leftEdge = transform.position.x - (wallWidth * 0.5f);
            float bottomEdge = transform.position.y - (wallHeight * 0.5f);

            float localX = hitPoint.x - leftEdge;
            float localY = hitPoint.y - bottomEdge;

            if (localX >= 0f && localX < wallWidth && localY >= 0f && localY < wallHeight)
            {
                hoveredCol = Mathf.Clamp(Mathf.FloorToInt(localX / TileWidth), 0, columns - 1);
                hoveredRow = Mathf.Clamp(Mathf.FloorToInt(localY / TileHeight), 0, rows - 1);
            }
        }
    }

    private void UpdateTileHighlightsAndGhost()
    {
        if (tileRenderers == null)
        {
            return;
        }

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                Renderer rend = tileRenderers[r, c];
                if (rend == null) continue;

                if (r == hoveredRow && c == hoveredCol)
                {
                    bool occupied = placedPlants[r, c] != null;
                    rend.material.color = occupied ? tileOccupiedColor : tileHoverColor;
                }
                else
                {
                    rend.material.color = GetBaseTileColor(r, c);
                }
            }
        }

        if (previewPlantObj != null)
        {
            bool showGhost = isPlantSelected && hoveredRow >= 0 && hoveredCol >= 0 && placedPlants[hoveredRow, hoveredCol] == null;
            previewPlantObj.SetActive(showGhost);
            if (showGhost)
            {
                previewPlantObj.transform.position = GetTileWorldPosition(hoveredRow, hoveredCol, -0.25f);
            }
        }
    }

    private void HandleMouseClick()
    {
        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        Vector2 guiMousePos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        if (plantButtonRect.Contains(guiMousePos))
        {
            return;
        }

        if (hoveredRow < 0 || hoveredCol < 0)
        {
            return;
        }

        if (!isPlantSelected)
        {
            isPlantSelected = true;
            return;
        }

        if (placedPlants[hoveredRow, hoveredCol] == null)
        {
            PlacePlantOnTile(hoveredRow, hoveredCol, notifyNetwork: true);
            isPlantSelected = false;
        }
    }

    public PlaceholderPlant PlacePlantOnTile(int row, int col, bool notifyNetwork = false)
    {
        if (row < 0 || row >= rows || col < 0 || col >= columns)
        {
            return null;
        }

        if (placedPlants == null)
        {
            InitializeRuntimeGrid();
        }

        if (placedPlants[row, col] != null)
        {
            return placedPlants[row, col];
        }

        Vector3 spawnPos = GetTileWorldPosition(row, col, -0.25f);
        GameObject plantObj;

        if (plantPrefab != null)
        {
            plantObj = Instantiate(plantPrefab, spawnPos, Quaternion.identity);
        }
        else
        {
            plantObj = CreatePlaceholderPlantVisual($"PlaceholderPlant_{row}_{col}", plantPlaceholderColor);
            plantObj.transform.position = spawnPos;
        }

        if (tilesContainer != null)
        {
            plantObj.transform.SetParent(tilesContainer.transform, true);
        }

        PlaceholderPlant plantComp = plantObj.GetComponent<PlaceholderPlant>();
        if (plantComp == null)
        {
            plantComp = plantObj.AddComponent<PlaceholderPlant>();
        }
        plantComp.Initialize(row, col);

        placedPlants[row, col] = plantComp;

        if (notifyNetwork)
        {
            OnPlantPlacedLocal?.Invoke(row, col);
        }

        return plantComp;
    }

    private GameObject CreatePlaceholderPlantVisual(string objectName, Color color)
    {
        GameObject root = new GameObject(objectName);
        float scale = Mathf.Min(TileWidth, TileHeight) * 0.45f;

        GameObject stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stem.name = "Stem";
        stem.transform.SetParent(root.transform, false);
        stem.transform.localPosition = new Vector3(0f, -scale * 0.35f, 0f);
        stem.transform.localScale = new Vector3(scale * 0.4f, scale * 0.35f, scale * 0.4f);
        Collider stemCol = stem.GetComponent<Collider>();
        if (stemCol != null) Destroy(stemCol);
        stem.GetComponent<Renderer>().material = CreateColoredMaterial(color * 0.75f);

        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(root.transform, false);
        head.transform.localPosition = new Vector3(0f, scale * 0.2f, 0f);
        head.transform.localScale = new Vector3(scale * 0.9f, scale * 0.9f, scale * 0.9f);
        Collider headCol = head.GetComponent<Collider>();
        if (headCol != null) Destroy(headCol);
        head.GetComponent<Renderer>().material = CreateColoredMaterial(color);

        return root;
    }

    public Vector3 GetTileWorldPosition(int row, int column, float surfaceZOffset = -0.05f)
    {
        float leftEdge = transform.position.x - (wallWidth * 0.5f);
        float bottomEdge = transform.position.y - (wallHeight * 0.5f);

        float x = leftEdge + (column + 0.5f) * TileWidth;
        float y = bottomEdge + (row + 0.5f) * TileHeight;
        float z = transform.position.z - (wallThickness * 0.5f) + surfaceZOffset;

        return new Vector3(x, y, z);
    }

    private Color GetBaseTileColor(int row, int col)
    {
        return ((row + col) % 2 == 0) ? tileColorLight : tileColorDark;
    }

    private Material CreateColoredMaterial(Color color)
    {
        if (sharedBaseMaterial == null)
        {
            Renderer ownRenderer = GetComponent<Renderer>();
            if (ownRenderer != null && ownRenderer.sharedMaterial != null)
            {
                sharedBaseMaterial = ownRenderer.sharedMaterial;
            }
        }

        Material mat;
        if (sharedBaseMaterial != null)
        {
            mat = new Material(sharedBaseMaterial);
        }
        else
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Standard");
            mat = new Material(shader);
        }

        mat.color = color;
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", color);
        }
        return mat;
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || !gameplayActive || !canPlacePlants)
        {
            return;
        }

        GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };

        Color prevColor = GUI.backgroundColor;
        GUI.backgroundColor = isPlantSelected ? new Color(0.3f, 1.0f, 0.4f) : new Color(0.85f, 0.85f, 0.85f);

        string label = isPlantSelected
            ? "Placeholder Plant: SELECTED\n(Click a tile to place)"
            : "Select Placeholder Plant\n(Click here or click a tile)";

        if (GUI.Button(plantButtonRect, label, buttonStyle))
        {
            isPlantSelected = !isPlantSelected;
        }

        GUI.backgroundColor = prevColor;
    }

    private void OnDrawGizmos()
    {
        float leftEdge = transform.position.x - (wallWidth * 0.5f);
        float bottomEdge = transform.position.y - (wallHeight * 0.5f);
        float z = transform.position.z - (wallThickness * 0.5f) - 0.02f;

        Gizmos.color = Color.green;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                Vector3 center = new Vector3(
                    leftEdge + (c + 0.5f) * TileWidth,
                    bottomEdge + (r + 0.5f) * TileHeight,
                    z
                );
                Gizmos.DrawWireCube(center, new Vector3(TileWidth - tilePadding, TileHeight - tilePadding, 0.05f));
            }
        }
    }
}

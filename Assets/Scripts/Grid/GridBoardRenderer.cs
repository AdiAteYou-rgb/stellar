using UnityEngine;

/// <summary>
/// Renders the 5x8 2D grid tiles, defense line, garden wooden border frame,
/// hover tile highlights, and unit placement preview ghosts.
/// </summary>
public class GridBoardRenderer : MonoBehaviour
{
    private GridManager grid;
    private GameObject tilesContainer;
    private Renderer[,] tileRenderers;
    private readonly GameObject[] previewGhosts = new GameObject[3];
    private readonly GameObject[] zombiePreviewGhosts = new GameObject[4];

    private int hoveredRow = -1;
    private int hoveredCol = -1;
    private int prevHoveredRow = -1;
    private int prevHoveredCol = -1;
    private bool prevIsZombieSelected = false;
    private bool prevHoveredOccupied = false;

    public GameObject TilesContainer => tilesContainer;
    public int HoveredRow => hoveredRow;
    public int HoveredCol => hoveredCol;

    public void InitializeBoard(GridManager manager)
    {
        grid = manager;
        int rows = grid.rows;
        int columns = grid.columns;

        tileRenderers = new Renderer[rows, columns];

        Transform existing = transform.Find("TileSpotsContainer");
        if (existing != null)
        {
            Destroy(existing.gameObject);
        }

        tilesContainer = new GameObject("TileSpotsContainer");
        tilesContainer.transform.SetPositionAndRotation(transform.position, Quaternion.identity);
        tilesContainer.transform.localScale = Vector3.one;

        Material wallTemplateMat = GetComponent<Renderer>() != null ? GetComponent<Renderer>().sharedMaterial : null;
        GameObject tilePrefab = grid.GameConfig != null ? grid.GameConfig.tilePrefab : null;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < columns; c++)
            {
                GameObject tileObj = tilePrefab != null
                    ? Instantiate(tilePrefab)
                    : GameObject.CreatePrimitive(PrimitiveType.Quad);

                tileObj.name = $"Tile_{r}_{c}";
                tileObj.transform.SetParent(tilesContainer.transform, true);
                MaterialCache.DestroyCollider(tileObj);

                tileObj.transform.position = GetTileWorldPosition(r, c, -0.01f);
                tileObj.transform.rotation = Quaternion.identity;
                tileObj.transform.localScale = new Vector3(
                    Mathf.Max(0.1f, grid.TileWidth - grid.tilePadding),
                    Mathf.Max(0.1f, grid.TileHeight - grid.tilePadding),
                    1.0f
                );

                Renderer rend = tileObj.GetComponent<Renderer>();
                if (rend != null)
                {
                    rend.sharedMaterial = MaterialCache.GetColoredMaterial(GetBaseTileColor(r, c), 0.35f, wallTemplateMat);
                }
                tileRenderers[r, c] = rend;
            }
        }

        prevHoveredRow = -1;
        prevHoveredCol = -1;

        BuildDefenseLineMarker(wallTemplateMat);
        BuildGardenBorderFrame(wallTemplateMat);
        CreatePreviewGhosts();
    }

    private void BuildDefenseLineMarker(Material templateMat)
    {
        Transform existingLine = transform.Find("DefenseLineMarker");
        if (existingLine != null)
        {
            Destroy(existingLine.gameObject);
        }

        GameObject defenseLineObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        defenseLineObj.name = "DefenseLineMarker";
        defenseLineObj.transform.SetParent(transform, true);
        defenseLineObj.transform.position = new Vector3(
            grid.DefenseLineX,
            transform.position.y,
            transform.position.z - (grid.wallThickness * 0.5f) - 0.02f
        );
        defenseLineObj.transform.localScale = new Vector3(0.16f, grid.wallHeight, 1.0f);
        MaterialCache.DestroyCollider(defenseLineObj);
        defenseLineObj.GetComponent<Renderer>().sharedMaterial =
            MaterialCache.GetColoredMaterial(new Color(1.0f, 0.20f, 0.25f, 0.98f), 0.35f, templateMat);
    }

    private void BuildGardenBorderFrame(Material templateMat)
    {
        Transform existingBorder = transform.Find("GardenFrame");
        if (existingBorder != null) Destroy(existingBorder.gameObject);

        GameObject frameRoot = new GameObject("GardenFrame");
        frameRoot.transform.SetParent(transform, false);

        Color woodCol = new Color(0.38f, 0.24f, 0.14f);
        float hw = grid.wallWidth * 0.5f;
        float hh = grid.wallHeight * 0.5f;
        float beamThick = 0.28f;
        float zOffset = -(grid.wallThickness * 0.5f) - 0.03f;

        CreateFrameBeam(frameRoot, new Vector3(0f, hh + beamThick * 0.45f, zOffset), new Vector3(grid.wallWidth + beamThick * 2f, beamThick, 0.15f), woodCol, templateMat);
        CreateFrameBeam(frameRoot, new Vector3(0f, -hh - beamThick * 0.45f, zOffset), new Vector3(grid.wallWidth + beamThick * 2f, beamThick, 0.15f), woodCol, templateMat);
        CreateFrameBeam(frameRoot, new Vector3(-hw - beamThick * 0.45f, 0f, zOffset), new Vector3(beamThick, grid.wallHeight, 0.15f), woodCol, templateMat);
        CreateFrameBeam(frameRoot, new Vector3(hw + beamThick * 0.45f, 0f, zOffset), new Vector3(beamThick, grid.wallHeight, 0.15f), woodCol, templateMat);
    }

    private void CreateFrameBeam(GameObject parent, Vector3 localPos, Vector3 localScale, Color color, Material templateMat)
    {
        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
        beam.name = "FrameBeam";
        beam.transform.SetParent(parent.transform, false);
        beam.transform.localPosition = localPos;
        beam.transform.localScale = localScale;
        MaterialCache.DestroyCollider(beam);
        beam.GetComponent<Renderer>().sharedMaterial = MaterialCache.GetColoredMaterial(color, 0.35f, templateMat);
    }

    private void CreatePreviewGhosts()
    {
        for (int i = 0; i < previewGhosts.Length; i++)
        {
            if (previewGhosts[i] != null)
            {
                Destroy(previewGhosts[i]);
            }
            previewGhosts[i] = GameVisuals.CreatePlant((PlantType)i, $"PreviewGhost_{(PlantType)i}", isGhost: true, grid.TileWidth, grid.TileHeight);
            if (tilesContainer != null)
            {
                previewGhosts[i].transform.SetParent(tilesContainer.transform, true);
            }
            previewGhosts[i].SetActive(false);
        }

        for (int i = 0; i < zombiePreviewGhosts.Length; i++)
        {
            if (zombiePreviewGhosts[i] != null)
            {
                Destroy(zombiePreviewGhosts[i]);
            }
            zombiePreviewGhosts[i] = GameVisuals.CreateZombie((ZombieType)i, $"ZombiePreviewGhost_{(ZombieType)i}", isGhost: true, grid.TileWidth, grid.TileHeight);
            if (tilesContainer != null)
            {
                zombiePreviewGhosts[i].transform.SetParent(tilesContainer.transform, true);
            }
            zombiePreviewGhosts[i].SetActive(false);
        }
    }

    public void UpdateHoveredTile(Camera targetCamera)
    {
        hoveredRow = -1;
        hoveredCol = -1;

        if (targetCamera == null || grid == null)
        {
            return;
        }

        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        Vector3 wallFrontCenter = transform.position - transform.forward * (grid.wallThickness * 0.5f);
        Plane wallPlane = new Plane(-transform.forward, wallFrontCenter);

        if (wallPlane.Raycast(ray, out float enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            float leftEdge = transform.position.x - (grid.wallWidth * 0.5f);
            float bottomEdge = transform.position.y - (grid.wallHeight * 0.5f);

            float localX = hitPoint.x - leftEdge;
            float localY = hitPoint.y - bottomEdge;

            if (localX >= 0f && localX < grid.wallWidth && localY >= 0f && localY < grid.wallHeight)
            {
                hoveredCol = Mathf.Clamp(Mathf.FloorToInt(localX / grid.TileWidth), 0, grid.columns - 1);
                hoveredRow = Mathf.Clamp(Mathf.FloorToInt(localY / grid.TileHeight), 0, grid.rows - 1);
            }
        }
    }

    public void HideAllPreviewGhosts()
    {
        for (int i = 0; i < previewGhosts.Length; i++)
        {
            if (previewGhosts[i] != null && previewGhosts[i].activeSelf)
            {
                previewGhosts[i].SetActive(false);
            }
        }
        for (int i = 0; i < zombiePreviewGhosts.Length; i++)
        {
            if (zombiePreviewGhosts[i] != null && zombiePreviewGhosts[i].activeSelf)
            {
                zombiePreviewGhosts[i].SetActive(false);
            }
        }
    }

    public void UpdateTileHighlightsAndGhost(
        PlaceholderPlant[,] placedPlants,
        bool isPlantSelected,
        PlantType selectedPlantType,
        bool isZombieSelected,
        ZombieType selectedZombieType,
        bool canAffordPlant,
        bool canAffordZombie)
    {
        if (tileRenderers == null || grid == null)
        {
            return;
        }

        int rows = grid.rows;
        int columns = grid.columns;
        bool hoveredOccupied = hoveredRow >= 0 && hoveredRow < rows && hoveredCol >= 0 && hoveredCol < columns && placedPlants[hoveredRow, hoveredCol] != null;

        if (hoveredRow != prevHoveredRow || hoveredCol != prevHoveredCol || isZombieSelected != prevIsZombieSelected || hoveredOccupied != prevHoveredOccupied)
        {
            if (prevHoveredRow >= 0 && prevHoveredRow < rows && prevHoveredCol >= 0 && prevHoveredCol < columns)
            {
                Renderer prevRend = tileRenderers[prevHoveredRow, prevHoveredCol];
                if (prevRend != null)
                {
                    prevRend.sharedMaterial = MaterialCache.GetColoredMaterial(GetBaseTileColor(prevHoveredRow, prevHoveredCol));
                }
            }

            if (hoveredRow >= 0 && hoveredRow < rows && hoveredCol >= 0 && hoveredCol < columns)
            {
                Renderer curRend = tileRenderers[hoveredRow, hoveredCol];
                if (curRend != null)
                {
                    Color col;
                    if (isZombieSelected)
                    {
                        bool allowed = grid.IsColumnAllowedForZombie(hoveredCol);
                        col = allowed ? grid.tileZombieHoverColor : new Color(0.85f, 0.2f, 0.2f, 1.0f);
                    }
                    else
                    {
                        col = hoveredOccupied ? grid.tileOccupiedColor : grid.tileHoverColor;
                    }
                    curRend.sharedMaterial = MaterialCache.GetColoredMaterial(col);
                }
            }

            prevHoveredRow = hoveredRow;
            prevHoveredCol = hoveredCol;
            prevIsZombieSelected = isZombieSelected;
            prevHoveredOccupied = hoveredOccupied;
        }

        bool showPlantGhost = isPlantSelected
                              && canAffordPlant
                              && hoveredRow >= 0
                              && hoveredCol >= 0
                              && placedPlants[hoveredRow, hoveredCol] == null;

        for (int i = 0; i < previewGhosts.Length; i++)
        {
            GameObject ghost = previewGhosts[i];
            if (ghost == null) continue;

            bool active = showPlantGhost && ((int)selectedPlantType == i);
            ghost.SetActive(active);
            if (active)
            {
                ghost.transform.position = GetTileWorldPosition(hoveredRow, hoveredCol, -0.25f);
            }
        }

        bool showZombieGhost = isZombieSelected
                               && canAffordZombie
                               && hoveredRow >= 0
                               && hoveredCol >= 0
                               && grid.IsColumnAllowedForZombie(hoveredCol);

        for (int i = 0; i < zombiePreviewGhosts.Length; i++)
        {
            GameObject ghost = zombiePreviewGhosts[i];
            if (ghost == null) continue;

            bool active = showZombieGhost && ((int)selectedZombieType == i);
            ghost.SetActive(active);
            if (active)
            {
                ghost.transform.position = GetTileWorldPosition(hoveredRow, hoveredCol, -0.3f);
            }
        }
    }

    public Vector3 GetTileWorldPosition(int row, int column, float surfaceZOffset = -0.05f)
    {
        float leftEdge = transform.position.x - (grid.wallWidth * 0.5f);
        float bottomEdge = transform.position.y - (grid.wallHeight * 0.5f);

        float x = leftEdge + (column + 0.5f) * grid.TileWidth;
        float y = bottomEdge + (row + 0.5f) * grid.TileHeight;
        float z = transform.position.z - (grid.wallThickness * 0.5f) + surfaceZOffset;

        return new Vector3(x, y, z);
    }

    public Color GetBaseTileColor(int row, int col)
    {
        if (col >= grid.columns - grid.zombieAllowedColumnsFromRight)
        {
            return ((row + col) % 2 == 0)
                ? new Color(0.28f, 0.22f, 0.35f, 1.0f)
                : new Color(0.20f, 0.16f, 0.28f, 1.0f);
        }
        return ((row + col) % 2 == 0) ? grid.tileColorLight : grid.tileColorDark;
    }
}

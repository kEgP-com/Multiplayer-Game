using UnityEngine;
using Unity.AI.Navigation;

/// <summary>
/// Generates and manages the arena walls and obstacle layout:
/// - Outer perimeter walls (enclosing 60x60m arena)
/// - Central dividing wall with an open doorway (Slides 2 & 6: Enemy -> Hallway -> Door -> Player)
/// - Corridor chokepoint (Slide 8: Agent radius clearance)
/// - Tactical cover pillars and L-obstacles (Slides 3, 10, 12: LoS blocking and detour pathfinding)
/// </summary>
[DisallowMultipleComponent]
public class ArenaWalls : MonoBehaviour
{
    [Header("Wall Configuration")]
    [SerializeField] private float arenaSize = 60f;
    [SerializeField] private float wallHeight = 3.5f;
    [SerializeField] private float wallThickness = 1.0f;
    [SerializeField] private Color wallColor = new Color(0.22f, 0.25f, 0.28f);
    [SerializeField] private bool autoBuildOnAwake = true;

    private Material wallMaterial;

    private void Awake()
    {
        if (autoBuildOnAwake)
        {
            BuildWallsIfMissing();
        }
    }

    public void BuildWallsIfMissing()
    {
        Transform existing = transform.Find("Walls_Container");
        if (existing != null && existing.childCount > 0)
        {
            return; // Walls already built
        }

        BuildAllWalls();
    }

    public void BuildAllWalls()
    {
        // Remove existing container if present
        Transform existing = transform.Find("Walls_Container");
        if (existing != null)
        {
            DestroyImmediate(existing.gameObject);
        }

        GameObject container = new GameObject("Walls_Container");
        container.transform.SetParent(transform, false);

        CreateWallMaterial();

        float half = arenaSize * 0.5f;
        float halfH = wallHeight * 0.5f;

        // 1. Perimeter Boundary Walls
        // North Wall
        CreateWall(container.transform, "Wall_North", new Vector3(0f, halfH, half), new Vector3(arenaSize, wallHeight, wallThickness));
        // South Wall
        CreateWall(container.transform, "Wall_South", new Vector3(0f, halfH, -half), new Vector3(arenaSize, wallHeight, wallThickness));
        // East Wall
        CreateWall(container.transform, "Wall_East", new Vector3(half, halfH, 0f), new Vector3(wallThickness, wallHeight, arenaSize));
        // West Wall
        CreateWall(container.transform, "Wall_West", new Vector3(-half, halfH, 0f), new Vector3(wallThickness, wallHeight, arenaSize));

        // 2. Central Dividing Wall with 10m Doorway (Slides 2 & 6: Enemy -> Hallway -> Door -> Player)
        // Doorway is between X = -5 and X = +5
        float wingLength = (arenaSize - 10f) * 0.5f; // 25m each
        float westWingX = -half + (wingLength * 0.5f); // -17.5
        float eastWingX = half - (wingLength * 0.5f);  // +17.5
        CreateWall(container.transform, "Divider_WestWing", new Vector3(westWingX, halfH, 0f), new Vector3(wingLength, wallHeight, wallThickness));
        CreateWall(container.transform, "Divider_EastWing", new Vector3(eastWingX, halfH, 0f), new Vector3(wingLength, wallHeight, wallThickness));

        // 3. Corridor Chokepoint (Slide 8: Agent radius clearance)
        // 10m wide corridor extending into North zone
        CreateWall(container.transform, "Corridor_Left", new Vector3(-5f, halfH, 10f), new Vector3(wallThickness, wallHeight, 14f));
        CreateWall(container.transform, "Corridor_Right", new Vector3(5f, halfH, 10f), new Vector3(wallThickness, wallHeight, 14f));

        // 4. Tactical Cover Obstacles & L-Walls (Slides 3, 10, 12: LoS blocking & detour paths)
        // South-West L-cover
        CreateWall(container.transform, "Cover_SW_1", new Vector3(-14f, halfH * 0.85f, -14f), new Vector3(7f, wallHeight * 0.85f, 2f));
        CreateWall(container.transform, "Cover_SW_2", new Vector3(-17.5f, halfH * 0.85f, -18f), new Vector3(2f, wallHeight * 0.85f, 8f));

        // South-East L-cover
        CreateWall(container.transform, "Cover_SE_1", new Vector3(14f, halfH * 0.85f, -14f), new Vector3(7f, wallHeight * 0.85f, 2f));
        CreateWall(container.transform, "Cover_SE_2", new Vector3(17.5f, halfH * 0.85f, -18f), new Vector3(2f, wallHeight * 0.85f, 8f));

        // North-West Pillar
        CreateWall(container.transform, "Pillar_NW", new Vector3(-16f, halfH, 16f), new Vector3(5f, wallHeight, 5f));

        // North-East Pillar
        CreateWall(container.transform, "Pillar_NE", new Vector3(16f, halfH, 16f), new Vector3(5f, wallHeight, 5f));

        Debug.Log("[ArenaWalls] Built arena boundary walls, central hallway/door, corridor, and cover pillars!");
    }

    private void CreateWall(Transform parent, string name, Vector3 localPos, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = localPos;
        wall.transform.localScale = scale;

        Renderer r = wall.GetComponent<Renderer>();
        if (r != null && wallMaterial != null)
        {
            r.material = wallMaterial;
        }

        BoxCollider col = wall.GetComponent<BoxCollider>();
        if (col == null)
        {
            col = wall.AddComponent<BoxCollider>();
        }
    }

    private void CreateWallMaterial()
    {
        if (wallMaterial != null) return;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        wallMaterial = new Material(shader)
        {
            color = wallColor
        };
        if (wallMaterial.HasProperty("_BaseColor"))
        {
            wallMaterial.SetColor("_BaseColor", wallColor);
        }
        if (wallMaterial.HasProperty("_Smoothness"))
        {
            wallMaterial.SetFloat("_Smoothness", 0.4f);
        }
    }
}

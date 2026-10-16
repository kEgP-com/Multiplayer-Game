using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
#endif

public static class AISetupEditor
{
    [MenuItem("Tools/AI & Combat/Setup Complete AI System in Scene")]
    public static void SetupCompleteAISystem()
    {
        CreateOrUpdateEnemyPrefab();
        SetupPlayerPrefabsWithShooter();
        SetupSpawnerAndNavMeshInScene();

        EditorUtility.DisplayDialog(
            "AI & Combat Setup Complete",
            "The AI Pathfinding, Spawner, Sensory Indicators, and Player Shooting system have been fully set up!\n\n" +
            "- Enemy prefab created in Assets/Resources/Enemy.prefab\n" +
            "- NavMeshSurface attached to ground and baked\n" +
            "- EnemySpawner created in scene\n" +
            "- PlayerShooter & Health attached to Player prefabs\n\n" +
            "Press Play to test!",
            "Awesome!"
        );
    }

    [MenuItem("Tools/AI & Combat/Create or Update Enemy Prefab")]
    public static void CreateOrUpdateEnemyPrefab()
    {
        if (!Directory.Exists("Assets/Resources"))
        {
            Directory.CreateDirectory("Assets/Resources");
        }

        string prefabPath = "Assets/Resources/Enemy.prefab";

        GameObject enemyGo = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        enemyGo.name = "Enemy";

        // Create distinctive Enemy Material (dark crimson)
        Material enemyMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        enemyMat.color = new Color(0.85f, 0.22f, 0.22f);
        if (enemyMat.HasProperty("_BaseColor")) enemyMat.SetColor("_BaseColor", new Color(0.85f, 0.22f, 0.22f));

        Renderer r = enemyGo.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = enemyMat;

        // NavMeshAgent setup
        NavMeshAgent agent = enemyGo.AddComponent<NavMeshAgent>();
        agent.speed = 2.5f;
        agent.angularSpeed = 240f;
        agent.acceleration = 12f;
        agent.stoppingDistance = 1.6f;
        agent.radius = 0.5f;
        agent.height = 2f;

        // Health setup
        Health health = enemyGo.AddComponent<Health>();
        var healthSo = new SerializedObject(health);
        healthSo.FindProperty("destroyOnDeath").boolValue = true;
        healthSo.FindProperty("maxHealth").floatValue = 100f;
        healthSo.ApplyModifiedProperties();

        // Visual Indicators setup
        enemyGo.AddComponent<EnemyVisualIndicators>();

        // Enemy AI setup
        enemyGo.AddComponent<EnemyAI>();

        // Photon networking setup
#if PHOTON_UNITY_NETWORKING
        PhotonTransformView ptv = enemyGo.AddComponent<PhotonTransformView>();
        PhotonView pv = enemyGo.AddComponent<PhotonView>();
        pv.ObservedComponents = new System.Collections.Generic.List<Component> { ptv };
        pv.Synchronization = ViewSynchronization.ReliableDeltaCompressed;
#endif

        // Save Prefab to Resources and root Assets
        PrefabUtility.SaveAsPrefabAsset(enemyGo, prefabPath);
        PrefabUtility.SaveAsPrefabAsset(enemyGo, "Assets/Enemy.prefab");
        Object.DestroyImmediate(enemyGo);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[AISetupEditor] Successfully built Enemy prefab at '{prefabPath}'!");
    }

    [MenuItem("Tools/AI & Combat/Setup Player Prefabs with Shooter & Health")]
    public static void SetupPlayerPrefabsWithShooter()
    {
        string[] playerPaths = new string[] { "Assets/Resources/Player.prefab", "Assets/Player.prefab" };

        foreach (string path in playerPaths)
        {
            if (!File.Exists(path)) continue;

            GameObject prefab = PrefabUtility.LoadPrefabContents(path);

            // Ensure Health
            Health health = prefab.GetComponent<Health>();
            if (health == null)
            {
                health = prefab.AddComponent<Health>();
            }

            // Ensure PlayerHealth
            PlayerHealth playerHealth = prefab.GetComponent<PlayerHealth>();
            if (playerHealth == null)
            {
                playerHealth = prefab.AddComponent<PlayerHealth>();
            }

            // Ensure PlayerShooter
            PlayerShooter shooter = prefab.GetComponent<PlayerShooter>();
            if (shooter == null)
            {
                shooter = prefab.AddComponent<PlayerShooter>();
            }

            PrefabUtility.SaveAsPrefabAsset(prefab, path);
            PrefabUtility.UnloadPrefabContents(prefab);
            Debug.Log($"[AISetupEditor] Configured PlayerShooter and Health on '{path}'");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("Tools/AI & Combat/Setup Spawner & NavMesh in Scene")]
    public static void SetupSpawnerAndNavMeshInScene()
    {
        // 1. Ensure Ground Plane exists with Collider
        GameObject ground = GameObject.Find("Ground Plane") ?? GameObject.Find("Plane");
        if (ground == null)
        {
            ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground Plane";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(5f, 1f, 5f);
            Undo.RegisterCreatedObjectUndo(ground, "Create Ground Plane");
        }

        // 2. Add NavMeshSurface and build
        NavMeshSurface surface = ground.GetComponent<NavMeshSurface>() ?? Undo.AddComponent<NavMeshSurface>(ground);
        surface.collectObjects = CollectObjects.All;
        surface.BuildNavMesh();
        Debug.Log("[AISetupEditor] NavMesh baked on scene ground!");

        // 3. Ensure EnemySpawner exists
        EnemySpawner spawner = Object.FindFirstObjectByType<EnemySpawner>();
        if (spawner == null)
        {
            GameObject spawnerObj = new GameObject("EnemySpawner");
            Undo.RegisterCreatedObjectUndo(spawnerObj, "Create EnemySpawner");
            spawner = Undo.AddComponent<EnemySpawner>(spawnerObj);
            spawnerObj.transform.position = new Vector3(0f, 0.5f, 0f);
        }

        Selection.activeGameObject = spawner.gameObject;
        EditorGUIUtility.PingObject(spawner.gameObject);
        Debug.Log("[AISetupEditor] EnemySpawner configured in active scene!");
    }

    [MenuItem("Tools/AI & Combat/Build Arena Walls in Scene")]
    public static void BuildArenaWallsInScene()
    {
        ArenaWalls walls = Object.FindFirstObjectByType<ArenaWalls>();
        if (walls == null)
        {
            GameObject wallsObj = new GameObject("ArenaWalls");
            Undo.RegisterCreatedObjectUndo(wallsObj, "Create ArenaWalls");
            walls = Undo.AddComponent<ArenaWalls>(wallsObj);
        }

        walls.BuildAllWalls();

        // Re-bake NavMesh with walls
        SetupSpawnerAndNavMeshInScene();

        EditorUtility.DisplayDialog(
            "Walls Built & Baked",
            "Arena walls have been generated and baked into the NavMesh!\n\n" +
            "- Perimeter boundary walls\n" +
            "- Central dividing wall with 10m doorway\n" +
            "- Corridor chokepoint\n" +
            "- Tactical cover pillars and L-obstacles\n" +
            "- NavMesh updated with obstacle avoidance",
            "OK"
        );
    }
}

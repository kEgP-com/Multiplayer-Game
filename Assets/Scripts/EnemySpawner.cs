using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
#endif

/// <summary>
/// Spawns AI enemies periodically up to a maximum capacity,
/// ensures NavMesh is built and active on scene ground,
/// and handles both singleplayer and multiplayer (MasterClient) authority.
/// </summary>
public class EnemySpawner : MonoBehaviour
{
    public static EnemySpawner Instance { get; private set; }

    [Header("Enemy Prefab")]
    [Tooltip("Enemy prefab to instantiate. If empty, loaded from Resources/Enemy.")]
    [SerializeField] private GameObject enemyPrefab;
    [SerializeField] private string enemyResourceName = "Enemy";

    [Header("Spawn Settings")]
    [Tooltip("Maximum simultaneous alive enemies.")]
    [SerializeField] private int maxEnemies = 6;

    [Tooltip("Seconds between spawn attempts.")]
    [SerializeField] private float spawnInterval = 4.0f;

    [Tooltip("Radius around the spawner to spawn enemies.")]
    [SerializeField] private float spawnRadius = 22f;

    [Tooltip("Minimum distance from players to spawn enemies.")]
    [SerializeField] private float minDistanceFromPlayers = 7f;

    [Header("NavMesh Automation")]
    [Tooltip("Automatically find or add NavMeshSurface and build NavMesh at runtime if needed.")]
    [SerializeField] private bool autoBuildNavMesh = true;

    // Runtime state
    private static int activeEnemyCount = 0;
    private static int totalEnemiesKilled = 0;
    private float nextSpawnTime = 0f;
    private NavMeshSurface navSurface;

    public static int ActiveEnemyCount => activeEnemyCount;
    public static int TotalEnemiesKilled => totalEnemiesKilled;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        activeEnemyCount = 0;

        EnsureNavMesh();
    }

    private void Start()
    {
        if (enemyPrefab == null)
        {
            enemyPrefab = Resources.Load<GameObject>(enemyResourceName);
        }

        // Stagger first spawn slightly so players have time to load in
        nextSpawnTime = Time.time + 2.0f;
    }

    private void Update()
    {
        // In multiplayer, only MasterClient should spawn enemies
#if PHOTON_UNITY_NETWORKING
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            return;
        }
#endif

        if (Time.time >= nextSpawnTime)
        {
            nextSpawnTime = Time.time + spawnInterval;
            TrySpawnEnemy();
        }
    }

    public void EnsureNavMesh()
    {
        if (!autoBuildNavMesh) return;

        // Ensure ArenaWalls are built first so they are included as obstacles in the bake
        ArenaWalls walls = FindFirstObjectByType<ArenaWalls>();
        if (walls != null)
        {
            walls.BuildWallsIfMissing();
        }

        navSurface = FindFirstObjectByType<NavMeshSurface>();
        if (navSurface == null)
        {
            // Find ground plane to attach NavMeshSurface
            GameObject ground = GameObject.Find("Ground Plane") ?? GameObject.Find("Plane");
            if (ground != null)
            {
                navSurface = ground.GetComponent<NavMeshSurface>() ?? ground.AddComponent<NavMeshSurface>();
            }
            else
            {
                navSurface = gameObject.AddComponent<NavMeshSurface>();
            }
        }

        if (navSurface != null)
        {
            navSurface.collectObjects = CollectObjects.All;
            Debug.Log("[EnemySpawner] Baking NavMeshSurface with all arena walls...");
            navSurface.BuildNavMesh();
            Debug.Log("[EnemySpawner] NavMesh baked successfully with wall obstacles!");
        }
    }

    public void TrySpawnEnemy()
    {
        if (activeEnemyCount >= maxEnemies) return;

        if (enemyPrefab == null)
        {
            enemyPrefab = Resources.Load<GameObject>(enemyResourceName);
            if (enemyPrefab == null)
            {
                Debug.LogWarning($"[EnemySpawner] No Enemy prefab found in Inspector or Resources/{enemyResourceName}!");
                return;
            }
        }

        if (FindValidSpawnPosition(out Vector3 spawnPos))
        {
            SpawnEnemyAt(spawnPos);
        }
    }

    private bool FindValidSpawnPosition(out Vector3 result)
    {
        result = Vector3.zero;

        for (int i = 0; i < 15; i++)
        {
            Vector2 randCircle = Random.insideUnitCircle * spawnRadius;
            Vector3 candidate = transform.position + new Vector3(randCircle.x, 0f, randCircle.y);

            // Check NavMesh sampling
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                // Verify distance from all active players
                bool tooClose = false;
                FirstPersonController[] players = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
                foreach (FirstPersonController p in players)
                {
                    if (p != null && Vector3.Distance(hit.position, p.transform.position) < minDistanceFromPlayers)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (!tooClose)
                {
                    result = hit.position;
                    return true;
                }
            }
        }

        return false;
    }

    private void SpawnEnemyAt(Vector3 position)
    {
        GameObject spawned = null;

#if PHOTON_UNITY_NETWORKING
        if (PhotonNetwork.InRoom)
        {
            spawned = PhotonNetwork.InstantiateRoomObject(enemyResourceName, position, Quaternion.identity);
        }
        else
        {
            spawned = Instantiate(enemyPrefab, position, Quaternion.identity);
        }
#else
        spawned = Instantiate(enemyPrefab, position, Quaternion.identity);
#endif

        if (spawned != null)
        {
            activeEnemyCount++;
            Debug.Log($"[EnemySpawner] Spawned AI Enemy at {position}. (Active: {activeEnemyCount}/{maxEnemies})");
        }
    }

    public static void OnEnemyKilled()
    {
        activeEnemyCount = Mathf.Max(0, activeEnemyCount - 1);
        totalEnemiesKilled++;
    }

    public void ResetAllEnemies()
    {
        EnemyAI[] enemies = FindObjectsByType<EnemyAI>(FindObjectsSortMode.None);
        foreach (EnemyAI e in enemies)
        {
            if (e != null)
            {
                Destroy(e.gameObject);
            }
        }
        activeEnemyCount = 0;
        totalEnemiesKilled = 0;
        nextSpawnTime = Time.time + 1.5f;
        Debug.Log("[EnemySpawner] All enemies cleared and spawner reset.");
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
    }
}

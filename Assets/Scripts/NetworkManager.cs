using UnityEngine;
using Photon.Pun;
using Photon.Realtime;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Handles Photon Cloud connection, interactive lobby menu, auto-matchmaking,
/// and first-person player spawning with automatic dummy-player cleanup.
/// </summary>
public class NetworkManager : MonoBehaviourPunCallbacks
{
    public static NetworkManager Instance { get; private set; }

    [Header("Connection Settings")]
    [Tooltip("The game version. Clients with different versions are separated.")]
    [SerializeField] private string gameVersion = "1.0";

    [Tooltip("Skip lobby UI and auto-join immediately on Play.")]
    [SerializeField] private bool autoJoinOnStart = false;

    [Header("Lobby & Matchmaking")]
    [Tooltip("Default player nickname.")]
    [SerializeField] private string defaultPlayerName = "Player";

    [Tooltip("Default room name.")]
    [SerializeField] private string defaultRoomName = "PrudenteRoom";

    [Tooltip("Maximum players allowed per room.")]
    [SerializeField] private byte maxPlayers = 8;

    [Header("Player Spawning")]
    [Tooltip("Name of the networked player prefab in Assets/Resources/")]
    [SerializeField] private string playerPrefabName = "Player";

    [Tooltip("Spawn position for the first-person player.")]
    [SerializeField] private Vector3 spawnPosition = new Vector3(0f, 1.5f, 0f);

    [Header("GUI Settings")]
    [SerializeField] private bool showOverlay = true;

    // Runtime State
    private string playerNameInput;
    private string roomNameInput;
    private string statusMessage = "Connecting to Photon Server...";
    private bool isMatchmakingPending = false;
    private bool hasSpawnedPlayer = false;
    private GameObject spawnedPlayerObj;
    private Camera lobbyCamera;
    private bool showInGameMenu = false;

    public bool IsInGameMenuOpen => showInGameMenu;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        playerNameInput = defaultPlayerName + "_" + Random.Range(100, 999);
        roomNameInput = defaultRoomName;

        Application.runInBackground = true;

        CleanupSceneDummyPlayers();
        EnsureGroundExists();
        EnsureEnemySpawnerExists();
    }

    private void Start()
    {
        EnsureLobbyCamera();
        ConnectToServer();
    }

    private void Update()
    {
        // Keep cursor free and visible while in lobby menu
        if (!PhotonNetwork.InRoom)
        {
            if (Cursor.lockState != CursorLockMode.None || !Cursor.visible)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
        else
        {
            // In room: Escape key toggles In-Game Pause / Exit Menu
            bool escapePressed = false;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                escapePressed = true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (!escapePressed && Input.GetKeyDown(KeyCode.Escape))
            {
                escapePressed = true;
            }
#endif
            if (escapePressed)
            {
                ToggleInGameMenu();
            }
        }
    }

    private void EnsureLobbyCamera()
    {
        if (lobbyCamera != null && lobbyCamera.gameObject.activeInHierarchy) return;

        Camera[] sceneCams = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (Camera c in sceneCams)
        {
            if (c != null && !c.transform.name.Contains("Player"))
            {
                lobbyCamera = c;
                lobbyCamera.gameObject.SetActive(true);
                return;
            }
        }

        // If no standalone camera exists, create one for the lobby view
        GameObject camObj = new GameObject("LobbyCamera");
        lobbyCamera = camObj.AddComponent<Camera>();
        camObj.AddComponent<AudioListener>();
        lobbyCamera.transform.position = new Vector3(0f, 2.5f, -8f);
        lobbyCamera.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
    }

    public void ConnectToServer()
    {
        if (PhotonNetwork.IsConnected) return;
        statusMessage = "Connecting to Photon Server...";
        PhotonNetwork.AutomaticallySyncScene = true;
        PhotonNetwork.GameVersion = gameVersion;
        PhotonNetwork.NickName = playerNameInput;
        PhotonNetwork.ConnectUsingSettings();
    }

    #region Photon Callbacks

    public override void OnConnectedToMaster()
    {
        statusMessage = "Connected to Master Server. Ready to play!";
        Debug.Log("[NetworkManager] " + statusMessage);

        if (isMatchmakingPending || autoJoinOnStart)
        {
            isMatchmakingPending = false;
            JoinGameRoom();
        }
    }

    public override void OnJoinedRoom()
    {
        statusMessage = $"Joined Room: '{PhotonNetwork.CurrentRoom.Name}' (Players: {PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers})";
        Debug.Log("[NetworkManager] " + statusMessage);

        SpawnFirstPersonPlayer();
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        Debug.Log("[NetworkManager] No random room available. Creating room instead...");
        JoinGameRoom();
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        statusMessage = $"Create room failed: {message} ({returnCode})";
        Debug.LogError("[NetworkManager] " + statusMessage);
        isMatchmakingPending = false;
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        statusMessage = $"Join room failed: {message} ({returnCode})";
        Debug.LogError("[NetworkManager] " + statusMessage);
        isMatchmakingPending = false;
    }

    public override void OnLeftRoom()
    {
        statusMessage = "Returned to Lobby. Ready to play!";
        Debug.Log("[NetworkManager] " + statusMessage);

        hasSpawnedPlayer = false;
        isMatchmakingPending = false;
        showInGameMenu = false;

        if (spawnedPlayerObj != null)
        {
            Destroy(spawnedPlayerObj);
            spawnedPlayerObj = null;
        }

        CleanupSceneDummyPlayers();
        EnsureLobbyCamera();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        statusMessage = $"Disconnected: {cause}";
        Debug.LogWarning("[NetworkManager] " + statusMessage);
        hasSpawnedPlayer = false;
        isMatchmakingPending = false;
        showInGameMenu = false;

        if (spawnedPlayerObj != null)
        {
            Destroy(spawnedPlayerObj);
            spawnedPlayerObj = null;
        }

        CleanupSceneDummyPlayers();
        EnsureLobbyCamera();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        Debug.Log($"[NetworkManager] Player '{newPlayer.NickName}' joined the room! (Total: {PhotonNetwork.CurrentRoom.PlayerCount})");
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        Debug.Log($"[NetworkManager] Player '{otherPlayer.NickName}' left the room. (Total: {PhotonNetwork.CurrentRoom.PlayerCount})");
    }

    #endregion

    #region Matchmaking Actions

    public void QuickJoinOrCreate()
    {
        if (PhotonNetwork.InRoom) return;

        PhotonNetwork.NickName = string.IsNullOrEmpty(playerNameInput) ? "Player" : playerNameInput;

        // Verify we are safely on the MasterServer before sending room requests
        if (PhotonNetwork.IsConnectedAndReady && PhotonNetwork.Server == ServerConnection.MasterServer)
        {
            JoinGameRoom();
        }
        else
        {
            isMatchmakingPending = true;
            statusMessage = "Connecting to Master Server... Entering match shortly.";
            Debug.Log("[NetworkManager] Client is connecting/transitioning. Matchmaking will trigger on Master Server.");
            if (!PhotonNetwork.IsConnected)
            {
                ConnectToServer();
            }
        }
    }

    public void JoinOrCreateNamedRoom()
    {
        QuickJoinOrCreate();
    }

    private void JoinGameRoom()
    {
        string targetRoom = string.IsNullOrEmpty(roomNameInput) ? defaultRoomName : roomNameInput;
        statusMessage = $"Joining room '{targetRoom}'...";
        Debug.Log($"[NetworkManager] Joining or creating room '{targetRoom}'");

        RoomOptions options = new RoomOptions
        {
            MaxPlayers = maxPlayers,
            IsOpen = true,
            IsVisible = true
        };

        PhotonNetwork.JoinOrCreateRoom(targetRoom, options, TypedLobby.Default);
    }

    public void LeaveRoom()
    {
        if (PhotonNetwork.InRoom)
        {
            statusMessage = "Leaving room...";
            showInGameMenu = false;

            if (spawnedPlayerObj != null)
            {
                PhotonNetwork.Destroy(spawnedPlayerObj);
                spawnedPlayerObj = null;
            }

            hasSpawnedPlayer = false;
            PhotonNetwork.LeaveRoom();

            EnsureLobbyCamera();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            EnsureLobbyCamera();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    public void ExitGame()
    {
        Debug.Log("[NetworkManager] Exiting application...");
        if (PhotonNetwork.InRoom)
        {
            if (spawnedPlayerObj != null)
            {
                PhotonNetwork.Destroy(spawnedPlayerObj);
                spawnedPlayerObj = null;
            }
            PhotonNetwork.LeaveRoom();
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void ToggleInGameMenu()
    {
        showInGameMenu = !showInGameMenu;
        if (showInGameMenu)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public void CloseInGameMenu()
    {
        showInGameMenu = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    #endregion

    #region Scene & Player Management

    private void CleanupSceneDummyPlayers()
    {
        // 1. Clean up any pre-placed FirstPersonController in the scene
        FirstPersonController[] scenePlayers = FindObjectsByType<FirstPersonController>(FindObjectsSortMode.None);
        foreach (FirstPersonController p in scenePlayers)
        {
            if (p != null)
            {
                PhotonView pv = p.GetComponent<PhotonView>();
                if (pv == null || pv.InstantiationId <= 0)
                {
                    Debug.Log($"[NetworkManager] Removed dummy scene player '{p.name}'.");
                    p.gameObject.SetActive(false);
                    Destroy(p.gameObject);
                }
            }
        }

        // 2. Also clean up any root GameObjects named Player
        GameObject[] roots = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
        foreach (GameObject go in roots)
        {
            if (go != null && (go.name == "Player" || go.name.StartsWith("Player (")))
            {
                PhotonView pv = go.GetComponent<PhotonView>();
                if (pv == null || pv.InstantiationId <= 0)
                {
                    go.SetActive(false);
                    Destroy(go);
                }
            }
        }
    }

    private void EnsureGroundExists()
    {
        Collider[] colliders = FindObjectsByType<Collider>(FindObjectsSortMode.None);
        foreach (Collider col in colliders)
        {
            string colName = col.gameObject.name.ToLower();
            if (colName.Contains("plane") || colName.Contains("ground") || colName.Contains("floor"))
            {
                return; // Ground already exists
            }
        }

        Debug.Log("[NetworkManager] No ground plane detected. Creating a 50x50 Ground Plane at (0, 0, 0)...");
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground Plane";
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(5f, 1f, 5f);
    }

    private void EnsureEnemySpawnerExists()
    {
        if (EnemySpawner.Instance != null) return;
        EnemySpawner spawner = FindFirstObjectByType<EnemySpawner>();
        if (spawner == null)
        {
            GameObject spawnerObj = new GameObject("EnemySpawner");
            spawner = spawnerObj.AddComponent<EnemySpawner>();
            spawnerObj.transform.position = new Vector3(0f, 0.5f, 0f);
            Debug.Log("[NetworkManager] Created EnemySpawner in scene.");
        }
    }

    private void SpawnFirstPersonPlayer()
    {
        if (hasSpawnedPlayer) return;
        hasSpawnedPlayer = true;

        CleanupSceneDummyPlayers();

        // Spawn networked player from Resources
        spawnedPlayerObj = PhotonNetwork.Instantiate(playerPrefabName, spawnPosition, Quaternion.identity);
        Debug.Log($"[NetworkManager] Spawned local first-person player at {spawnedPlayerObj.transform.position}");

        // Disable lobby camera so player's first-person eyes take over
        if (lobbyCamera != null)
        {
            lobbyCamera.gameObject.SetActive(false);
        }
    }

    #endregion

    #region UI Rendering

    private void OnGUI()
    {
        if (!showOverlay) return;

        if (!PhotonNetwork.InRoom)
        {
            DrawLobbyUI();
        }
        else
        {
            DrawInGameHUD();

            if (showInGameMenu)
            {
                DrawInGameMenu();
            }
        }
    }

    private void DrawLobbyUI()
    {
        int panelWidth = 460;
        int panelHeight = 365;
        float x = (Screen.width - panelWidth) * 0.5f;
        float y = (Screen.height - panelHeight) * 0.5f;

        // Semi-transparent dark background
        GUI.Box(new Rect(x, y, panelWidth, panelHeight), GUIContent.none);

        GUILayout.BeginArea(new Rect(x + 20, y + 15, panelWidth - 40, panelHeight - 30));

        // Title
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
        GUILayout.Label("MULTIPLAYER LOBBY", titleStyle);
        GUILayout.Space(8);

        // Status indicator
        string pingText = PhotonNetwork.IsConnected ? $"{PhotonNetwork.GetPing()} ms" : "N/A";
        GUILayout.Label($"<b>Status:</b> {statusMessage} (Ping: {pingText})");
        GUILayout.Space(8);

        // Player Name input
        GUILayout.BeginHorizontal();
        GUILayout.Label("<b>Your Name:</b>", GUILayout.Width(100));
        playerNameInput = GUILayout.TextField(playerNameInput, 20);
        GUILayout.EndHorizontal();

        GUILayout.Space(6);

        // Room Name input
        GUILayout.BeginHorizontal();
        GUILayout.Label("<b>Room Name:</b>", GUILayout.Width(100));
        roomNameInput = GUILayout.TextField(roomNameInput, 20);
        GUILayout.EndHorizontal();

        GUILayout.Space(12);

        // Auto Join / Quick Play button
        string playBtnText = isMatchmakingPending ? "CONNECTING & ENTERING ROOM..." : "PLAY NOW (Auto-Join / Quick Match)";
        if (GUILayout.Button(playBtnText, GUILayout.Height(42)))
        {
            if (!isMatchmakingPending)
            {
                QuickJoinOrCreate();
            }
        }

        GUILayout.Space(6);

        // Join Specific Room button
        string roomBtnText = string.IsNullOrEmpty(roomNameInput) ? defaultRoomName : roomNameInput;
        if (GUILayout.Button($"Join or Create Room: '{roomBtnText}'", GUILayout.Height(34)))
        {
            JoinOrCreateNamedRoom();
        }

        GUILayout.Space(6);

        // Exit Application button
        Color prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.9f, 0.35f, 0.35f);
        if (GUILayout.Button("EXIT GAME", GUILayout.Height(34)))
        {
            ExitGame();
        }
        GUI.backgroundColor = prevBg;

        GUILayout.Space(6);
        GUIStyle noteStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.75f, 0.75f, 0.75f) }
        };
        GUILayout.Label("Anyone with this App ID will auto-connect to the same match!", noteStyle);

        GUILayout.EndArea();
    }

    private void DrawInGameHUD()
    {
        float x = 25f;
        float y = 25f;
        float width = 560f;
        float height = 70f;

        // HUD Card Background
        Color prevColor = GUI.color;
        GUI.color = new Color(0.08f, 0.08f, 0.1f, 0.88f);
        GUI.Box(new Rect(x, y, width, height), GUIContent.none);
        GUI.color = prevColor;

        string pingText = PhotonNetwork.IsConnected ? $"{PhotonNetwork.GetPing()} ms" : "N/A";

        GUIStyle headerStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        GUIStyle contentStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11,
            normal = { textColor = new Color(0.9f, 0.9f, 0.9f) }
        };

        GUI.Label(new Rect(x + 14, y + 8, width - 270, 20), $"Room: <b>{PhotonNetwork.CurrentRoom.Name}</b>", headerStyle);
        GUI.Label(new Rect(x + 14, y + 28, width - 270, 20), $"Players: <b>{PhotonNetwork.CurrentRoom.PlayerCount}</b>  |  Enemies: <b>{EnemySpawner.ActiveEnemyCount}</b>  |  Kills: <b>{EnemySpawner.TotalEnemiesKilled}</b>  |  Ping: {pingText}", contentStyle);
        GUI.Label(new Rect(x + 14, y + 46, width - 270, 18), "<i>Left-Click: Shoot  |  [V]: 1st/3rd Person  |  [Esc]: Menu</i>", new GUIStyle(GUI.skin.label) { fontSize = 10, normal = { textColor = new Color(0.7f, 0.7f, 0.7f) } });

        // Perspective Toggle Button (1st / 3rd Person)
        FirstPersonController localPlayer = FindFirstObjectByType<FirstPersonController>();
        if (localPlayer != null)
        {
            string perspText = localPlayer.CurrentPerspective == FirstPersonController.PerspectiveMode.FirstPerson ? "1st View" : "3rd View";
            if (GUI.Button(new Rect(x + width - 260, y + 15, 80, 40), perspText))
            {
                localPlayer.TogglePerspective();
            }
        }

        // Leave Room Quick Button
        if (GUI.Button(new Rect(x + width - 175, y + 15, 80, 40), "Leave"))
        {
            LeaveRoom();
        }

        // Menu Toggle Button
        if (GUI.Button(new Rect(x + width - 90, y + 15, 78, 40), showInGameMenu ? "Resume" : "Menu"))
        {
            ToggleInGameMenu();
        }
    }

    private void DrawInGameMenu()
    {
        int menuWidth = 380;
        int menuHeight = 270;
        float x = (Screen.width - menuWidth) * 0.5f;
        float y = (Screen.height - menuHeight) * 0.5f;

        // Dark modal backdrop
        Color prevBg = GUI.backgroundColor;
        Color prevColor = GUI.color;

        // Backdrop overlay
        GUI.Box(new Rect(x, y, menuWidth, menuHeight), GUIContent.none);

        GUILayout.BeginArea(new Rect(x + 20, y + 20, menuWidth - 40, menuHeight - 40));

        // Header
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white }
        };
        GUILayout.Label("GAME MENU", titleStyle);
        GUILayout.Space(6);

        GUIStyle subStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }
        };
        GUILayout.Label($"Room: <b>{PhotonNetwork.CurrentRoom.Name}</b>  |  Players: <b>{PhotonNetwork.CurrentRoom.PlayerCount}/{PhotonNetwork.CurrentRoom.MaxPlayers}</b>", subStyle);
        GUILayout.Space(14);

        // Resume Game
        GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
        if (GUILayout.Button("RESUME GAME", GUILayout.Height(42)))
        {
            CloseInGameMenu();
        }
        GUI.backgroundColor = prevBg;

        GUILayout.Space(8);

        // Leave Room back to Lobby
        GUI.backgroundColor = new Color(0.95f, 0.7f, 0.25f);
        if (GUILayout.Button("LEAVE ROOM  (Back to Lobby)", GUILayout.Height(38)))
        {
            LeaveRoom();
        }
        GUI.backgroundColor = prevBg;

        GUILayout.Space(8);

        // Exit Application
        GUI.backgroundColor = new Color(0.9f, 0.35f, 0.35f);
        if (GUILayout.Button("EXIT GAME", GUILayout.Height(38)))
        {
            ExitGame();
        }
        GUI.backgroundColor = prevBg;

        GUILayout.EndArea();

        GUI.color = prevColor;
    }

    #endregion
}

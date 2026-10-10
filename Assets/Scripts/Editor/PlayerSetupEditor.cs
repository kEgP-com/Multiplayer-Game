using UnityEditor;
using UnityEngine;

public static class PlayerSetupEditor
{
    [MenuItem("Tools/First Person/Setup Selected Object as Player")]
    public static void SetupSelectedObject()
    {
        GameObject selected = Selection.activeGameObject;
        if (selected == null)
        {
            EditorUtility.DisplayDialog("No Selection", "Please select a GameObject in the Hierarchy to set up as a First Person Player.", "OK");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(selected, "Setup First Person Player");

        // 1. Ensure FirstPersonController is attached
        FirstPersonController controller = selected.GetComponent<FirstPersonController>();
        if (controller == null)
        {
            controller = Undo.AddComponent<FirstPersonController>(selected);
        }

        // 2. Ensure Rigidbody or CharacterController
        Rigidbody rb = selected.GetComponent<Rigidbody>();
        CharacterController cc = selected.GetComponent<CharacterController>();
        if (rb == null && cc == null)
        {
            rb = Undo.AddComponent<Rigidbody>(selected);
            rb.mass = 1f;
            rb.freezeRotation = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        // 3. Ensure Collider
        Collider col = selected.GetComponent<Collider>();
        if (col == null && cc == null)
        {
            CapsuleCollider capsule = Undo.AddComponent<CapsuleCollider>(selected);
            capsule.height = 2f;
            capsule.radius = 0.5f;
            capsule.center = Vector3.zero;
        }

        // 4. Ensure Camera
        Camera cam = selected.GetComponentInChildren<Camera>();
        if (cam == null)
        {
            GameObject camObj = new GameObject("Main Camera");
            Undo.RegisterCreatedObjectUndo(camObj, "Create Player Camera");
            camObj.transform.SetParent(selected.transform, false);
            camObj.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            camObj.transform.localRotation = Quaternion.identity;
            cam = Undo.AddComponent<Camera>(camObj);
            Undo.AddComponent<AudioListener>(camObj);
            camObj.tag = "MainCamera";
        }
        else
        {
            cam.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        }

        // 5. Disable other cameras in scene
        Camera[] sceneCams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (Camera c in sceneCams)
        {
            if (c != null && c != cam && !c.transform.IsChildOf(selected.transform))
            {
                Undo.RecordObject(c.gameObject, "Disable scene camera");
                c.gameObject.SetActive(false);
            }
        }

        EditorUtility.DisplayDialog("Success", $"'{selected.name}' is now configured with FirstPersonController, camera, physics, and input support!", "Awesome");
    }

    [MenuItem("Tools/First Person/Verify Player & Scene Setup")]
    public static void VerifyScene()
    {
        FirstPersonController controller = Object.FindFirstObjectByType<FirstPersonController>();
        if (controller == null)
        {
            Debug.LogWarning("[Player Setup] No FirstPersonController found in the active scene! Drag Player.prefab into the scene or select an object and use 'Tools > First Person > Setup Selected Object as Player'.");
            return;
        }

        // Check ground plane / collider
        Collider[] allColliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
        bool hasGroundCollider = false;
        foreach (var c in allColliders)
        {
            if (c.gameObject != controller.gameObject && !c.transform.IsChildOf(controller.transform))
            {
                hasGroundCollider = true;
                break;
            }
        }

        if (!hasGroundCollider)
        {
            Debug.LogWarning("[Player Setup] No ground/floor colliders found in the scene! Ensure your Plane or Ground object has a Collider so the player can stand and walk on it.");
        }
        else
        {
            Debug.Log($"[Player Setup] Player '{controller.name}' and ground colliders verified successfully!");
        }

        Selection.activeGameObject = controller.gameObject;
        EditorGUIUtility.PingObject(controller.gameObject);
    }

    [MenuItem("Tools/Photon/Create NetworkManager in Scene")]
    public static void CreateNetworkManagerInScene()
    {
        NetworkManager existing = Object.FindFirstObjectByType<NetworkManager>();
        if (existing != null)
        {
            Selection.activeGameObject = existing.gameObject;
            EditorGUIUtility.PingObject(existing.gameObject);
            EditorUtility.DisplayDialog("Already Exists", "NetworkManager is already present in your scene!", "OK");
            return;
        }

        GameObject nmObj = new GameObject("NetworkManager");
        Undo.RegisterCreatedObjectUndo(nmObj, "Create NetworkManager");
        Undo.AddComponent<NetworkManager>(nmObj);

        Selection.activeGameObject = nmObj;
        EditorGUIUtility.PingObject(nmObj);
        EditorUtility.DisplayDialog("Success", "NetworkManager has been created in your scene! Press Play to connect to Photon Cloud.", "OK");
    }

    [MenuItem("Tools/Photon/Configure Player Prefab with PhotonView")]
    public static void SetupPlayerPrefabWithPhotonView()
    {
        string[] prefabPaths = new string[] { "Assets/Player.prefab", "Assets/Resources/Player.prefab" };
        foreach (string path in prefabPaths)
        {
            if (!System.IO.File.Exists(path)) continue;

            GameObject prefab = PrefabUtility.LoadPrefabContents(path);
            
            Photon.Pun.PhotonTransformView ptv = prefab.GetComponent<Photon.Pun.PhotonTransformView>();
            if (ptv == null) ptv = prefab.AddComponent<Photon.Pun.PhotonTransformView>();
            
            Photon.Pun.PhotonView pv = prefab.GetComponent<Photon.Pun.PhotonView>();
            if (pv == null) pv = prefab.AddComponent<Photon.Pun.PhotonView>();
            
            pv.ObservedComponents = new System.Collections.Generic.List<Component> { ptv };
            pv.Synchronization = Photon.Pun.ViewSynchronization.ReliableDeltaCompressed;

            PrefabUtility.SaveAsPrefabAsset(prefab, path);
            PrefabUtility.UnloadPrefabContents(prefab);
            Debug.Log($"[Photon Setup] Configured PhotonView and PhotonTransformView on '{path}'");
        }
        EditorUtility.DisplayDialog("Success", "Player prefabs in Assets and Assets/Resources are now configured with PhotonView and PhotonTransformView for multiplayer synchronization!", "OK");
    }

    [MenuItem("Tools/Photon/Configure Windowed Mode for Multiplayer Testing")]
    public static void ConfigureWindowedMode()
    {
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;

        EditorUtility.DisplayDialog("Windowed Mode Configured",
            "Standalone build is configured to:\n- Fullscreen Mode: Windowed\n- Resolution: 1280 x 720\n- Resizable Window: Enabled\n- Run In Background: Enabled (Crucial for side-by-side multiplayer testing)\n\nYou can now Build and Run!",
            "OK");
    }
}

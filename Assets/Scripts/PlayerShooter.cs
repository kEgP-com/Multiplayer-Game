using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if PHOTON_UNITY_NETWORKING
using Photon.Pun;
#endif

/// <summary>
/// First-person shooting system with a 3D procedural weapon model,
/// weapon recoil kick, hitscan raycasting, crosshair, dynamic hit marker,
/// muzzle flash, bullet tracers, impact effects, and procedural gunshot sound synthesis.
/// </summary>
[DisallowMultipleComponent]
public class PlayerShooter : MonoBehaviour
{
#if PHOTON_UNITY_NETWORKING
    private PhotonView photonView;
#endif

    [Header("Weapon Settings")]
    [Tooltip("Damage dealt per shot.")]
    [SerializeField] private float damage = 35f;

    [Tooltip("Maximum shooting distance in meters.")]
    [SerializeField] private float maxRange = 100f;

    [Tooltip("Minimum time between shots (fire rate).")]
    [SerializeField] private float fireRate = 0.18f;

    [Tooltip("Layer mask specifying what the bullets can hit.")]
    [SerializeField] private LayerMask hitLayers = ~0;

    [Header("Visual Effects")]
    [SerializeField] private Color tracerColor = new Color(1f, 0.85f, 0.3f, 0.9f);
    [SerializeField] private float tracerDuration = 0.06f;

    [Header("Audio")]
    [SerializeField] private bool enableProceduralAudio = true;
    [SerializeField] [Range(0f, 1f)] private float soundVolume = 0.45f;

    // Internal components
    private Camera playerCamera;
    private AudioSource audioSource;
    private AudioClip gunshotClip;
    private AudioClip hitmarkerClip;
    private float nextFireTime = 0f;

    // Crosshair & Hit marker state
    private float hitMarkerTimer = 0f;
    private const float HitMarkerDuration = 0.12f;

    // Procedural weapon model & recoil
    private Transform gunRoot;
    private Transform gunBarrelTip;
    private Vector3 gunRestLocalPos = new Vector3(0.3f, -0.22f, 0.55f);
    private Quaternion gunRestLocalRot = Quaternion.identity;
    private Coroutine recoilCoroutine;

    // Procedural muzzle flash
    private Light muzzleFlashLight;

    private void Awake()
    {
#if PHOTON_UNITY_NETWORKING
        photonView = GetComponent<PhotonView>();
#endif
        audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        SetupCamera();
        SetupWeaponModel();
        SetupMuzzleFlash();

        if (enableProceduralAudio)
        {
            GenerateProceduralAudioClips();
        }
    }

    private void SetupCamera()
    {
        playerCamera = GetComponentInChildren<Camera>();
        if (playerCamera == null && Camera.main != null && Camera.main.transform.IsChildOf(transform))
        {
            playerCamera = Camera.main;
        }
    }

    private void SetupWeaponModel()
    {
        if (playerCamera == null) SetupCamera();
        if (playerCamera == null) return;

        // Check if gun model already exists
        Transform existing = playerCamera.transform.Find("Weapon_Blaster");
        if (existing != null)
        {
            gunRoot = existing;
            gunBarrelTip = gunRoot.Find("BarrelTip") ?? gunRoot;
            return;
        }

        // Create sleek 3D weapon model attached to camera
        GameObject gunObj = new GameObject("Weapon_Blaster");
        gunObj.transform.SetParent(playerCamera.transform, false);
        gunObj.transform.localPosition = gunRestLocalPos;
        gunObj.transform.localRotation = gunRestLocalRot;
        gunRoot = gunObj.transform;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material bodyMat = new Material(shader) { color = new Color(0.2f, 0.22f, 0.26f) };
        if (bodyMat.HasProperty("_BaseColor")) bodyMat.SetColor("_BaseColor", new Color(0.2f, 0.22f, 0.26f));

        Material barrelMat = new Material(shader) { color = new Color(0.12f, 0.12f, 0.14f) };
        if (barrelMat.HasProperty("_BaseColor")) barrelMat.SetColor("_BaseColor", new Color(0.12f, 0.12f, 0.14f));

        Material glowMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
        glowMat.color = new Color(0.1f, 0.6f, 1f);

        // 1. Receiver / Body
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "GunBody";
        body.transform.SetParent(gunRoot, false);
        body.transform.localPosition = new Vector3(0f, 0f, 0f);
        body.transform.localScale = new Vector3(0.08f, 0.12f, 0.26f);
        Destroy(body.GetComponent<Collider>());
        body.GetComponent<Renderer>().material = bodyMat;

        // 2. Barrel
        GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        barrel.name = "GunBarrel";
        barrel.transform.SetParent(gunRoot, false);
        barrel.transform.localPosition = new Vector3(0f, 0.02f, 0.22f);
        barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        barrel.transform.localScale = new Vector3(0.045f, 0.14f, 0.045f);
        Destroy(barrel.GetComponent<Collider>());
        barrel.GetComponent<Renderer>().material = barrelMat;

        // 3. Sight / Energy core
        GameObject sight = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sight.name = "EnergySight";
        sight.transform.SetParent(gunRoot, false);
        sight.transform.localPosition = new Vector3(0f, 0.07f, -0.02f);
        sight.transform.localScale = new Vector3(0.03f, 0.025f, 0.14f);
        Destroy(sight.GetComponent<Collider>());
        sight.GetComponent<Renderer>().material = glowMat;

        // 4. Barrel Tip marker
        GameObject tipObj = new GameObject("BarrelTip");
        tipObj.transform.SetParent(gunRoot, false);
        tipObj.transform.localPosition = new Vector3(0f, 0.02f, 0.36f);
        gunBarrelTip = tipObj.transform;
    }

    private void SetupMuzzleFlash()
    {
        Transform parent = gunBarrelTip != null ? gunBarrelTip : (playerCamera != null ? playerCamera.transform : transform);
        GameObject flashObj = new GameObject("MuzzleFlashLight");
        flashObj.transform.SetParent(parent, false);
        flashObj.transform.localPosition = Vector3.zero;

        muzzleFlashLight = flashObj.AddComponent<Light>();
        muzzleFlashLight.type = LightType.Point;
        muzzleFlashLight.range = 4f;
        muzzleFlashLight.color = new Color(1f, 0.85f, 0.45f);
        muzzleFlashLight.intensity = 3.5f;
        muzzleFlashLight.enabled = false;
    }

    private void Update()
    {
#if PHOTON_UNITY_NETWORKING
        if (photonView != null && !photonView.IsMine) return;
        // Only block if connected to Photon and still in lobby menu
        if (PhotonNetwork.IsConnected && !PhotonNetwork.InRoom) return;
#endif

        if (PlayerHealth.IsGameOver) return;
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsInGameMenuOpen) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;

        if (hitMarkerTimer > 0f) hitMarkerTimer -= Time.deltaTime;

        if (CheckShootInput() && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + fireRate;
            Shoot();
        }
    }

    private bool CheckShootInput()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetButtonDown("Fire1") || Input.GetMouseButtonDown(0)) return true;
#endif
        return false;
    }

    public void SetWeaponPerspective(bool isThirdPerson)
    {
        if (gunRoot != null)
        {
            gunRoot.gameObject.SetActive(!isThirdPerson);
        }
    }

    private void Shoot()
    {
        if (playerCamera == null) SetupCamera();
        if (playerCamera == null) return;

        bool isThirdPerson = false;
        FirstPersonController controller = GetComponent<FirstPersonController>();
        if (controller != null && controller.CurrentPerspective == FirstPersonController.PerspectiveMode.ThirdPerson)
        {
            isThirdPerson = true;
        }

        // Audio & Visual Recoil
        PlayGunshotAudio();
        if (!isThirdPerson)
        {
            StartCoroutine(ShowMuzzleFlash());
            TriggerRecoil();
        }

        // Raycast from exact crosshair center
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 origin = (!isThirdPerson && gunBarrelTip != null)
            ? gunBarrelTip.position
            : transform.position + Vector3.up * 1.2f + transform.right * 0.35f;
        Vector3 hitPoint = ray.origin + ray.direction * maxRange;

        if (Physics.Raycast(ray, out RaycastHit hit, maxRange, hitLayers, QueryTriggerInteraction.Ignore))
        {
            hitPoint = hit.point;

            // Damage handling
            IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(damage, hit.point, hit.normal);
                TriggerHitMarker();
            }

            CreateImpactEffect(hit.point, hit.normal, damageable != null);
        }

        StartCoroutine(DrawTracerRoutine(origin, hitPoint));
    }

    private void TriggerRecoil()
    {
        if (gunRoot == null) return;
        if (recoilCoroutine != null) StopCoroutine(recoilCoroutine);
        recoilCoroutine = StartCoroutine(RecoilRoutine());
    }

    private IEnumerator RecoilRoutine()
    {
        Vector3 kickPos = gunRestLocalPos + new Vector3(0f, 0.02f, -0.07f);
        Quaternion kickRot = Quaternion.Euler(-7f, 0f, 0f);

        // Fast kick
        float kickDuration = 0.04f;
        float elapsed = 0f;
        while (elapsed < kickDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / kickDuration;
            gunRoot.localPosition = Vector3.Lerp(gunRestLocalPos, kickPos, t);
            gunRoot.localRotation = Quaternion.Slerp(gunRestLocalRot, kickRot, t);
            yield return null;
        }

        // Smooth return
        float returnDuration = 0.12f;
        elapsed = 0f;
        while (elapsed < returnDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / returnDuration;
            gunRoot.localPosition = Vector3.Lerp(kickPos, gunRestLocalPos, t);
            gunRoot.localRotation = Quaternion.Slerp(kickRot, gunRestLocalRot, t);
            yield return null;
        }

        gunRoot.localPosition = gunRestLocalPos;
        gunRoot.localRotation = gunRestLocalRot;
    }

    private void TriggerHitMarker()
    {
        hitMarkerTimer = HitMarkerDuration;
        if (hitmarkerClip != null && audioSource != null)
        {
            audioSource.PlayOneShot(hitmarkerClip, soundVolume * 0.85f);
        }
    }

    private IEnumerator ShowMuzzleFlash()
    {
        if (muzzleFlashLight != null)
        {
            muzzleFlashLight.enabled = true;
            yield return new WaitForSeconds(0.04f);
            muzzleFlashLight.enabled = false;
        }
    }

    private IEnumerator DrawTracerRoutine(Vector3 start, Vector3 end)
    {
        GameObject tracerObj = new GameObject("BulletTracer");
        LineRenderer lr = tracerObj.AddComponent<LineRenderer>();

        Shader shader = Shader.Find("Sprites/Default") 
                        ?? Shader.Find("Universal Render Pipeline/Unlit") 
                        ?? Shader.Find("Unlit/Color");
        if (shader != null) lr.material = new Material(shader);

        lr.startColor = tracerColor;
        lr.endColor = tracerColor;
        lr.startWidth = 0.04f;
        lr.endWidth = 0.02f;
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);

        yield return new WaitForSeconds(tracerDuration);

        Destroy(tracerObj);
    }

    private void CreateImpactEffect(Vector3 point, Vector3 normal, bool isEnemy)
    {
        GameObject impact = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        impact.name = "BulletImpact";
        impact.transform.position = point;
        impact.transform.localScale = Vector3.one * 0.12f;

        Collider col = impact.GetComponent<Collider>();
        if (col != null) Destroy(col);

        Renderer r = impact.GetComponent<Renderer>();
        if (r != null)
        {
            Shader s = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (s != null)
            {
                Material m = new Material(s);
                m.color = isEnemy ? Color.red : new Color(1f, 0.8f, 0.3f);
                r.material = m;
            }
        }

        Destroy(impact, 0.15f);
    }

    #region Procedural Audio Generation

    private void GenerateProceduralAudioClips()
    {
        // 1. Gunshot clip: punchy crack + bass thump
        int sampleRate = 44100;
        int samples = (int)(sampleRate * 0.18f);
        float[] pcm = new float[samples];
        for (int i = 0; i < samples; i++)
        {
            float t = (float)i / samples;
            float envelope = Mathf.Exp(-t * 22f);
            float noise = (Random.value * 2f - 1f);
            float tone = Mathf.Sin(2f * Mathf.PI * (160f - t * 80f) * ((float)i / sampleRate));
            pcm[i] = (noise * 0.7f + tone * 0.3f) * envelope;
        }
        gunshotClip = AudioClip.Create("ProceduralGunshot", samples, 1, sampleRate, false);
        gunshotClip.SetData(pcm, 0);

        // 2. Hitmarker clip: sharp high-pitch ping
        int hitSamples = (int)(sampleRate * 0.05f);
        float[] hitPcm = new float[hitSamples];
        for (int i = 0; i < hitSamples; i++)
        {
            float t = (float)i / hitSamples;
            float envelope = 1f - t;
            float tone = Mathf.Sin(2f * Mathf.PI * 1800f * ((float)i / sampleRate));
            hitPcm[i] = tone * envelope * 0.5f;
        }
        hitmarkerClip = AudioClip.Create("ProceduralHitmarker", hitSamples, 1, sampleRate, false);
        hitmarkerClip.SetData(hitPcm, 0);
    }

    private void PlayGunshotAudio()
    {
        if (audioSource != null && gunshotClip != null)
        {
            audioSource.pitch = Random.Range(0.96f, 1.04f);
            audioSource.PlayOneShot(gunshotClip, soundVolume);
        }
    }

    #endregion

    #region Crosshair & Hitmarker HUD

    private void OnGUI()
    {
#if PHOTON_UNITY_NETWORKING
        if (photonView != null && !photonView.IsMine) return;
        if (PhotonNetwork.IsConnected && !PhotonNetwork.InRoom) return;
#endif
        if (PlayerHealth.IsGameOver) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;

        float centerX = Screen.width * 0.5f;
        float centerY = Screen.height * 0.5f;

        // 1. Center Crosshair (+)
        int size = 9;
        int thickness = 2;
        Color crosshairColor = new Color(1f, 1f, 1f, 0.85f);
        DrawRect(new Rect(centerX - size, centerY - thickness / 2, size * 2, thickness), crosshairColor);
        DrawRect(new Rect(centerX - thickness / 2, centerY - size, thickness, size * 2), crosshairColor);

        // 2. Hit Marker (X) when hitting an enemy
        if (hitMarkerTimer > 0f)
        {
            Color hitColor = new Color(1f, 0.2f, 0.2f, 0.95f);
            int markerSize = 8;
            DrawLineGUI(new Vector2(centerX - markerSize, centerY - markerSize), new Vector2(centerX + markerSize, centerY + markerSize), hitColor, 2f);
            DrawLineGUI(new Vector2(centerX - markerSize, centerY + markerSize), new Vector2(centerX + markerSize, centerY - markerSize), hitColor, 2f);
        }
    }

    private void DrawRect(Rect rect, Color color)
    {
        Color prev = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = prev;
    }

    private void DrawLineGUI(Vector2 pA, Vector2 pB, Color color, float width)
    {
        Color prev = GUI.color;
        GUI.color = color;
        Vector2 delta = pB - pA;
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        Matrix4x4 matrix = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, pA);
        GUI.DrawTexture(new Rect(pA.x, pA.y - width / 2, delta.magnitude, width), Texture2D.whiteTexture);
        GUI.matrix = matrix;
        GUI.color = prev;
    }

    #endregion
}

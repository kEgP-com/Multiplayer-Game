using UnityEngine;

/// <summary>
/// Renders sensory detection indicators for the AI:
/// - Blue for Vision Cone (FOV arc / cone)
/// - Red for Proximity (radial danger zone)
/// - Yellow for Sight Through (unobstructed line-of-sight laser/beam to target)
/// Also manages an overhead dynamic status beacon matching the current highest priority detection.
/// </summary>
[DisallowMultipleComponent]
public class EnemyVisualIndicators : MonoBehaviour
{
    public enum DetectionState
    {
        Idle,
        VisionCone,   // Blue
        SightThrough, // Yellow
        Proximity     // Red
    }

    [Header("Indicator Colors")]
    [SerializeField] private Color proximityColor = new Color(1f, 0.15f, 0.15f, 0.9f); // Red
    [SerializeField] private Color visionConeColor = new Color(0.15f, 0.55f, 1f, 0.85f); // Blue
    [SerializeField] private Color sightThroughColor = new Color(1f, 0.9f, 0.05f, 1f);  // Yellow
    [SerializeField] private Color idleColor = new Color(0.4f, 0.4f, 0.45f, 0.4f);

    [Header("Overhead Status Beacon")]
    [SerializeField] private bool showOverheadBeacon = true;
    [SerializeField] private Vector3 beaconOffset = new Vector3(0f, 2.2f, 0f);
    [SerializeField] private float beaconScale = 0.35f;

    [Header("Ground Projection Visuals")]
    [SerializeField] private bool showGroundIndicators = true;
    [SerializeField] private float groundYOffset = 0.06f;

    // Runtime visual components
    private GameObject beaconObj;
    private Renderer beaconRenderer;
    private Material beaconMaterial;

    private LineRenderer proximityCircleRenderer;
    private LineRenderer visionConeRenderer;
    private LineRenderer sightThroughBeamRenderer;

    private DetectionState currentState = DetectionState.Idle;

    private void Awake()
    {
        SetupBeacon();
        if (showGroundIndicators)
        {
            SetupLineRenderers();
        }
    }

    private void SetupBeacon()
    {
        if (!showOverheadBeacon) return;

        beaconObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        beaconObj.name = "DetectionBeacon";
        beaconObj.transform.SetParent(transform, false);
        beaconObj.transform.localPosition = beaconOffset;
        beaconObj.transform.localScale = Vector3.one * beaconScale;

        // Remove collider from beacon
        Collider col = beaconObj.GetComponent<Collider>();
        if (col != null) Destroy(col);

        beaconRenderer = beaconObj.GetComponent<Renderer>();
        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") 
                             ?? Shader.Find("Sprites/Default") 
                             ?? Shader.Find("Unlit/Color");

        if (unlitShader != null)
        {
            beaconMaterial = new Material(unlitShader);
            beaconRenderer.material = beaconMaterial;
        }

        UpdateBeaconColor(idleColor);
    }

    private void SetupLineRenderers()
    {
        Shader lineShader = Shader.Find("Sprites/Default") 
                            ?? Shader.Find("Universal Render Pipeline/Unlit") 
                            ?? Shader.Find("Unlit/Color");
        Material lineMat = lineShader != null ? new Material(lineShader) : null;

        // 1. Proximity Circle (Red)
        GameObject proxObj = new GameObject("ProximityCircle");
        proxObj.transform.SetParent(transform, false);
        proxObj.transform.localPosition = new Vector3(0f, groundYOffset, 0f);
        proximityCircleRenderer = proxObj.AddComponent<LineRenderer>();
        ConfigureLineRenderer(proximityCircleRenderer, lineMat, proximityColor, 0.07f);
        proximityCircleRenderer.loop = true;

        // 2. Vision Cone Arc (Blue)
        GameObject coneObj = new GameObject("VisionConeArc");
        coneObj.transform.SetParent(transform, false);
        coneObj.transform.localPosition = new Vector3(0f, groundYOffset, 0f);
        visionConeRenderer = coneObj.AddComponent<LineRenderer>();
        ConfigureLineRenderer(visionConeRenderer, lineMat, visionConeColor, 0.07f);
        visionConeRenderer.loop = true;

        // 3. Sight-Through Beam (Yellow)
        GameObject beamObj = new GameObject("SightThroughBeam");
        beamObj.transform.SetParent(transform, false);
        beamObj.transform.localPosition = Vector3.zero;
        sightThroughBeamRenderer = beamObj.AddComponent<LineRenderer>();
        ConfigureLineRenderer(sightThroughBeamRenderer, lineMat, sightThroughColor, 0.09f);
        sightThroughBeamRenderer.loop = false;
        sightThroughBeamRenderer.enabled = false;
    }

    private void ConfigureLineRenderer(LineRenderer lr, Material mat, Color color, float width)
    {
        if (mat != null) lr.material = mat;
        lr.startColor = color;
        lr.endColor = color;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }

    /// <summary>
    /// Update line renderers and overhead beacon based on current AI state.
    /// </summary>
    public void UpdateIndicators(
        float proximityRadius, 
        bool isProximityTriggered,
        float visionRadius, 
        float visionAngle, 
        bool isVisionConeTriggered,
        bool hasSightThrough, 
        Vector3 eyePosition, 
        Transform targetPlayer)
    {
        // Determine current detection state priority:
        // Proximity (Red) > SightThrough (Yellow) > VisionCone (Blue) > Idle
        if (isProximityTriggered)
        {
            currentState = DetectionState.Proximity;
        }
        else if (hasSightThrough)
        {
            currentState = DetectionState.SightThrough;
        }
        else if (isVisionConeTriggered)
        {
            currentState = DetectionState.VisionCone;
        }
        else
        {
            currentState = DetectionState.Idle;
        }

        // 1. Update Beacon Color
        Color activeColor = idleColor;
        switch (currentState)
        {
            case DetectionState.Proximity:
                activeColor = proximityColor;
                break;
            case DetectionState.SightThrough:
                activeColor = sightThroughColor;
                break;
            case DetectionState.VisionCone:
                activeColor = visionConeColor;
                break;
        }
        UpdateBeaconColor(activeColor);

        if (!showGroundIndicators) return;

        Vector3 center = transform.position;
        center.y += groundYOffset;

        // 2. Draw Red Proximity Circle
        if (proximityCircleRenderer != null)
        {
            int segments = 32;
            proximityCircleRenderer.positionCount = segments;
            Color proxCol = isProximityTriggered ? proximityColor : new Color(proximityColor.r, proximityColor.g, proximityColor.b, 0.35f);
            proximityCircleRenderer.startColor = proxCol;
            proximityCircleRenderer.endColor = proxCol;

            for (int i = 0; i < segments; i++)
            {
                float angle = (float)i / segments * Mathf.PI * 2f;
                float x = Mathf.Sin(angle) * proximityRadius;
                float z = Mathf.Cos(angle) * proximityRadius;
                proximityCircleRenderer.SetPosition(i, center + new Vector3(x, 0f, z));
            }
        }

        // 3. Draw Blue Vision Cone Arc
        if (visionConeRenderer != null)
        {
            int arcSegments = 24;
            visionConeRenderer.positionCount = arcSegments + 2; // Center + arc points + close back to center
            Color coneCol = isVisionConeTriggered ? visionConeColor : new Color(visionConeColor.r, visionConeColor.g, visionConeColor.b, 0.45f);
            visionConeRenderer.startColor = coneCol;
            visionConeRenderer.endColor = coneCol;

            visionConeRenderer.SetPosition(0, center);

            float halfFov = visionAngle * 0.5f;
            float currentFacing = transform.eulerAngles.y;

            for (int i = 0; i <= arcSegments; i++)
            {
                float t = (float)i / arcSegments;
                float angleDeg = currentFacing - halfFov + (t * visionAngle);
                float rad = angleDeg * Mathf.Deg2Rad;
                float x = Mathf.Sin(rad) * visionRadius;
                float z = Mathf.Cos(rad) * visionRadius;
                visionConeRenderer.SetPosition(i + 1, center + new Vector3(x, 0f, z));
            }
        }

        // 4. Draw Yellow Sight-Through Beam to target
        if (sightThroughBeamRenderer != null)
        {
            if (hasSightThrough && targetPlayer != null)
            {
                sightThroughBeamRenderer.enabled = true;
                sightThroughBeamRenderer.positionCount = 2;
                sightThroughBeamRenderer.SetPosition(0, eyePosition);
                // Aim beam at player chest height
                Vector3 targetChest = targetPlayer.position + Vector3.up * 1.0f;
                sightThroughBeamRenderer.SetPosition(1, targetChest);
            }
            else
            {
                sightThroughBeamRenderer.enabled = false;
            }
        }
    }

    private void UpdateBeaconColor(Color c)
    {
        if (beaconRenderer != null && beaconRenderer.material != null)
        {
            beaconRenderer.material.color = c;
            if (beaconRenderer.material.HasProperty("_BaseColor"))
            {
                beaconRenderer.material.SetColor("_BaseColor", c);
            }
            if (beaconRenderer.material.HasProperty("_EmissionColor"))
            {
                beaconRenderer.material.SetColor("_EmissionColor", c * 1.5f);
            }
        }
    }

    private void OnDestroy()
    {
        if (beaconMaterial != null) Destroy(beaconMaterial);
    }
}

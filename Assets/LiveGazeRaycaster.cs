using UnityEngine;
using VirtualMaze.Assets.Scripts.Raycasting;

/// <summary>
/// Casts the current eye gaze into the maze and provides an in-game debug view.
/// The dummy option intentionally uses the centre of the subject view so this
/// path can be tested without an EyeLink sample.
/// </summary>
public sealed class LiveGazeRaycaster : MonoBehaviour
{
    [Header("Raycast")]
    [SerializeField] private Camera gazeCamera;
    [SerializeField] private LayerMask gazeLayers = ~0;
    [SerializeField] private float maximumDistance = 1000f;

    [Header("Temporary gaze input")]
    [Tooltip("Use the centre of the subject view instead of EyeLink data.")]
    [SerializeField] private bool useDummyCenterGaze = true;
    [Tooltip("Continue displaying the last valid EyeLink sample between tracker updates.")]
    [SerializeField] private bool keepLastValidSample = true;

    [Header("Mini-camera overlay")]
    [SerializeField] private bool showGazeArea = true;
    [SerializeField] private float gazeAreaRadiusPixels = 42f;
    [SerializeField] private Color hitAreaColor = new Color(0.15f, 1f, 0.35f, 1f);
    [SerializeField] private Color missAreaColor = new Color(1f, 0.75f, 0.1f, 1f);
    [SerializeField] private bool showRayStatus = true;

    [Header("World-space ray")]
    [SerializeField] private bool showWorldRay = true;
    [SerializeField] private float rayWidth = 0.015f;
    [SerializeField] private float missRayLength = 20f;
    [SerializeField] private Color rayHitColor = Color.green;
    [SerializeField] private Color rayMissColor = Color.yellow;

    public GameObject CurrentObject { get; private set; }
    public RaycastHit CurrentHit { get; private set; }
    public Ray CurrentRay { get; private set; }
    public Vector2 CurrentViewportGaze { get; private set; }
    public bool HasCurrentRay { get; private set; }

    private const int GazeTextureSize = 128;

    private LineRenderer rayRenderer;
    private Material rayMaterial;
    private Texture2D gazeAreaTexture;
    private GUIStyle statusStyle;
    private GameObject lastLoggedObject;
    private bool warnedAboutMissingCamera;

    private void Awake()
    {
        if (gazeCamera == null)
        {
            gazeCamera = GetComponentInChildren<Camera>();
        }

        CreateGazeAreaTexture();
        CreateRayRenderer();
    }

    private void Update()
    {
        if (gazeCamera == null)
        {
            ClearCurrentGaze();
            if (!warnedAboutMissingCamera)
            {
                Debug.LogWarning("[LiveGazeRaycaster] A gaze camera is required.", this);
                warnedAboutMissingCamera = true;
            }
            return;
        }

        Vector2 viewportGaze;
        if (!TryGetViewportGaze(out viewportGaze))
        {
            ClearCurrentGaze();
            return;
        }

        CurrentViewportGaze = viewportGaze;
        CurrentRay = gazeCamera.ViewportPointToRay(
            new Vector3(viewportGaze.x, viewportGaze.y, 0f));
        HasCurrentRay = true;

        RaycastHit hit;
        bool didHit = Physics.Raycast(
            CurrentRay,
            out hit,
            maximumDistance,
            gazeLayers);

        if (didHit)
        {
            CurrentHit = hit;
            CurrentObject = hit.collider.gameObject;
            UpdateWorldRay(hit.point, rayHitColor);
            LogObjectChange(CurrentObject);
        }
        else
        {
            CurrentHit = default(RaycastHit);
            CurrentObject = null;
            float visibleMissDistance = Mathf.Min(maximumDistance, missRayLength);
            UpdateWorldRay(CurrentRay.GetPoint(visibleMissDistance), rayMissColor);
            LogObjectChange(null);
        }
    }

    private bool TryGetViewportGaze(out Vector2 viewportGaze)
    {
        if (useDummyCenterGaze)
        {
            viewportGaze = new Vector2(0.5f, 0.5f);
            return true;
        }

        EyeLink.GazeSample sample;
        bool hasNewSample = EyeLink.TryGetLatestSample(out sample);

        // EyeLink.TryGetLatestSample returns false when the tracker has not
        // produced a newer timestamp yet. That is not the same as losing gaze.
        if (!hasNewSample && keepLastValidSample && EyeLink.HasGazeSample)
        {
            sample = EyeLink.LatestGazeSample;
        }

        if ((!hasNewSample && (!keepLastValidSample || !EyeLink.HasGazeSample)) ||
            !sample.isValid || Screen.width <= 0 || Screen.height <= 0)
        {
            viewportGaze = Vector2.zero;
            return false;
        }

        // EyeLink pixels describe the complete subject display. Normalizing
        // first keeps the ray correct when gazeCamera is only a mini viewport.
        viewportGaze = new Vector2(
            sample.unityPixels.x / Screen.width,
            sample.unityPixels.y / Screen.height);

        return viewportGaze.x >= 0f && viewportGaze.x <= 1f &&
            viewportGaze.y >= 0f && viewportGaze.y <= 1f;
    }

    private void ClearCurrentGaze()
    {
        HasCurrentRay = false;
        CurrentObject = null;
        CurrentHit = default(RaycastHit);

        if (rayRenderer != null)
        {
            rayRenderer.enabled = false;
        }
    }

    private void UpdateWorldRay(Vector3 endPoint, Color color)
    {
        if (rayRenderer == null)
        {
            return;
        }

        rayRenderer.enabled = showWorldRay;
        rayRenderer.startWidth = rayWidth;
        rayRenderer.endWidth = rayWidth;
        rayRenderer.startColor = color;
        rayRenderer.endColor = color;
        rayRenderer.SetPosition(0, CurrentRay.origin);
        rayRenderer.SetPosition(1, endPoint);
    }

    private void CreateRayRenderer()
    {
        GameObject rayObject = new GameObject("Live Eye Gaze Ray");
        rayObject.hideFlags = HideFlags.DontSave;
        rayObject.transform.SetParent(transform, false);

        rayRenderer = rayObject.AddComponent<LineRenderer>();
        rayRenderer.useWorldSpace = true;
        rayRenderer.positionCount = 2;
        rayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rayRenderer.receiveShadows = false;
        rayRenderer.enabled = false;

        Shader rayShader = Shader.Find("Sprites/Default");
        if (rayShader == null)
        {
            rayShader = Shader.Find("Unlit/Color");
        }

        if (rayShader != null)
        {
            rayMaterial = new Material(rayShader);
            rayMaterial.hideFlags = HideFlags.HideAndDontSave;
            rayRenderer.sharedMaterial = rayMaterial;
        }
    }

    private void CreateGazeAreaTexture()
    {
        gazeAreaTexture = new Texture2D(
            GazeTextureSize,
            GazeTextureSize,
            TextureFormat.ARGB32,
            false);
        gazeAreaTexture.name = "Live Gaze Area Overlay";
        gazeAreaTexture.hideFlags = HideFlags.HideAndDontSave;
        gazeAreaTexture.wrapMode = TextureWrapMode.Clamp;
        gazeAreaTexture.filterMode = FilterMode.Bilinear;

        Color[] pixels = new Color[GazeTextureSize * GazeTextureSize];
        float centre = (GazeTextureSize - 1) * 0.5f;
        float radius = centre;

        for (int y = 0; y < GazeTextureSize; y++)
        {
            for (int x = 0; x < GazeTextureSize; x++)
            {
                float normalizedDistance = Vector2.Distance(
                    new Vector2(x, y),
                    new Vector2(centre, centre)) / radius;

                float alpha = 0f;
                if (normalizedDistance <= 1f)
                {
                    alpha = 0.14f;
                    if (normalizedDistance >= 0.82f)
                    {
                        alpha = Mathf.Lerp(0.14f, 0.95f,
                            Mathf.InverseLerp(0.82f, 0.9f, normalizedDistance));
                    }

                    bool centreCross = Mathf.Abs(x - centre) <= 1.5f &&
                        Mathf.Abs(y - centre) <= 7f ||
                        Mathf.Abs(y - centre) <= 1.5f &&
                        Mathf.Abs(x - centre) <= 7f;
                    if (centreCross)
                    {
                        alpha = 1f;
                    }
                }

                pixels[y * GazeTextureSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        gazeAreaTexture.SetPixels(pixels);
        gazeAreaTexture.Apply(false, true);
    }

    private void OnGUI()
    {
        if (!showGazeArea || !HasCurrentRay || gazeCamera == null ||
            gazeAreaTexture == null)
        {
            return;
        }

        Vector3 gazeScreenPoint = gazeCamera.ViewportToScreenPoint(
            new Vector3(CurrentViewportGaze.x, CurrentViewportGaze.y, 0f));
        float diameter = Mathf.Max(2f, gazeAreaRadiusPixels * 2f);
        Rect gazeRect = new Rect(
            gazeScreenPoint.x - gazeAreaRadiusPixels,
            Screen.height - gazeScreenPoint.y - gazeAreaRadiusPixels,
            diameter,
            diameter);

        Color previousColor = GUI.color;
        GUI.color = CurrentObject != null ? hitAreaColor : missAreaColor;
        GUI.DrawTexture(gazeRect, gazeAreaTexture, ScaleMode.StretchToFill, true);
        GUI.color = previousColor;

        if (showRayStatus)
        {
            EnsureStatusStyle();
            string source = useDummyCenterGaze ? "DUMMY CENTER" : "EYELINK";
            string hitText = CurrentObject == null
                ? "RAY: NO HIT"
                : "RAY: " + CurrentObject.name + " (" +
                    CurrentHit.distance.ToString("0.00") + "m)";
            Rect labelRect = new Rect(
                gazeScreenPoint.x - 130f,
                Screen.height - gazeScreenPoint.y + gazeAreaRadiusPixels + 4f,
                260f,
                22f);
            GUI.Label(labelRect, source + "  |  " + hitText, statusStyle);
        }
    }

    private void EnsureStatusStyle()
    {
        if (statusStyle != null)
        {
            return;
        }

        // GUI.skin is only safe to access during OnGUI.
        statusStyle = new GUIStyle(GUI.skin.label);
        statusStyle.alignment = TextAnchor.UpperCenter;
        statusStyle.fontSize = 12;
        statusStyle.normal.textColor = Color.white;
    }

    private void LogObjectChange(GameObject newObject)
    {
        if (lastLoggedObject == newObject)
        {
            return;
        }

        lastLoggedObject = newObject;
        if (newObject == null)
        {
            Debug.Log("[LiveGazeRaycaster] Gaze ray is not hitting an object.", this);
            return;
        }

        string fullObjectName = RelativeHitLocFinder.getChainedName(newObject);
        Vector2 relativeHit = RelativeHitLocFinder.getRelativeHit(CurrentHit);
        Debug.Log("[LiveGazeRaycaster] Looking at " + fullObjectName +
            " at " + relativeHit + ".", this);
    }

    private void OnDisable()
    {
        if (rayRenderer != null)
        {
            rayRenderer.enabled = false;
        }
    }

    private void OnDestroy()
    {
        if (gazeAreaTexture != null)
        {
            Destroy(gazeAreaTexture);
        }

        if (rayMaterial != null)
        {
            Destroy(rayMaterial);
        }
    }
}

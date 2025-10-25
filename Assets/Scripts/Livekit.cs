using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Backward compatibility wrapper - Uses [SerializeField] to avoid naming conflicts
/// This class maintains the original public interface while internally delegating to modular components
/// </summary>
public class Livekit : MonoBehaviour
{
    [Header("Connection Settings")]
    [SerializeField] private string _url = "wss://livekit.zwind-robot.com";
    [SerializeField] private string _token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjIxMjA2MzA5NDMsImlkZW50aXR5IjoidnIiLCJpc3MiOiJBUElIdEdUYVV2YzlGNHkiLCJuYW1lIjoidnIiLCJuYmYiOjE3NjA2MzQ1NDMsInN1YiI6InZyIiwidmlkZW8iOnsicm9vbSI6InRlc3Rfcm9vbSIsInJvb21Kb2luIjp0cnVlfX0.j6HIfGId5GIVFjY0th28XneEgDiQ0Vixqf63rehnGr4";

    [Header("UI References")]
    [SerializeField] private GameObject _videoStreamObject;
    [SerializeField] private TMP_Text _infoText;
    [SerializeField] private TMP_Text _connectStatusText;
    [SerializeField] private TMP_Text _buttonStatusText;
    [SerializeField] private Image _connectStatusIcon;
    [SerializeField] private Material _pointCloudMaterial;

    [Header("Status Icons")]
    [SerializeField] private Sprite _disConnectSprite;
    [SerializeField] private Sprite _connectSprite;
    [SerializeField] private Sprite _robotOfflineSprite;
    [SerializeField] private Sprite _teleopSprite;

    [Header("Point Cloud Settings")]
    [Tooltip("Maximum number of point cloud frames to accumulate")]
    [SerializeField] private int _pointCloudBufferSize = 30;
    [Tooltip("Color tint applied to point cloud")]
    [SerializeField] private Color _pointTint = Color.white;
    [Tooltip("VR Camera transform (leave empty to auto-detect CenterEyeAnchor)")]
    [SerializeField] private Transform _vrCameraTransform = null;

    // Public properties for backward compatibility
    public string url { get => _url; set => _url = value; }
    public string token { get => _token; set => _token = value; }
    public GameObject VideoStreamObject { get => _videoStreamObject; set => _videoStreamObject = value; }
    public TMP_Text infoText { get => _infoText; set => _infoText = value; }
    public TMP_Text connectStatusText { get => _connectStatusText; set => _connectStatusText = value; }
    public TMP_Text buttonStatusText { get => _buttonStatusText; set => _buttonStatusText = value; }
    public Image connectStatusIcon { get => _connectStatusIcon; set => _connectStatusIcon = value; }
    public Material pointCloudMaterial { get => _pointCloudMaterial; set => _pointCloudMaterial = value; }
    public Sprite disConnectSprite { get => _disConnectSprite; set => _disConnectSprite = value; }
    public Sprite connectSprite { get => _connectSprite; set => _connectSprite = value; }
    public Sprite robotOfflineSprite { get => _robotOfflineSprite; set => _robotOfflineSprite = value; }
    public Sprite teleopSprite { get => _teleopSprite; set => _teleopSprite = value; }
    public int pointCloudBufferSize { get => _pointCloudBufferSize; set => _pointCloudBufferSize = value; }
    public Color pointTint { get => _pointTint; set => _pointTint = value; }
    public Transform vrCameraTransform { get => _vrCameraTransform; set => _vrCameraTransform = value; }

    // Modular component references
    private LivekitController livekitController;

    void Start()
    {
        SetupModularComponents();
    }

    /// <summary>
    /// Setup modular components
    /// </summary>
    private void SetupModularComponents()
    {
        // Add main controller
        livekitController = GetOrAddComponent<LivekitController>();

        // Add connection manager and configure
        var connectionManager = GetOrAddComponent<ConnectionManager>();
        connectionManager.url = _url;
        connectionManager.token = _token;

        // Add UI controller and configure
        var uiController = GetOrAddComponent<UIController>();
        uiController.VideoStreamObject = _videoStreamObject;
        uiController.infoText = _infoText;
        uiController.connectStatusText = _connectStatusText;
        uiController.buttonStatusText = _buttonStatusText;
        uiController.connectStatusIcon = _connectStatusIcon;
        uiController.disConnectSprite = _disConnectSprite;
        uiController.connectSprite = _connectSprite;
        uiController.robotOfflineSprite = _robotOfflineSprite;
        uiController.teleopSprite = _teleopSprite;

        // Add VR input manager
        GetOrAddComponent<VRInputManager>();

        // Add ROS2 data publisher
        GetOrAddComponent<ROS2DataPublisher>();

        // Add point cloud renderer and configure
        var pointCloudRenderer = GetOrAddComponent<PointCloudRenderer>();
        pointCloudRenderer.pointCloudBufferSize = _pointCloudBufferSize;
        pointCloudRenderer.pointTint = _pointTint;
        pointCloudRenderer.vrCameraTransform = _vrCameraTransform;
        pointCloudRenderer.pointCloudMaterial = _pointCloudMaterial;
    }

    /// <summary>
    /// Helper method to get or add component
    /// </summary>
    private T GetOrAddComponent<T>() where T : Component
    {
        T component = GetComponent<T>();
        if (component == null)
        {
            component = gameObject.AddComponent<T>();
        }
        return component;
    }

    #region Backward compatible public methods

    /// <summary>
    /// Connect button click event - backward compatible
    /// </summary>
    public void onClickConnect()
    {
        livekitController?.OnClickConnect();
    }

    /// <summary>
    /// Go to pick pose - backward compatible
    /// </summary>
    public void onClickGoToPickPose()
    {
        livekitController?.OnClickGoToPickPose();
    }

    /// <summary>
    /// Go to tap pose - backward compatible
    /// </summary>
    public void onClickGoToTapPose()
    {
        livekitController?.OnClickGoToTapPose();
    }

    /// <summary>
    /// Go to sleep pose - backward compatible
    /// </summary>
    public void onClickGoToSleepPose()
    {
        livekitController?.OnClickGoToSleepPose();
    }

    /// <summary>
    /// start movebase control - backward compatible
    /// </summary>
    public void onClickMovebaseControl()
    {
        livekitController?.onClickMovebaseControl();
    }

    /// <summary>
    /// Get connection manager component - for accessing connection status
    /// </summary>
    public ConnectionManager GetConnectionManager()
    {
        return GetComponent<ConnectionManager>();
    }

    /// <summary>
    /// Update connection status text - backward compatible
    /// </summary>
    public void UpdateConnectStatus(string newText)
    {
        var uiController = GetComponent<UIController>();
        uiController?.UpdateConnectStatus(newText);
    }

    /// <summary>
    /// Update info text - backward compatible
    /// </summary>
    public void UpdateInfoText(string newText)
    {
        var uiController = GetComponent<UIController>();
        uiController?.UpdateInfoText(newText);
    }

    /// <summary>
    /// Update button status text - backward compatible
    /// </summary>
    public void UpdateButtonStatusText(string newText)
    {
        var uiController = GetComponent<UIController>();
        uiController?.UpdateButtonStatusText(newText);
    }

    #endregion
}

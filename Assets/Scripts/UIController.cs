using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI controller
/// </summary>
public class UIController : MonoBehaviour
{
    [Header("UI References")]
    public GameObject VideoStreamObject;
    public TMP_Text infoText;
    public TMP_Text connectStatusText;
    public TMP_Text buttonStatusText;
    public Image connectStatusIcon;

    [Header("Status Icons")]
    public Sprite disConnectSprite;
    public Sprite connectSprite;
    public Sprite robotOfflineSprite;
    public Sprite teleopSprite;

    private ConnectionManager connectionManager;

    void Start()
    {
        ValidateUIReferences();
        
        connectionManager = FindFirstObjectByType<ConnectionManager>();
        if (connectionManager != null)
        {
            connectionManager.OnStatusChanged += UpdateUIForStatus;
            UpdateUIForStatus(connectionManager.CurrentStatus);
        }
    }

    /// <summary>
    /// Validate UI references
    /// </summary>
    private void ValidateUIReferences()
    {
        if (VideoStreamObject == null) Debug.LogError("VideoStreamObject is not set!");
        if (infoText == null) Debug.LogError("infoText is not set!");
        if (connectStatusText == null) Debug.LogError("connectStatusText is not set!");
        if (buttonStatusText == null) Debug.LogError("buttonStatusText is not set!");
        if (connectStatusIcon == null) Debug.LogError("connectStatusIcon is not set!");
    }

    /// <summary>
    /// Update UI based on status
    /// </summary>
    private void UpdateUIForStatus(ConnectionStatus status)
    {
        if (connectionManager == null) return;

        var config = connectionManager.GetStatusConfig(status);
        if (config != null)
        {
            UpdateConnectStatus(config.statusText);
            UpdateButtonStatusText(config.buttonText);
            UpdateInfoText(config.infoText);

            if (connectStatusIcon != null)
            {
                connectStatusIcon.sprite = GetStatusSprite(status);
                connectStatusIcon.color = config.statusColor;
            }
        }

        // Control VideoStreamObject visibility based on status
        UpdateVideoStreamVisibility(status);
    }

    /// <summary>
    /// Update video stream visibility based on connection status
    /// </summary>
    private void UpdateVideoStreamVisibility(ConnectionStatus status)
    {
        if (VideoStreamObject != null)
        {
            // Hide video stream in movebase control mode, show in other modes
            bool shouldShow = status != ConnectionStatus.RobotOnlineMovebase;
            VideoStreamObject.SetActive(shouldShow);
            
            Debug.Log($"VideoStreamObject visibility: {(shouldShow ? "Visible" : "Hidden")} (Status: {status})");
        }
    }

    /// <summary>
    /// Get status icon
    /// </summary>
    private Sprite GetStatusSprite(ConnectionStatus status)
    {
        return status switch
        {
            ConnectionStatus.Disconnected => disConnectSprite,
            ConnectionStatus.RoomConnectedRobotOffline => robotOfflineSprite ?? disConnectSprite,
            ConnectionStatus.RobotOnlineIdle => connectSprite,
            ConnectionStatus.RobotOnlineTeleop => teleopSprite ?? connectSprite,
            ConnectionStatus.RobotOnlineMovebase => connectSprite, // Use connect sprite for movebase mode
            _ => disConnectSprite
        };
    }

    /// <summary>
    /// Update connection status text
    /// </summary>
    public void UpdateConnectStatus(string newText)
    {
        if (connectStatusText != null)
            connectStatusText.text = newText;
    }

    /// <summary>
    /// Update info text
    /// </summary>
    public void UpdateInfoText(string newText)
    {
        if (infoText != null)
            infoText.text = newText;
    }

    /// <summary>
    /// Update button status text
    /// </summary>
    public void UpdateButtonStatusText(string newText)
    {
        if (buttonStatusText != null)
            buttonStatusText.text = newText;
    }

    /// <summary>
    /// Clear video display
    /// </summary>
    public void ClearVideoDisplay()
    {
        if (VideoStreamObject != null)
        {
            var image = VideoStreamObject.GetComponentInChildren<RawImage>();
            if (image != null)
            {
                image.color = Color.clear;
                image.texture = null;
            }
        }
    }

    /// <summary>
    /// Set video texture
    /// </summary>
    public void SetVideoTexture(Texture texture)
    {
        if (VideoStreamObject != null)
        {
            var image = VideoStreamObject.GetComponentInChildren<RawImage>();
            if (image != null)
            {
                image.color = Color.white;
                image.texture = texture;
            }
        }
    }

    void OnDestroy()
    {
        if (connectionManager != null)
        {
            connectionManager.OnStatusChanged -= UpdateUIForStatus;
        }
    }
}

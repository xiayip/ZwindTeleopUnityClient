using LiveKit;
using LiveKit.Proto;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// LiveKit main controller - modularized refactored version
/// Coordinates the work of various sub-modules
/// </summary>
public class LivekitController : MonoBehaviour
{
    [Header("Module References")]
    [SerializeField] private ConnectionManager connectionManager;
    [SerializeField] private VRInputManager vrInputManager;
    [SerializeField] private ROS2DataPublisher ros2Publisher;
    [SerializeField] private UIController uiController;
    [SerializeField] private PointCloudRenderer pointCloudRenderer;

    // Teleop state
    private bool inTeleopMode = false;
    private bool inMovebaseControlMode = false;

    void Start()
    {
        InitializeModules();
        SetupEventHandlers();
    }

    void Update()
    {
        // Publish teleop data
        if (inTeleopMode && connectionManager.CurrentStatus == ConnectionStatus.RobotOnlineTeleop)
        {
            ros2Publisher?.PublishTeleopData();
        }
        // Publish movebase control data
        else if (inMovebaseControlMode && connectionManager.CurrentStatus == ConnectionStatus.RobotOnlineMovebase)
        {
            ros2Publisher?.PublishMovebaseControlData();
        }
    }

    /// <summary>
    /// Initialize all modules
    /// </summary>
    private void InitializeModules()
    {
        // Auto-find modules if not manually set
        if (connectionManager == null)
            connectionManager = FindFirstObjectByType<ConnectionManager>();
        
        if (vrInputManager == null)
            vrInputManager = FindFirstObjectByType<VRInputManager>();
        
        if (ros2Publisher == null)
            ros2Publisher = FindFirstObjectByType<ROS2DataPublisher>();
        
        if (uiController == null)
            uiController = FindFirstObjectByType<UIController>();
        
        if (pointCloudRenderer == null)
            pointCloudRenderer = FindFirstObjectByType<PointCloudRenderer>();

        // Validate critical modules
        if (connectionManager == null)
            Debug.LogError("ConnectionManager not found!");
        if (vrInputManager == null)
            Debug.LogError("VRInputManager not found!");
        if (ros2Publisher == null)
            Debug.LogError("ROS2DataPublisher not found!");
    }

    /// <summary>
    /// Setup event handlers
    /// </summary>
    private void SetupEventHandlers()
    {
        if (connectionManager != null)
        {
            connectionManager.OnTrackSubscribed += OnTrackSubscribed;
            connectionManager.OnTrackUnsubscribed += OnTrackUnsubscribed;
            connectionManager.OnDataReceived += OnDataReceived;
            connectionManager.OnParticipantConnected += OnParticipantConnected;
            connectionManager.OnParticipantDisconnected += OnParticipantDisconnected;
        }

        if (vrInputManager != null)
        {
            vrInputManager.OnTeleopStartRequested += StartTeleop;
            vrInputManager.OnTeleopStopRequested += StopTeleop;
        }
    }

    #region Connection Control

    /// <summary>
    /// Connection button click handler
    /// </summary>
    public void OnClickConnect()
    {
        if (connectionManager == null) return;

        switch (connectionManager.CurrentStatus)
        {
            case ConnectionStatus.Disconnected:
            case ConnectionStatus.RoomConnectedRobotOffline:
                StartCoroutine(Connect());
                break;
            case ConnectionStatus.RobotOnlineIdle:
            case ConnectionStatus.RobotOnlineTeleop:
            case ConnectionStatus.RobotOnlineMovebase:
                Disconnect();
                break;
        }
    }

    /// <summary>
    /// Connect to robot
    /// </summary>
    private IEnumerator Connect()
    {
        yield return StartCoroutine(connectionManager.ConnectAsync());

        if (connectionManager.IsConnected)
        {
            // Initialize ROS2 publisher
            ros2Publisher?.Initialize(connectionManager.Room);

            // Record initial camera pose for point cloud
            pointCloudRenderer?.RecordInitialCameraPose();

            Debug.Log("Successfully connected and initialized all modules");
        }
    }

    /// <summary>
    /// Disconnect
    /// </summary>
    private void Disconnect()
    {
        connectionManager?.Disconnect();
        ros2Publisher?.Cleanup();
        uiController?.ClearVideoDisplay();
        pointCloudRenderer?.CleanupPointCloudBuffer();
        
        inTeleopMode = false;
        Debug.Log("Disconnected and cleaned up all modules");
    }

    #endregion

    #region Teleop Control

    /// <summary>
    /// Start teleop mode
    /// </summary>
    private void StartTeleop()
    {
        if (connectionManager?.CurrentStatus != ConnectionStatus.RobotOnlineIdle)
        {
            Debug.LogWarning("Cannot start teleop - robot not in idle state");
            return;
        }

        // Initialize controller pose
        vrInputManager?.InitializeControllerPose();

        // Call ROS service to start teleop
        ros2Publisher?.CallTeleopStart((success) =>
        {
            if (success)
            {
                inTeleopMode = true;
                connectionManager?.SetConnectionStatus(ConnectionStatus.RobotOnlineTeleop);
                Debug.Log("Teleop mode started successfully");
            }
            else
            {
                Debug.LogError("Failed to start teleop mode");
            }
        });
    }

    /// <summary>
    /// Stop teleop mode
    /// </summary>
    private void StopTeleop()
    {
        if (connectionManager?.CurrentStatus != ConnectionStatus.RobotOnlineTeleop)
        {
            Debug.LogWarning("Cannot stop teleop - robot not in teleop state");
            return;
        }

        // Call ROS service to stop teleop
        ros2Publisher?.CallTeleopStop((success) =>
        {
            if (success)
            {
                inTeleopMode = false;
                connectionManager?.SetConnectionStatus(ConnectionStatus.RobotOnlineIdle);
                Debug.Log("Teleop mode stopped successfully");
            }
            else
            {
                Debug.LogError("Failed to stop teleop mode");
            }
        });
    }

    #endregion

    # region Movebase Control

    /// <summary>
    /// Start movebase control mode
    /// </summary>
    public void onClickMovebaseControl()
    {
        if (inMovebaseControlMode)
        {
            StopMovebaseControl();
        } else
        {
            StartMovebaseControl();
        }
        return;
    }

    private void StartMovebaseControl()
    {
        // Call ROS service to start movebase control
        ros2Publisher?.CallMovebaseModeStart((success) =>
        {
            if (success)
            {
                inMovebaseControlMode = true;
                connectionManager?.SetConnectionStatus(ConnectionStatus.RobotOnlineMovebase);
                Debug.Log("Movebase control mode started successfully");
            }
            else
            {
                Debug.LogError("Failed to start movebase control mode");
            }
        });
    }

    private void StopMovebaseControl()
    {
        // Call ROS service to stop movebase control
        ros2Publisher?.CallMovebaseModeStop((success) =>
        {
            if (success)
            {
                inMovebaseControlMode = false;
                connectionManager?.SetConnectionStatus(ConnectionStatus.RobotOnlineIdle);
                Debug.Log("Movebase control mode stopped successfully");
            }
            else
            {
                Debug.LogError("Failed to stop movebase control mode");
            }
        });
    }

    #endregion

    #region Robot Actions

    /// <summary>
    /// Go to pick pose
    /// </summary>
    public void OnClickGoToPickPose()
    {
        if (connectionManager?.CurrentStatus != ConnectionStatus.RobotOnlineIdle)
        {
            Debug.LogWarning("Robot is not in idle state, cannot execute pick pose command");
            return;
        }

        ros2Publisher?.CallGoToPickPose();
    }

    /// <summary>
    /// Go to tap pose
    /// </summary>
    public void OnClickGoToTapPose()
    {
        if (connectionManager?.CurrentStatus != ConnectionStatus.RobotOnlineIdle)
        {
            Debug.LogWarning("Robot is not in idle state, cannot execute tap pose command");
            return;
        }

        ros2Publisher?.CallGoToTapPose();
    }

    /// <summary>
    /// Go to sleep pose
    /// </summary>
    public void OnClickGoToSleepPose()
    {
        if (connectionManager?.CurrentStatus != ConnectionStatus.RobotOnlineIdle)
        {
            Debug.LogWarning("Robot is not in idle state, cannot execute sleep pose command");
            return;
        }

        ros2Publisher?.CallGoToSleepPose();
    }

    #endregion

    #region LiveKit Event Handling

    private void OnParticipantConnected(Participant participant)
    {
        Debug.Log($"Participant connected: {participant.Identity}");
        connectionManager?.CheckRobotOnlineStatus();
    }

    private void OnParticipantDisconnected(Participant participant)
    {
        Debug.Log($"Participant disconnected: {participant.Identity}");
        connectionManager?.CheckRobotOnlineStatus();
    }

    private void OnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        if (track is RemoteVideoTrack videoTrack)
        {
            AddVideoTrack(videoTrack);
        }
    }

    private void OnTrackUnsubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        // Handle track unsubscribed if needed
    }

    private void OnDataReceived(byte[] data, Participant participant, DataPacketKind kind, string topic)
    {
        // Pass point cloud data to point cloud renderer
        if (topic == "pointcloud:meta" || topic == "pointcloud")
        {
            pointCloudRenderer?.HandlePointCloudData(data, topic);
        }
    }

    /// <summary>
    /// Add video track
    /// </summary>
    private void AddVideoTrack(RemoteVideoTrack videoTrack)
    {
        Debug.Log("AddVideoTrack " + videoTrack.Sid);

        if (uiController?.VideoStreamObject != null)
        {
            var image = uiController.VideoStreamObject.GetComponent<RawImage>();
            var stream = new VideoStream(videoTrack);
            stream.TextureReceived += (tex) =>
            {
                uiController?.SetVideoTexture(tex);
            };

            stream.Start();
            StartCoroutine(stream.Update());
        }
    }

    #endregion

    void OnDestroy()
    {
        Disconnect();

        // Cleanup event handlers
        if (connectionManager != null)
        {
            connectionManager.OnTrackSubscribed -= OnTrackSubscribed;
            connectionManager.OnTrackUnsubscribed -= OnTrackUnsubscribed;
            connectionManager.OnDataReceived -= OnDataReceived;
            connectionManager.OnParticipantConnected -= OnParticipantConnected;
            connectionManager.OnParticipantDisconnected -= OnParticipantDisconnected;
        }

        if (vrInputManager != null)
        {
            vrInputManager.OnTeleopStartRequested -= StartTeleop;
            vrInputManager.OnTeleopStopRequested -= StopTeleop;
        }
    }
}

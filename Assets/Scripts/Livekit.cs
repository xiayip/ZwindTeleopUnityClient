using System;
using System.Collections;
using UnityEngine;
using LiveKit;
using LiveKit.Proto;
using UnityEngine.UI;
using RoomOptions = LiveKit.RoomOptions;
using System.Collections.Generic;
using TMPro;
using System.Text.Json;
using LiveKitROS2Bridge;

public class Livekit : MonoBehaviour
{
    #region ״̬ö�ٺ�����

    /// <summary>
    /// ����״̬ö��
    /// </summary>
    public enum ConnectionStatus
    {
        Disconnected,               // δ����
        RoomConnectedRobotOffline, // �����ӷ��䵫�����˲�����
        RobotOnlineIdle,           // ���������ߵ�δ����ң��
        RobotOnlineTeleop          // ��������������ң��
    }

    /// <summary>
    /// ״̬�������ݽṹ
    /// </summary>
    [System.Serializable]
    public class StatusConfig
    {
        public string statusText;
        public string buttonText;
        public string infoText;
        public Sprite statusIcon;
        public Color statusColor = Color.white;
    }

    #endregion

    #region �����ֶ�

    [Header("Connection Settings")]
    //public string url = "wss://zwindz1-lam2j1uj.livekit.cloud";
    //public string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjE3NjA3MzI5ODYsImlzcyI6IkFQSXduZkVoTmNUY2ZzQSIsIm5iZiI6MTc1MTczMjk4Niwic3ViIjoiVlIiLCJ2aWRlbyI6eyJjYW5QdWJsaXNoIjp0cnVlLCJjYW5QdWJsaXNoRGF0YSI6dHJ1ZSwiY2FuU3Vic2NyaWJlIjp0cnVlLCJyb29tIjoibXktcm9vbSIsInJvb21Kb2luIjp0cnVlfH0.oGMrOIDNqyilOWw-6FgqpSKBzyIEp7pZG_FkFtPXji8";

    public string url = "ws://47.111.148.68:7880";
    public string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjE4MjY5NjU3ODIsImlzcyI6InVuaXgiLCJuYW1lIjoidW5pdHkiLCJuYmYiOjE3NTQ5NjU3ODIsInN1YiI6InVuaXR5IiwidmlkZW8iOnsicm9vbSI6ImRpbmdkYW5nIiwicm9vbUpvaW4iOnRydWV9fQ.CADs5YcN1w5b7JJ8_WA_ruP_7NWIcKVZPkZ8SuwZChM";

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

    #endregion

    #region ˽���ֶ�

    // ״̬����
    private ConnectionStatus currentStatus = ConnectionStatus.Disconnected;
    private Dictionary<ConnectionStatus, StatusConfig> statusConfigs;

    // LiveKit���
    private Room room = null;
    private bool inTeleopMode = false;
    private Vector3 initialRightControllerPosition;
    private Quaternion initialRightControllerRotation;

    // ROS2�Ž�
    private LiveKitROS2BridgeManager bridgeManager = null;
    private LiveKitROS2Publisher<TwistMessage> cmdVelPublisher = null;
    private LiveKitROS2Publisher<PoseStampedMessage> eePosePublisher = null;
    private LiveKitROS2Publisher<JointStateMessage> gripperPublisher = null;

    // ֡�ʿ���
    private float lastPublishTime = 0f;
    private const float TARGET_FPS = 30f;
    private const float PUBLISH_INTERVAL = 1f / TARGET_FPS;

    #endregion

    #region Unity��������

    void Start()
    {
        InitializeStatusConfigs();
        ValidateUIReferences();
        SetConnectionStatus(ConnectionStatus.Disconnected);
    }

    void Update()
    {
        HandleVRInput();
        PublishTeleopData();
    }

    void OnDestroy()
    {
        Disconnect();
    }

    #endregion

    #region ״̬����

    /// <summary>
    /// ��ʼ��״̬����
    /// </summary>
    private void InitializeStatusConfigs()
    {
        statusConfigs = new Dictionary<ConnectionStatus, StatusConfig>
        {
            [ConnectionStatus.Disconnected] = new StatusConfig
            {
                statusText = "Disconnected",
                buttonText = "Connect Robot",
                infoText = "Please Connect to Robot First",
                statusIcon = disConnectSprite,
                statusColor = Color.red
            },
            [ConnectionStatus.RoomConnectedRobotOffline] = new StatusConfig
            {
                statusText = "Robot Offline",
                buttonText = "Refresh Connection",
                infoText = "Robot is not online, please check robot status",
                statusIcon = robotOfflineSprite ?? disConnectSprite,
                statusColor = Color.yellow
            },
            [ConnectionStatus.RobotOnlineIdle] = new StatusConfig
            {
                statusText = "Robot Ready",
                buttonText = "Disconnect Robot",
                infoText = "Press B to start Teleop",
                statusIcon = connectSprite,
                statusColor = Color.green
            },
            [ConnectionStatus.RobotOnlineTeleop] = new StatusConfig
            {
                statusText = "Teleoperating",
                buttonText = "Disconnect Robot",
                infoText = "Teleop Mode Active - Press B to stop",
                statusIcon = teleopSprite ?? connectSprite,
                statusColor = Color.cyan
            }
        };
    }

    /// <summary>
    /// ��������״̬
    /// </summary>
    public void SetConnectionStatus(ConnectionStatus newStatus)
    {
        if (currentStatus != newStatus)
        {
            Debug.Log($"Status changed: {currentStatus} -> {newStatus}");
            currentStatus = newStatus;
            UpdateUIForStatus(newStatus);
        }
    }

    /// <summary>
    /// ��ȡ��ǰ״̬
    /// </summary>
    public ConnectionStatus GetCurrentStatus() => currentStatus;

    /// <summary>
    /// ����״̬����UI
    /// </summary>
    private void UpdateUIForStatus(ConnectionStatus status)
    {
        if (statusConfigs.TryGetValue(status, out var config))
        {
            UpdateConnectStatus(config.statusText);
            UpdateButtonStatusText(config.buttonText);
            UpdateInfoText(config.infoText);

            if (connectStatusIcon != null)
            {
                connectStatusIcon.sprite = config.statusIcon;
                connectStatusIcon.color = config.statusColor;
            }
        }
    }

    /// <summary>
    /// ��鲢���»���������״̬
    /// </summary>
    private void CheckRobotOnlineStatus()
    {
        bool robotOnline = room != null && room.RemoteParticipants.Count > 0;

        if (robotOnline)
        {
            if (inTeleopMode)
                SetConnectionStatus(ConnectionStatus.RobotOnlineTeleop);
            else
                SetConnectionStatus(ConnectionStatus.RobotOnlineIdle);
        }
        else
        {
            if (room != null && room.IsConnected)
                SetConnectionStatus(ConnectionStatus.RoomConnectedRobotOffline);
            else
                SetConnectionStatus(ConnectionStatus.Disconnected);
        }
    }

    #endregion

    #region VR���봦��

    private void HandleVRInput()
    {
        if (room == null) return;

        if (OVRInput.GetUp(OVRInput.RawButton.B))
        {
            switch (currentStatus)
            {
                case ConnectionStatus.RobotOnlineIdle:
                    StartTeleop();
                    break;
                case ConnectionStatus.RobotOnlineTeleop:
                    StopTeleop();
                    break;
            }
        }
    }

    private void StartTeleop()
    {
        CallTeleopStart();
        (initialRightControllerPosition, initialRightControllerRotation) = getControllerPose();
        lastPublishTime = Time.time;
        Debug.Log("Starting Teleop Mode");
    }

    private void StopTeleop()
    {
        inTeleopMode = false;
        CheckRobotOnlineStatus();
        Debug.Log("Stopping Teleop Mode");
    }

    private void PublishTeleopData()
    {
        if (currentStatus == ConnectionStatus.RobotOnlineTeleop &&
            Time.time - lastPublishTime >= PUBLISH_INTERVAL)
        {
            PublishVelocityData();
            PublishEEPoseData();
            PublishTriggerValue();
            lastPublishTime = Time.time;
        }
    }

    #endregion

    #region ���ӹ���

    public void onClickConnect()
    {
        switch (currentStatus)
        {
            case ConnectionStatus.Disconnected:
            case ConnectionStatus.RoomConnectedRobotOffline:
                StartCoroutine(Connect());
                break;
            case ConnectionStatus.RobotOnlineIdle:
            case ConnectionStatus.RobotOnlineTeleop:
                Disconnect();
                break;
        }
    }

    IEnumerator Connect()
    {
        if (room == null)
        {
            room = new Room();
            room.TrackSubscribed += TrackSubscribed;
            room.TrackUnsubscribed += UnTrackSubscribed;
            room.DataReceived += DataReceived;
            room.ParticipantConnected += OnParticipantConnected;
            room.ParticipantDisconnected += OnParticipantDisconnected;

            var options = new RoomOptions();
            var connect = room.Connect(url, token, options);
            yield return connect;

            if (!connect.IsError)
            {
                Debug.Log("Connected to " + room.Name);

                bridgeManager = new LiveKitROS2BridgeManager(room);
                cmdVelPublisher = bridgeManager.CreatePublisher<TwistMessage>("cmd_vel");
                eePosePublisher = bridgeManager.CreatePublisher<PoseStampedMessage>("ee_offset_pose");
                gripperPublisher = bridgeManager.CreatePublisher<JointStateMessage>("main_gripper/gripper_command");

                CheckRobotOnlineStatus();
            }
            else
            {
                Debug.LogError("Failed to connect to room: " + connect.IsError);
                SetConnectionStatus(ConnectionStatus.Disconnected);
            }
        }
    }

    void Disconnect()
    {
        if (room != null)
        {
            room.ParticipantConnected -= OnParticipantConnected;
            room.ParticipantDisconnected -= OnParticipantDisconnected;
            room.Disconnect();
            room = null;
        }

        bridgeManager?.Dispose();
        bridgeManager = null;
        inTeleopMode = false;

        // ������Ƶ��ʾ
        ClearVideoDisplay();

        SetConnectionStatus(ConnectionStatus.Disconnected);
    }

    private void ClearVideoDisplay()
    {
        if (VideoStreamObject != null)
        {
            var image = VideoStreamObject.GetComponent<RawImage>();
            if (image != null)
            {
                image.color = Color.clear;
                image.texture = null;
            }
        }
    }

    #endregion

    #region �¼�����

    private void OnParticipantConnected(Participant participant)
    {
        Debug.Log($"Participant connected: {participant.Identity}");
        CheckRobotOnlineStatus();
    }

    private void OnParticipantDisconnected(Participant participant)
    {
        Debug.Log($"Participant disconnected: {participant.Identity}");
        CheckRobotOnlineStatus();
    }

    void TrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        if (track is RemoteVideoTrack videoTrack)
        {
            AddVideoTrack(videoTrack);
        }
    }

    void UnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        // Handle track unsubscribed if needed
    }

    void DataReceived(byte[] data, Participant participant, DataPacketKind kind, string topic)
    {
        var str = System.Text.Encoding.Default.GetString(data);
        Debug.Log("DataReceived: from " + participant.Identity + ", data " + str);
    }

    #endregion

    #region �������

    private bool GetBoolValue(object value)
    {
        return value switch
        {
            JsonElement jsonElement => jsonElement.GetBoolean(),
            bool boolValue => boolValue,
            _ => Convert.ToBoolean(value.ToString())
        };
    }

    void CallTeleopStart()
    {
        if (bridgeManager == null) return;

        bridgeManager.CallService("start_teleop", "std_srvs/srv/Trigger", new Dictionary<string, object>(), response =>
        {
            try
            {
                if (response?.ContainsKey("success") == true)
                {
                    inTeleopMode = GetBoolValue(response["success"]);
                    if (inTeleopMode)
                    {
                        SetConnectionStatus(ConnectionStatus.RobotOnlineTeleop);
                    }
                    Debug.Log($"Teleop mode set to: {inTeleopMode}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"���������Ӧʱ����: {ex.Message}");
            }
        });
    }

    public void onClicGoToPickPose()
    {
        if (currentStatus != ConnectionStatus.RobotOnlineIdle) 
        {
            Debug.LogWarning("Robot is not in idle state, cannot execute pick pose command");
            return;
        }

        bridgeManager.CallService("goto_pick_pose", "std_srvs/srv/Trigger", new Dictionary<string, object>(), response =>
        {
            bool success = response?.ContainsKey("success") == true && GetBoolValue(response["success"]);
            Debug.Log($"goto_pick_pose {(success ? "success" : "failed")}");
        });
    }

    public void onClicGoToTapPose()
    {
        if (currentStatus != ConnectionStatus.RobotOnlineIdle)
        {
            Debug.LogWarning("Robot is not in idle state, cannot execute tap pose command");
            return;
        }

        bridgeManager.CallService("goto_tap_pose", "std_srvs/srv/Trigger", new Dictionary<string, object>(), response =>
        {
            bool success = response?.ContainsKey("success") == true && GetBoolValue(response["success"]);
            Debug.Log($"goto_tap_pose {(success ? "success" : "failed")}");
        });
    }

    #endregion

    #region ���ݷ���

    public void PublishVelocityData()
    {
        if (cmdVelPublisher == null) return;

        float az = -OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).x;
        float vx = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).y;

        var twistMsg = new TwistMessage
        {
            Linear = new Vector3(vx, 0.0f, 0.0f),
            Angular = new Vector3(0.0f, 0.0f, az)
        };
        cmdVelPublisher.Publish(twistMsg);
    }

    public void PublishEEPoseData()
    {
        if (eePosePublisher == null) return;

        (Vector3 position, Quaternion rotation) = getControllerPose();
        Vector3 offsetPosition = position - initialRightControllerPosition;
        Quaternion offsetRotation = rotation * Quaternion.Inverse(initialRightControllerRotation);
        (Vector3 offsetPositionRos, Quaternion offsetRotationRos) = UnityPose2RosPose(offsetPosition, offsetRotation);

        var poseMsg = new PoseStampedMessage
        {
            Header = new HeaderMessage { FrameId = "ee_offset" },
            Pose = new PoseMessage
            {
                Position = offsetPositionRos,
                Orientation = offsetRotationRos
            }
        };

        eePosePublisher.Publish(poseMsg);
    }

    private void PublishTriggerValue()
    {
        if (gripperPublisher == null) return;

        float triggerValue = OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger);
        var gripperMsg = new JointStateMessage
        {
            Name = new List<string> { "main_gripper" },
            Position = new List<double> { triggerValue },
            Velocity = new List<double> { 0.0 },
            Effort = new List<double> { 0.0 }
        };
        gripperPublisher.Publish(gripperMsg);
    }

    #endregion

    #region ��������

    private void ValidateUIReferences()
    {
        if (VideoStreamObject == null) Debug.LogError("VideoStreamObject is not set!");
        if (infoText == null) Debug.LogError("infoText is not set!");
        if (connectStatusText == null) Debug.LogError("connectStatusText is not set!");
        if (buttonStatusText == null) Debug.LogError("buttonStatusText is not set!");
        if (connectStatusIcon == null) Debug.LogError("connectStatusIcon is not set!");
    }

    public void UpdateConnectStatus(string newText)
    {
        if (connectStatusText != null)
            connectStatusText.text = newText;
    }

    public void UpdateInfoText(string newText)
    {
        if (infoText != null)
            infoText.text = newText;
    }

    public void UpdateButtonStatusText(string newText)
    {
        if (buttonStatusText != null)
            buttonStatusText.text = newText;
    }

    void AddVideoTrack(RemoteVideoTrack videoTrack)
    {
        Debug.Log("AddVideoTrack " + videoTrack.Sid);

        var image = VideoStreamObject.GetComponent<RawImage>();
        var stream = new VideoStream(videoTrack);
        stream.TextureReceived += (tex) =>
        {
            if (image != null)
            {
                image.color = Color.white;
                image.texture = tex;
            }
        };

        stream.Start();
        StartCoroutine(stream.Update());
    }

    private (Vector3, Quaternion) getControllerPose()
    {
        Vector3 position = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch);
        Quaternion rotation = OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch);
        return (position, rotation);
    }

    private (Vector3, Quaternion) UnityPose2RosPose(Vector3 unityPos, Quaternion unityRot)
    {
        var (offsetPositionRos, offsetRotationRos) = CoordinateConverter.UnityToROS2.ConvertPose(unityPos, unityRot);
        transform.position = offsetPositionRos;
        transform.rotation = offsetRotationRos;
        transform.RotateAround(Vector3.zero, Vector3.up, 0f);
        return (transform.position, transform.rotation);
    }

    #endregion
}
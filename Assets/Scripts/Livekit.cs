using Draco;
using LiveKit;
using LiveKit.Proto;
using LiveKitROS2Bridge;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using RoomOptions = LiveKit.RoomOptions;

public class Livekit : MonoBehaviour
{
    #region 状态枚举和配置

    /// <summary>
    /// 连接状态枚举
    /// </summary>
    public enum ConnectionStatus
    {
        Disconnected,               // 未连接
        RoomConnectedRobotOffline, // 已连接房间但机器人不在线
        RobotOnlineIdle,           // 机器人在线但未进入遥操
        RobotOnlineTeleop          // 机器人在线正在遥操
    }

    /// <summary>
    /// 状态配置数据结构
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

    #region 公共字段

    [Header("Connection Settings")]
    //public string url = "wss://zwindz1-lam2j1uj.livekit.cloud";
    //public string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjE3NjA3MzI5ODYsImlzcyI6IkFQSXduZkVoTmNUY2ZzQSIsIm5iZiI6MTc1MTczMjk4Niwic3ViIjoiVlIiLCJ2aWRlbyI6eyJjYW5QdWJsaXNoIjp0cnVlLCJjYW5QdWJsaXNoRGF0YSI6dHJ1ZSwiY2FuU3Vic2NyaWJlIjp0cnVlLCJyb29tIjoibXktcm9vbSIsInJvb21Kb2luIjp0cnVlfH0.oGMrOIDNqyilOWw-6FgqpSKBzyIEp7pZG_FkFtPXji8";

    public string url = "wss://livekit.zwind-robot.com";
    public string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjIxMjA2MzA5NDMsImlkZW50aXR5IjoidnIiLCJpc3MiOiJBUElIdEdUYVV2YzlGNHkiLCJuYW1lIjoidnIiLCJuYmYiOjE3NjA2MzQ1NDMsInN1YiI6InZyIiwidmlkZW8iOnsicm9vbSI6InRlc3Rfcm9vbSIsInJvb21Kb2luIjp0cnVlfX0.j6HIfGId5GIVFjY0th28XneEgDiQ0Vixqf63rehnGr4";

    [Header("UI References")]
    public GameObject VideoStreamObject;
    public TMP_Text infoText;
    public TMP_Text connectStatusText;
    public TMP_Text buttonStatusText;
    public Image connectStatusIcon;
    public Material pointCloudMaterial;

    [Header("Status Icons")]
    public Sprite disConnectSprite;
    public Sprite connectSprite;
    public Sprite robotOfflineSprite;
    public Sprite teleopSprite;

    [Header("Point Cloud Settings")]
    [Tooltip("Maximum number of point cloud frames to accumulate")]
    public int pointCloudBufferSize = 30;
    [Tooltip("Color tint applied to point cloud")]
    public Color pointTint = Color.white;

    #endregion

    #region 私有字段

    // 状态管理
    private ConnectionStatus currentStatus = ConnectionStatus.Disconnected;
    private Dictionary<ConnectionStatus, StatusConfig> statusConfigs;

    // LiveKit相关
    private Room room = null;
    private bool inTeleopMode = false;
    private Vector3 initialRightControllerPosition;
    private Quaternion initialRightControllerRotation;

    // single frame pointcloud buffer
    private byte[] pointCloudBuffer = new byte[1024 * 1024]; // 1 MB buffer

    // multi-frame pointcloud assembly
    private Dictionary<int, PointCloudFrame> pointCloudFrames = new Dictionary<int, PointCloudFrame>();
    
    // Point cloud ring buffer for accumulating multiple frames
    private Queue<Mesh> pointCloudRingBuffer = new Queue<Mesh>();
    private Mesh accumulatedPointCloudMesh = null;
    private bool needsRebuild = false;

    /// <summary>
    /// point cloud frame data structure for multi-frame assembly
    /// </summary>
    private class PointCloudFrame
    {
        public int Id { get; set; }
        public int TotalChunks { get; set; }
        public Dictionary<int, byte[]> Chunks { get; set; } = new Dictionary<int, byte[]>();
        public int ReceivedChunks => Chunks.Count;
        public bool IsComplete => ReceivedChunks == TotalChunks;

        public byte[] GetCompleteData()
        {
            if (!IsComplete) return null;

            int totalSize = 0;
            foreach (var chunk in Chunks.Values)
            {
                totalSize += chunk.Length;
            }

            var completeData = new byte[totalSize];
            int offset = 0;

            for (int i = 0; i < TotalChunks; i++)
            {
                if (Chunks.TryGetValue(i, out var chunk))
                {
                    Array.Copy(chunk, 0, completeData, offset, chunk.Length);
                    offset += chunk.Length;
                }
            }
            return completeData;
        }
    }

    // ROS2桥接
    private LiveKitROS2BridgeManager bridgeManager = null;
    private LiveKitROS2Publisher<TwistMessage> cmdVelPublisher = null;
    private LiveKitROS2Publisher<PoseStampedMessage> eePosePublisher = null;
    private LiveKitROS2Publisher<TwistStampedMessage> eeTwistPublisher = null;
    private LiveKitROS2Publisher<JointStateMessage> gripperPublisher = null;

    // 帧率控制
    private float lastPublishTime = 0f;
    private const float TARGET_FPS = 30f;
    private const float PUBLISH_INTERVAL = 1f / TARGET_FPS;

    #endregion

    #region Unity生命周期

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
        
        // Rebuild accumulated mesh if needed
        if (needsRebuild)
        {
            RebuildAccumulatedMesh();
            needsRebuild = false;
        }
    }

    void OnDestroy()
    {
        Disconnect();
        CleanupPointCloudBuffer();
    }

    #endregion

    #region 状态管理

    /// <summary>
    /// 初始化状态配置
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
    /// 设置连接状态
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
    /// 获取当前状态
    /// </summary>
    public ConnectionStatus GetCurrentStatus() => currentStatus;

    /// <summary>
    /// 根据状态更新UI
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
    /// 检查并更新机器人在线状态
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

    #region VR输入处理

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
            //PublishEETwistData();
            PublishTriggerValue();
            lastPublishTime = Time.time;
        }
    }

    #endregion

    #region 连接管理

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
                eeTwistPublisher = bridgeManager.CreatePublisher<TwistStampedMessage>("servo_node/delta_twist_cmds");
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

        // 清理视频显示
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

    #region 事件处理

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
        if (topic == "pointcloud:meta")
        {
            try
            {
                // parse json like {"id": 236, "chunk": 3, "total": 4}
                var jsonString = System.Text.Encoding.Default.GetString(data);
                var meta = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);

                if (meta != null && meta.ContainsKey("id") && meta.ContainsKey("chunk") && meta.ContainsKey("total"))
                {
                    int id = GetIntValue(meta["id"]);
                    int chunk = GetIntValue(meta["chunk"]);
                    int total = GetIntValue(meta["total"]);
                    //Debug.Log($"PointCloud Meta: id={id}, chunk={chunk}, total={total}");

                    if (!pointCloudFrames.ContainsKey(id))
                    {
                        pointCloudFrames[id] = new PointCloudFrame
                        {
                            Id = id,
                            TotalChunks = total,
                            Chunks = new Dictionary<int, byte[]>()
                        };

                        while (pointCloudFrames.Count > 3)
                        {
                            var oldestKey = pointCloudFrames.Keys.Min();
                            pointCloudFrames.Remove(oldestKey);
                        }
                    }
                    // check chunk index validity
                    if (chunk == total - 1 && pointCloudFrames[id].IsComplete)
                    {
                        var completeData = pointCloudFrames[id].GetCompleteData();
                        if (completeData != null && completeData.Length <= pointCloudBuffer.Length)
                        {
                            Array.Copy(completeData, 0, pointCloudBuffer, 0, completeData.Length);
                            //Debug.Log($"Rendering PointCloud id={id} with size={completeData.Length}");
                            RenderPointCloud(completeData, id);
                        }
                        pointCloudFrames.Remove(id);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error parsing pointcloud meta: {ex.Message}");
            }
        }
        else if (topic == "pointcloud")
        {
            if (pointCloudFrames.Count > 0)
            {
                var latestFrame = pointCloudFrames.Values.OrderBy(f => f.Id).Last();
                var nextChunkIndex = latestFrame.ReceivedChunks;

                if (nextChunkIndex < latestFrame.TotalChunks)
                {
                    latestFrame.Chunks[nextChunkIndex] = new byte[data.Length];
                    Array.Copy(data, 0, latestFrame.Chunks[nextChunkIndex], 0, data.Length);

                    //Debug.Log($"Stored chunk {nextChunkIndex}/{latestFrame.TotalChunks} for frame {latestFrame.Id}");

                    // check if frame is complete
                    if (latestFrame.IsComplete)
                    {
                        var completeData = latestFrame.GetCompleteData();
                        if (completeData != null && completeData.Length <= pointCloudBuffer.Length)
                        {
                            Array.Copy(completeData, 0, pointCloudBuffer, 0, completeData.Length);
                            //Debug.Log($"Rendering PointCloud id={latestFrame.Id} with size={completeData.Length}");
                            RenderPointCloud(completeData, latestFrame.Id);
                        }
                        pointCloudFrames.Remove(latestFrame.Id);
                    }
                }
            }
            else
            {
                if (data.Length <= pointCloudBuffer.Length)
                {
                    Array.Copy(data, 0, pointCloudBuffer, 0, data.Length);
                    //Debug.Log($"Direct pointcloud data received with size {data.Length}");
                }
                else
                {
                    Debug.LogWarning("Received pointcloud data chunk is too large for buffer");
                }
            }
        }
    }

    #endregion

    #region 服务调用

    private bool GetBoolValue(object value)
    {
        return value switch
        {
            JsonElement jsonElement => jsonElement.GetBoolean(),
            bool boolValue => boolValue,
            _ => Convert.ToBoolean(value.ToString())
        };
    }

    private int GetIntValue(object value)
    {
        return value switch
        {
            JsonElement jsonElement => jsonElement.GetInt32(),
            int intValue => intValue,
            long longValue => (int)longValue,
            double doubleValue => (int)doubleValue,
            _ => Convert.ToInt32(value.ToString())
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
                Debug.LogError($"处理服务响应时出错: {ex.Message}");
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

    #region 数据发布

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

    /// <summary>
    /// 发布末端执行器的速度数据
    /// </summary>
    public void PublishEETwistData()
    {
        if (eeTwistPublisher == null) return;

        // 直接获取控制器的线性和角速度
        (Vector3 linearVelocity, Vector3 angularVelocity) = getControllerVelocity();

        // 转换到ROS坐标系
        (Vector3 linearVelocityRos, Vector3 angularVelocityRos) = ConvertVelocityToROS(linearVelocity, angularVelocity);

        // 创建TwistStamped消息
        var twistStampedMsg = new TwistStampedMessage
        {
            Header = new HeaderMessage { FrameId = "link_grasp_center" },
            Twist = new TwistMessage
            {
                Linear = linearVelocityRos,
                Angular = angularVelocityRos
            }
        };

        // 发布消息
        eeTwistPublisher.Publish(twistStampedMsg);
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 将Unity坐标系的速度转换为ROS坐标系
    /// </summary>
    private (Vector3, Vector3) ConvertVelocityToROS(Vector3 unityLinearVel, Vector3 unityAngularVel)
    {
        // 使用与位置转换相同的坐标转换逻辑
        // Unity: +X=right, +Y=up, +Z=forward
        // ROS: +X=forward, +Y=left, +Z=up
        Vector3 rosLinearVel = new Vector3(-unityLinearVel.y, -unityLinearVel.x, unityLinearVel.z);
        Vector3 rosAngularVel = new Vector3(-unityAngularVel.y, -unityAngularVel.x, unityAngularVel.z);
        
        return (rosLinearVel, rosAngularVel);
    }

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

    private (Vector3, Vector3) getControllerVelocity()
    {
        Vector3 linear_vel = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);
        Vector3 angular_vel = OVRInput.GetLocalControllerAngularVelocity(OVRInput.Controller.RTouch);
        return (linear_vel, angular_vel);
    }

    private (Vector3, Quaternion) UnityPose2RosPose(Vector3 unityPos, Quaternion unityRot)
    {
        var (offsetPositionRos, offsetRotationRos) = CoordinateConverter.UnityToROS2.ConvertPose(unityPos, unityRot);
        transform.position = offsetPositionRos;
        transform.rotation = offsetRotationRos;
        transform.RotateAround(Vector3.zero, Vector3.up, 0f);
        return (transform.position, transform.rotation);
    }

    /// <summary>
    /// render point cloud from byte array data
    /// </summary>
    private void RenderPointCloud(byte[] pointCloudData, int frameId)
    {
        try
        {
            // start a coroutine to decode and render the point cloud
            StartCoroutine(RenderPointCloudAsync(pointCloudData, frameId));
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error rendering point cloud frame {frameId}: {ex.Message}");
        }
    }

    /// <summary>
    /// asynchronous point cloud decoding and rendering coroutine
    /// </summary>
    private System.Collections.IEnumerator RenderPointCloudAsync(byte[] pointCloudData, int frameId)
    {
        var decodingTask = DracoDecoder.DecodeMesh(pointCloudData);

        // wait for the decoding to complete
        while (!decodingTask.IsCompleted)
        {
            yield return null;
        }

        var mesh = decodingTask.Result;
        if (mesh != null)
        {
            // Configure mesh for point rendering
            mesh.indexFormat = mesh.vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            var indices = Enumerable.Range(0, mesh.vertexCount).ToArray();
            mesh.SetIndices(indices, MeshTopology.Points, 0, false);
            
            // Add to ring buffer
            AddMeshToRingBuffer(mesh);
        }
        else
        {
            Debug.LogWarning($"Failed to decode point cloud frame {frameId}: mesh is null");
        }
    }

    /// <summary>
    /// Add a decoded mesh to the ring buffer
    /// </summary>
    private void AddMeshToRingBuffer(Mesh newMesh)
    {
        // Add new mesh to buffer
        pointCloudRingBuffer.Enqueue(newMesh);
        
        // Remove oldest mesh if buffer is full
        if (pointCloudRingBuffer.Count > pointCloudBufferSize)
        {
            var oldMesh = pointCloudRingBuffer.Dequeue();
            if (oldMesh != null)
            {
                Destroy(oldMesh);
            }
        }
        
        // Mark for rebuild
        needsRebuild = true;
    }

    /// <summary>
    /// Rebuild accumulated mesh from all meshes in ring buffer
    /// </summary>
    private void RebuildAccumulatedMesh()
    {
        if (pointCloudRingBuffer.Count == 0)
        {
            return;
        }

        // Destroy old accumulated mesh
        if (accumulatedPointCloudMesh != null)
        {
            Destroy(accumulatedPointCloudMesh);
        }

        // Create new accumulated mesh
        accumulatedPointCloudMesh = new Mesh();
        accumulatedPointCloudMesh.name = "AccumulatedPointCloud";

        List<Vector3> allVertices = new List<Vector3>();
        List<Color32> allColors = new List<Color32>();
        List<int> allIndices = new List<int>();

        int vertexOffset = 0;
        foreach (var mesh in pointCloudRingBuffer)
        {
            if (mesh == null) continue;

            // Add vertices
            Vector3[] vertices = mesh.vertices;
            allVertices.AddRange(vertices);

            // Add colors (or default white if no colors)
            if (mesh.colors32 != null && mesh.colors32.Length == vertices.Length)
            {
                allColors.AddRange(mesh.colors32);
            }
            else if (mesh.colors != null && mesh.colors.Length == vertices.Length)
            {
                Color32[] colors32 = new Color32[mesh.colors.Length];
                for (int i = 0; i < mesh.colors.Length; i++)
                {
                    colors32[i] = mesh.colors[i];
                }
                allColors.AddRange(colors32);
            }
            else
            {
                // Default to white
                Color32[] defaultColors = new Color32[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    defaultColors[i] = Color.white;
                }
                allColors.AddRange(defaultColors);
            }

            // Add indices with offset
            int[] indices = mesh.GetIndices(0);
            for (int i = 0; i < indices.Length; i++)
            {
                allIndices.Add(indices[i] + vertexOffset);
            }

            vertexOffset += vertices.Length;
        }

        // Set mesh data
        accumulatedPointCloudMesh.indexFormat = allVertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        accumulatedPointCloudMesh.SetVertices(allVertices);
        accumulatedPointCloudMesh.SetColors(allColors);
        accumulatedPointCloudMesh.SetIndices(allIndices.ToArray(), MeshTopology.Points, 0, false);
        accumulatedPointCloudMesh.RecalculateBounds();

        Debug.Log($"Accumulated {pointCloudRingBuffer.Count} frames with total {allVertices.Count} vertices");

        // Update display
        UpdatePointCloudDisplay(accumulatedPointCloudMesh, -1);
    }

    /// <summary>
    /// Update or create the point cloud display object with the given mesh
    /// </summary>
    private void UpdatePointCloudDisplay(Mesh mesh, int frameId)
    {
        GameObject pointCloudObject = GameObject.Find("PointCloudDisplay");

        if (pointCloudObject == null)
        {
            pointCloudObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(pointCloudObject.GetComponent<Collider>());
            pointCloudObject.name = "PointCloudDisplay";
            Material m = Instantiate(pointCloudMaterial);
            MeshRenderer render = pointCloudObject.GetComponent<MeshRenderer>();
            
            // Apply shader properties
            if (m != null)
            {
                if (m.HasProperty("_PointSize")) m.SetFloat("_PointSize", .1f);
                if (m.HasProperty("_Tint")) m.SetColor("_Tint", pointTint);
            }
            
            render.material = m;
            // optional : disable shadow casting and receiving for performance
            render.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            render.receiveShadows = false;
        }

        // assign the mesh to the MeshFilter
        var meshFilter = pointCloudObject.GetComponent<MeshFilter>();
        meshFilter.mesh = mesh;
    }

    /// <summary>
    /// Clean up all meshes in the ring buffer
    /// </summary>
    private void CleanupPointCloudBuffer()
    {
        foreach (var mesh in pointCloudRingBuffer)
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }
        pointCloudRingBuffer.Clear();

        if (accumulatedPointCloudMesh != null)
        {
            Destroy(accumulatedPointCloudMesh);
            accumulatedPointCloudMesh = null;
        }
    }

    #endregion
}
using System;
using System.Collections;
using UnityEngine;
using LiveKit;
using LiveKit.Proto;
using UnityEngine.UI;
using RoomOptions = LiveKit.RoomOptions;
using System.Collections.Generic;
using TMPro;
//using Unity.AppUI.UI;
using System.Text.Json;
using LiveKitROS2Bridge;
public class Livekit : MonoBehaviour
{
    public string url = "wss://zwindz1-lam2j1uj.livekit.cloud";
    public string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjE3NjA3MzI5ODYsImlzcyI6IkFQSXduZkVoTmNUY2ZzQSIsIm5iZiI6MTc1MTczMjk4Niwic3ViIjoiVlIiLCJ2aWRlbyI6eyJjYW5QdWJsaXNoIjp0cnVlLCJjYW5QdWJsaXNoRGF0YSI6dHJ1ZSwiY2FuU3Vic2NyaWJlIjp0cnVlLCJyb29tIjoibXktcm9vbSIsInJvb21Kb2luIjp0cnVlfH0.oGMrOIDNqyilOWw-6FgqpSKBzyIEp7pZG_FkFtPXji8";

    //public string url = "ws://47.111.148.68:7880";
    //public string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjE4MjY5NjU3ODIsImlzcyI6InVuaXgiLCJuYW1lIjoidW5pdHkiLCJuYmYiOjE3NTQ5NjU3ODIsInN1YiI6InVuaXR5IiwidmlkZW8iOnsicm9vbSI6ImRpbmdkYW5nIiwicm9vbUpvaW4iOnRydWV9fQ.CADs5YcN1w5b7JJ8_WA_ruP_7NWIcKVZPkZ8SuwZChM";

    public GameObject VideoStreamObject;

    public TMP_Text infoText;
    public TMP_Text connectStatusText;
    public TMP_Text buttonStatusText;
    public Image connectStatusIcon;
    public Sprite disConnectSprite;
    public Sprite connectSprite;

    private Room room = null;
    private bool inTeleopMode = false;
    private Vector3 initialRightControllerPosition;
    private Quaternion initialRightControllerRotation;

    private LiveKitROS2BridgeManager bridgeManager = null;
    private LiveKitROS2Publisher<TwistMessage> cmdVelPublisher = null;
    private LiveKitROS2Publisher<PoseStampedMessage> eePosePublisher = null;
    private LiveKitROS2Publisher<JointStateMessage> gripperPublisher = null;

    // Frame rate control for teleop mode
    private float lastPublishTime = 0f;
    private const float TARGET_FPS = 30f;
    private const float PUBLISH_INTERVAL = 1f / TARGET_FPS; 

    // Start is called before the first frame update
    void Start()
    {   if (VideoStreamObject == null)
        {
            Debug.LogError("VideoStreamObject is not set!");
        }
        if (infoText == null)
        {
            Debug.LogError("infoText is not set!");
        }
        if (connectStatusText == null)
        {
            Debug.LogError("connectStatusText is not set!");
        }
        if (buttonStatusText == null)
        {
            Debug.LogError("buttonStatusText is not set!");
        }
        if (connectStatusIcon == null)
        {
            Debug.LogError("connectStatusIcon is not set!");
        }
    }

    // Update is called once per frame
    void Update()
    {
        if (room != null)
        {
            if (OVRInput.GetUp(OVRInput.RawButton.B) && !inTeleopMode)
            {                 
                CallTeleopStart();
                (initialRightControllerPosition, initialRightControllerRotation) = getControllerPose();
                Debug.Log("Entering Teleop Mode: initialRightControllerRotation=" + initialRightControllerRotation);
                lastPublishTime = Time.time; // Reset the timer when entering teleop mode
                UpdateInfoText("In Teleop Mode");
            }
            else if (OVRInput.GetUp(OVRInput.RawButton.B) && inTeleopMode)
            {
                //CallTeleopStop();
                inTeleopMode = false;
            }

            if (inTeleopMode)
            {
                // Frame rate control: only publish if enough time has passed
                if (Time.time - lastPublishTime >= PUBLISH_INTERVAL)
                {
                    PublishVelocityData();
                    PublishEEPoseData();
                    PublishTriggerValue();
                    lastPublishTime = Time.time;
                }
            }
        }
    }

    public void UpdateConnectStatus(string newText)
    {
        connectStatusText.text = newText;
        if (newText == "Connected")
            connectStatusIcon.sprite = connectSprite;
        else
            connectStatusIcon.sprite = disConnectSprite;
    }
    public void UpdateInfoText(string newText)
    {
        if (infoText != null)
        {
            infoText.text = newText;
        }
    }
    public void UpdateButtonStatusText(string newText)
    {
        if (buttonStatusText != null)
        {
            buttonStatusText.text = newText;
        }
    }
    public void onClickConnect()
    {
        if (room != null)
        {
            Debug.Log("onClickConnect clicked! room is not null, disconnecting...");
            Disconnect();
            return;
        }
        Debug.Log("Connect to room");
        StartCoroutine(Connect());
    }

    IEnumerator Connect()
    {
        if (room == null)
        {
            room = new Room();
            room.TrackSubscribed += TrackSubscribed;
            room.TrackUnsubscribed += UnTrackSubscribed;
            room.DataReceived += DataReceived;
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
                UpdateConnectStatus("Connected");
                UpdateButtonStatusText("Disonnect Robot");
                UpdateInfoText("Press B to start Teleop");
            }
        }
    }
    void Disconnect()
    {
        // disconnect room
        room.Disconnect();
        room = null;
        // set image panel to black
        RawImage image = VideoStreamObject.GetComponent<RawImage>();
        if (image != null)
        {
            image.color = Color.clear;
            image.texture = null;
        }
        // set inTeleopMode
        inTeleopMode = false;
        // update status text
        UpdateConnectStatus("Disconnected");
        UpdateButtonStatusText("Connect Robot");
        UpdateInfoText("Please Connect Robot First");
    }

    void AddVideoTrack(RemoteVideoTrack videoTrack)
    {
        Debug.Log("AddVideoTrack " + videoTrack.Sid);

        RawImage image = VideoStreamObject.GetComponent<RawImage>();

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

    void TrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
        if (track is RemoteVideoTrack videoTrack)
        {
            AddVideoTrack(videoTrack);
        }
        else if (track is RemoteAudioTrack audioTrack)
        {
        }
    }

    void UnTrackSubscribed(IRemoteTrack track, RemoteTrackPublication publication, RemoteParticipant participant)
    {
    }

    void DataReceived(byte[] data, Participant participant, DataPacketKind kind, string topic)
    {
        var str = System.Text.Encoding.Default.GetString(data);
        Debug.Log("DataReceived: from " + participant.Identity + ", data " + str);
    }

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
        bridgeManager.CallService("start_teleop", "std_srvs/srv/Trigger", new Dictionary<string, object>(), response =>
        {
            try
            {
                if (response?.ContainsKey("success") == true)
                {
                    inTeleopMode = GetBoolValue(response["success"]);
                    Debug.Log($"Teleop mode set to: {inTeleopMode}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"处理服务响应时出错: {ex.Message}");
            }
        });
    }

    public void PublishVelocityData()
    {
        float az = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).x;
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
        (Vector3 position, Quaternion rotation) = getControllerPose();
        // calculate offset
        Vector3 offsetPosition = position - initialRightControllerPosition;
        Quaternion offsetRotation =  rotation * Quaternion.Inverse(initialRightControllerRotation);
        // convert to ROS2 pose
        (Vector3 offsetPositionRos, Quaternion offsetRotationRos) = UnityPose2RosPose(offsetPosition, offsetRotation);

        var poseMsg = new PoseStampedMessage
        {
            Header = new HeaderMessage { FrameId = "ee_offset" },
            Pose = new PoseMessage
            {
                Position = offsetPositionRos,
                Orientation = offsetRotationRos  // Fixed: was using offsetRotation instead of offsetRotationRos
            }
        };

        eePosePublisher.Publish(poseMsg);
    }

    private (Vector3, Quaternion) getControllerPose()
    {
        Vector3 position = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch);
        Quaternion rotation = OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch);
        return (position, rotation);
    }

    private void PublishTriggerValue()
    {
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

    private (Vector3, Quaternion) UnityPose2RosPose(Vector3 unityPos, Quaternion unityRot)
    {
        var (offsetPositionRos, offsetRotationRos) = CoordinateConverter.UnityToROS2.ConvertPose(unityPos, unityRot);
        transform.position = offsetPositionRos;
        transform.rotation = offsetRotationRos;
        transform.RotateAround(Vector3.zero, Vector3.up, 0f); // Rotate around the Y-axis by 90 degrees
        return (transform.position, transform.rotation);
    }


    private void OnApplicationPause(bool pause)
    {
    }

    private void OnDestroy()
    {
    }
}
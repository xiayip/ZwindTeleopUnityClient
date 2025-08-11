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
public class Livekit : MonoBehaviour
{
    public string url = "wss://zwindz1-lam2j1uj.livekit.cloud";
    public string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjE3NjA3MzI5ODYsImlzcyI6IkFQSXduZkVoTmNUY2ZzQSIsIm5iZiI6MTc1MTczMjk4Niwic3ViIjoiVlIiLCJ2aWRlbyI6eyJjYW5QdWJsaXNoIjp0cnVlLCJjYW5QdWJsaXNoRGF0YSI6dHJ1ZSwiY2FuU3Vic2NyaWJlIjp0cnVlLCJyb29tIjoibXktcm9vbSIsInJvb21Kb2luIjp0cnVlfH0.oGMrOIDNqyilOWw-6FgqpSKBzyIEp7pZG_FkFtPXji8";

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

    private bool first_enter = false;
    
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
            if (OVRInput.GetUp(OVRInput.RawButton.B))
            {
                inTeleopMode = !inTeleopMode;
                if (inTeleopMode)
                {
                    (initialRightControllerPosition, initialRightControllerRotation) = getControllerPose();
                    Debug.Log("Entering Teleop Mode: initialRightControllerRotation=" + initialRightControllerRotation);
                    first_enter = true;
                    lastPublishTime = Time.time; // Reset the timer when entering teleop mode
                    UpdateInfoText("In Teleop Mode");
                }
            }
            if (inTeleopMode)
            {
                // Frame rate control: only publish if enough time has passed
                if (Time.time - lastPublishTime >= PUBLISH_INTERVAL)
                {
                    //publishVelocityData();
                    publishEEPoseData();
                    lastPublishTime = Time.time;
                }
            }
            else
            {
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
                UpdateConnectStatus("Connected");
                UpdateButtonStatusText("Disonnect Robot");
                UpdateInfoText("Press B to start Teleop");
            }
        }
    }
    void Disconnect()
    {
        room.Disconnect();
        CleanUp();
        room = null;
        UpdateConnectStatus("Disconnected");
        UpdateButtonStatusText("Connect Robot");
        UpdateInfoText("Please Connect Robot First");
    }

    void CleanUp()
    {
        RawImage image = VideoStreamObject.GetComponent<RawImage>();
        if (image != null)
        {
            image.color = Color.clear;
            image.texture = null;
        }
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

    public void publishVelocityData()
    {
        float az = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).x;
        float vx = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).y;
        // add to json
        var velocityData = new
        {
            linear = new
            {
                x = vx,
                y = 0.0f,
                z = 0.0f
            },
            angular = new
            {
                x = 0.0f,
                y = 0.0f,
                z = az
            },
            timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        string str = JsonSerializer.Serialize(velocityData);
        //Debug.Log("publishVelocityData: vx=" + vx + ", az=" + az);
        room.LocalParticipant.PublishData(System.Text.Encoding.Default.GetBytes(str), reliable: false, topic: "velocity");
    }

    public void publishEEPoseData()
    {
        (Vector3 position, Quaternion rotation) = getControllerPose();
        // calculate offset
        Vector3 offsetPosition = position - initialRightControllerPosition;
        Quaternion offsetRotation =  rotation * Quaternion.Inverse(initialRightControllerRotation);
        // convert to ROS2 pose
        (Vector3 offsetPositionRos, Quaternion offsetRotationRos) = UnityPose2RosPose(offsetPosition, offsetRotation);

        var eePoseData = new {
            p = new float[] { offsetPositionRos.x, offsetPositionRos.y, offsetPositionRos.z },
            q = new float[] { offsetRotationRos.x, offsetRotationRos.y, offsetRotationRos.z, offsetRotationRos.w },
            first_enter = first_enter ? 1 : 0,
            timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        string str = JsonSerializer.Serialize(eePoseData);
        // debug: convert qtion to rpy to check if it is correct
        //Debug.Log($"publishEEPoseData: position={offsetPositionRos}, rotation={offsetRotationRos.eulerAngles}, first_enter={first_enter}");
        room.LocalParticipant.PublishData(System.Text.Encoding.Default.GetBytes(str), reliable: false, topic: "ee_pose");
        
        // Reset first_enter flag after first publish
        if (first_enter)
        {
            first_enter = false;
        }
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
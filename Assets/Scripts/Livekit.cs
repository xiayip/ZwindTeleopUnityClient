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
                    initialRightControllerPosition = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch);
                    initialRightControllerRotation = OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch);
                    UpdateInfoText("In Teleop Mode");
                }
            }
            if (inTeleopMode)
            {
                publishVelocityData();
                publishEEPoseData();
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
        float vx = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).x;
        float az = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).y;
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
        room.LocalParticipant.PublishData(System.Text.Encoding.Default.GetBytes(str), topic: "velocity");
    }

    public void publishEEPoseData()
    {
        Vector3 position = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch);
        Quaternion rotation = OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch);

        Vector3 offsetPosition = position - initialRightControllerPosition;
        Quaternion offsetRotation = Quaternion.Inverse(initialRightControllerRotation) * rotation;

        var eePoseData = new
        {
            position = new
            {
                x = offsetPosition.x,
                y = offsetPosition.y,
                z = offsetPosition.z
            },
            rotation = new
            {
                x = offsetRotation.x,
                y = offsetRotation.y,
                z = offsetRotation.z,
                w = offsetRotation.w
            },
            timestamp = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        string str = JsonSerializer.Serialize(eePoseData);
        //Debug.Log("publishEEPoseData: offset position=" + offsetPosition + ", offset rotation=" + offsetRotation);
        room.LocalParticipant.PublishData(System.Text.Encoding.Default.GetBytes(str), topic: "ee_pose");
    }

    private void OnApplicationPause(bool pause)
    {
    }

    private void OnDestroy()
    {
    }
}
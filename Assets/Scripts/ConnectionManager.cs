using LiveKit;
using LiveKit.Proto;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using RoomOptions = LiveKit.RoomOptions;

/// <summary>
/// Connection status enumeration
/// </summary>
public enum ConnectionStatus
{
    Disconnected,               // Not connected
    RoomConnectedRobotOffline, // Connected to room but robot is offline
    RobotOnlineIdle,           // Robot is online but not in teleop mode
    RobotOnlineTeleop          // Robot is online and in teleop mode
}

/// <summary>
/// Status configuration data structure
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

/// <summary>
/// LiveKit connection manager
/// </summary>
public class ConnectionManager : MonoBehaviour
{
    [Header("Connection Settings")]
    public string url = "wss://livekit.zwind-robot.com";
    public string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJleHAiOjIxMjA2MzA5NDMsImlkZW50aXR5IjoidnIiLCJpc3MiOiJBUElIdEdUYVV2YzlGNHkiLCJuYW1lIjoidnIiLCJuYmYiOjE3NjA2MzQ1NDMsInN1YiI6InZyIiwidmlkZW8iOnsicm9vbSI6InRlc3Rfcm9vbSIsInJvb21Kb2luIjp0cnVlfX0.j6HIfGId5GIVFjY0th28XneEgDiQ0Vixqf63rehnGr4";

    // State management
    private ConnectionStatus currentStatus = ConnectionStatus.Disconnected;
    private Dictionary<ConnectionStatus, StatusConfig> statusConfigs;

    // LiveKit related
    private Room room = null;

    // Events
    public event Action<ConnectionStatus> OnStatusChanged;
    public event Action<IRemoteTrack, RemoteTrackPublication, RemoteParticipant> OnTrackSubscribed;
    public event Action<IRemoteTrack, RemoteTrackPublication, RemoteParticipant> OnTrackUnsubscribed;
    public event Action<byte[], Participant, DataPacketKind, string> OnDataReceived;
    public event Action<Participant> OnParticipantConnected;
    public event Action<Participant> OnParticipantDisconnected;

    public Room Room => room;
    public bool IsConnected => room != null && room.IsConnected;
    public ConnectionStatus CurrentStatus => currentStatus;

    void Start()
    {
        InitializeStatusConfigs();
    }

    /// <summary>
    /// Initialize status configurations
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
                statusColor = Color.red
            },
            [ConnectionStatus.RoomConnectedRobotOffline] = new StatusConfig
            {
                statusText = "Robot Offline",
                buttonText = "Refresh Connection",
                infoText = "Robot is not online, please check robot status",
                statusColor = Color.yellow
            },
            [ConnectionStatus.RobotOnlineIdle] = new StatusConfig
            {
                statusText = "Robot Ready",
                buttonText = "Disconnect Robot",
                infoText = "Press B to start Teleop",
                statusColor = Color.green
            },
            [ConnectionStatus.RobotOnlineTeleop] = new StatusConfig
            {
                statusText = "Teleoperating",
                buttonText = "Disconnect Robot",
                infoText = "Teleop Mode Active - Press B to stop",
                statusColor = Color.cyan
            }
        };
    }

    /// <summary>
    /// Set connection status
    /// </summary>
    public void SetConnectionStatus(ConnectionStatus newStatus)
    {
        if (currentStatus != newStatus)
        {
            Debug.Log($"Status changed: {currentStatus} -> {newStatus}");
            currentStatus = newStatus;
            OnStatusChanged?.Invoke(newStatus);
        }
    }

    /// <summary>
    /// Get status configuration
    /// </summary>
    public StatusConfig GetStatusConfig(ConnectionStatus status)
    {
        return statusConfigs.TryGetValue(status, out var config) ? config : null;
    }

    /// <summary>
    /// Connect to LiveKit room
    /// </summary>
    public IEnumerator ConnectAsync()
    {
        if (room == null)
        {
            room = new Room();
            room.TrackSubscribed += (track, pub, participant) => OnTrackSubscribed?.Invoke(track, pub, participant);
            room.TrackUnsubscribed += (track, pub, participant) => OnTrackUnsubscribed?.Invoke(track, pub, participant);
            room.DataReceived += (data, participant, kind, topic) => OnDataReceived?.Invoke(data, participant, kind, topic);
            room.ParticipantConnected += (participant) => OnParticipantConnected?.Invoke(participant);
            room.ParticipantDisconnected += (participant) => OnParticipantDisconnected?.Invoke(participant);

            var options = new RoomOptions();
            var connect = room.Connect(url, token, options);
            yield return connect;

            if (!connect.IsError)
            {
                Debug.Log("Connected to " + room.Name);
                CheckRobotOnlineStatus();
            }
            else
            {
                Debug.LogError("Failed to connect to room: " + connect.IsError);
                SetConnectionStatus(ConnectionStatus.Disconnected);
            }
        }
    }

    /// <summary>
    /// Disconnect from room
    /// </summary>
    public void Disconnect()
    {
        if (room != null)
        {
            room.ParticipantConnected -= (participant) => OnParticipantConnected?.Invoke(participant);
            room.ParticipantDisconnected -= (participant) => OnParticipantDisconnected?.Invoke(participant);
            room.Disconnect();
            room = null;
        }

        SetConnectionStatus(ConnectionStatus.Disconnected);
    }

    /// <summary>
    /// Check and update robot online status
    /// </summary>
    public void CheckRobotOnlineStatus()
    {
        bool robotOnline = room != null && room.RemoteParticipants.Count > 0;

        if (robotOnline)
        {
            // Status will be set by external controller based on teleop mode
            if (currentStatus == ConnectionStatus.Disconnected || 
                currentStatus == ConnectionStatus.RoomConnectedRobotOffline)
            {
                SetConnectionStatus(ConnectionStatus.RobotOnlineIdle);
            }
        }
        else
        {
            if (room != null && room.IsConnected)
                SetConnectionStatus(ConnectionStatus.RoomConnectedRobotOffline);
            else
                SetConnectionStatus(ConnectionStatus.Disconnected);
        }
    }

    void OnDestroy()
    {
        Disconnect();
    }
}

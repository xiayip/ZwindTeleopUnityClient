using LiveKitROS2Bridge;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ROS2 data publisher
/// </summary>
public class ROS2DataPublisher : MonoBehaviour
{
    private LiveKitROS2BridgeManager bridgeManager = null;
    private LiveKitROS2Publisher<TwistMessage> cmdVelPublisher = null;
    private LiveKitROS2Publisher<PoseStampedMessage> eePosePublisher = null;
    private LiveKitROS2Publisher<TwistStampedMessage> eeTwistPublisher = null;
    private LiveKitROS2Publisher<JointStateMessage> gripperPublisher = null;

    // Frame rate control
    private float lastPublishTime = 0f;
    private const float TARGET_FPS = 30f;
    private const float PUBLISH_INTERVAL = 1f / TARGET_FPS;

    private VRInputManager vrInputManager;

    public bool IsInitialized => bridgeManager != null;

    void Start()
    {
        vrInputManager = FindFirstObjectByType<VRInputManager>();
        if (vrInputManager == null)
        {
            Debug.LogError("VRInputManager not found!");
        }
    }

    /// <summary>
    /// Initialize ROS2 bridge
    /// </summary>
    public void Initialize(LiveKit.Room room)
    {
        if (room == null)
        {
            Debug.LogError("Cannot initialize ROS2 publisher with null room");
            return;
        }

        bridgeManager = new LiveKitROS2BridgeManager(room);
        cmdVelPublisher = bridgeManager.CreatePublisher<TwistMessage>("cmd_vel");
        eePosePublisher = bridgeManager.CreatePublisher<PoseStampedMessage>("ee_offset_pose");
        eeTwistPublisher = bridgeManager.CreatePublisher<TwistStampedMessage>("servo_node/delta_twist_cmds");
        gripperPublisher = bridgeManager.CreatePublisher<JointStateMessage>("main_gripper/gripper_command");

        Debug.Log("ROS2 publishers initialized");
    }

    /// <summary>
    /// Cleanup resources
    /// </summary>
    public void Cleanup()
    {
        bridgeManager?.Dispose();
        bridgeManager = null;
        cmdVelPublisher = null;
        eePosePublisher = null;
        eeTwistPublisher = null;
        gripperPublisher = null;
    }

    /// <summary>
    /// Publish all teleop data
    /// </summary>
    public void PublishTeleopData()
    {
        if (!IsInitialized || vrInputManager == null) return;

        if (Time.time - lastPublishTime >= PUBLISH_INTERVAL)
        {
            PublishVelocityData();
            PublishEEPoseData();
            PublishTriggerValue();
            lastPublishTime = Time.time;
        }
    }

    /// <summary>
    /// Publish velocity data
    /// </summary>
    public void PublishVelocityData()
    {
        if (cmdVelPublisher == null || vrInputManager == null) return;

        (float vx, float az) = vrInputManager.GetThumbstickInput();

        var twistMsg = new TwistMessage
        {
            Linear = new Vector3(vx, 0.0f, 0.0f),
            Angular = new Vector3(0.0f, 0.0f, az)
        };
        cmdVelPublisher.Publish(twistMsg);
    }

    /// <summary>
    /// Publish end-effector pose data
    /// </summary>
    public void PublishEEPoseData()
    {
        if (eePosePublisher == null || vrInputManager == null) return;

        (Vector3 offsetPosition, Quaternion offsetRotation) = vrInputManager.GetControllerOffset();
        (Vector3 offsetPositionRos, Quaternion offsetRotationRos) = vrInputManager.UnityPoseToROSPose(offsetPosition, offsetRotation);

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

    /// <summary>
    /// Publish end-effector velocity data
    /// </summary>
    public void PublishEETwistData()
    {
        if (eeTwistPublisher == null || vrInputManager == null) return;

        (Vector3 linearVelocity, Vector3 angularVelocity) = vrInputManager.GetControllerVelocity();
        (Vector3 linearVelocityRos, Vector3 angularVelocityRos) = vrInputManager.ConvertVelocityToROS(linearVelocity, angularVelocity);

        var twistStampedMsg = new TwistStampedMessage
        {
            Header = new HeaderMessage { FrameId = "link_grasp_center" },
            Twist = new TwistMessage
            {
                Linear = linearVelocityRos,
                Angular = angularVelocityRos
            }
        };

        eeTwistPublisher.Publish(twistStampedMsg);
    }

    /// <summary>
    /// Publish gripper trigger value
    /// </summary>
    private void PublishTriggerValue()
    {
        if (gripperPublisher == null || vrInputManager == null) return;

        float triggerValue = vrInputManager.GetTriggerValue();
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
    /// Call ROS service to start teleop
    /// </summary>
    public void CallTeleopStart(Action<bool> onComplete = null)
    {
        if (bridgeManager == null) return;

        bridgeManager.CallService("start_teleop", "std_srvs/srv/Trigger", new Dictionary<string, object>(), response =>
        {
            try
            {
                bool success = false;
                if (response?.ContainsKey("success") == true)
                {
                    success = GetBoolValue(response["success"]);
                    Debug.Log($"Teleop start service response: {success}");
                }
                onComplete?.Invoke(success);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing service response: {ex.Message}");
                onComplete?.Invoke(false);
            }
        });
    }

    /// <summary>
    /// Call go to pick pose service
    /// </summary>
    public void CallGoToPickPose()
    {
        if (bridgeManager == null) return;

        bridgeManager.CallService("goto_pick_pose", "std_srvs/srv/Trigger", new Dictionary<string, object>(), response =>
        {
            bool success = response?.ContainsKey("success") == true && GetBoolValue(response["success"]);
            Debug.Log($"goto_pick_pose {(success ? "success" : "failed")}");
        });
    }

    /// <summary>
    /// Call go to tap pose service
    /// </summary>
    public void CallGoToTapPose()
    {
        if (bridgeManager == null) return;

        bridgeManager.CallService("goto_tap_pose", "std_srvs/srv/Trigger", new Dictionary<string, object>(), response =>
        {
            bool success = response?.ContainsKey("success") == true && GetBoolValue(response["success"]);
            Debug.Log($"goto_tap_pose {(success ? "success" : "failed")}");
        });
    }

    /// <summary>
    /// Helper method: Get boolean value
    /// </summary>
    private bool GetBoolValue(object value)
    {
        return value switch
        {
            System.Text.Json.JsonElement jsonElement => jsonElement.GetBoolean(),
            bool boolValue => boolValue,
            _ => Convert.ToBoolean(value.ToString())
        };
    }

    void OnDestroy()
    {
        Cleanup();
    }
}

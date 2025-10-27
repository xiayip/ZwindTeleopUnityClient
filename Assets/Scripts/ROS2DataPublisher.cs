using LiveKitROS2Bridge;
using System;
using System.Collections.Generic;
using System.Text.Json;
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
        eePosePublisher = bridgeManager.CreatePublisher<PoseStampedMessage>("servo_node/pose_target_cmds");
        eeTwistPublisher = bridgeManager.CreatePublisher<TwistStampedMessage>("servo_node/delta_twist_cmds");
        gripperPublisher = bridgeManager.CreatePublisher<JointStateMessage>("main_gripper/gripper_command");
        bridgeManager.SubscribeTopic("/pose", pose_callback);
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
    /// Publish movebase control data
    /// </summary>
    public void PublishMovebaseControlData()
    {
        if (!IsInitialized || vrInputManager == null) return;

        if (Time.time - lastPublishTime >= PUBLISH_INTERVAL)
        {
            PublishVelocityData();
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
    /// Execute BehaviorTree action
    /// </summary>
    private string ExecuteBehaviorTree(string treeName, string actionDescription, Action<bool> onComplete = null)
    {
        if (bridgeManager == null)
        {
            Debug.LogWarning($"{actionDescription} failed: bridge manager not initialized");
            onComplete?.Invoke(false);
            return null;
        }

        var goal = new Dictionary<string, object>
        {
            ["target_tree"] = treeName
        };

        string goalId = bridgeManager.SendActionGoal(
            actionName: "/behavior_server",
            actionType: "btcpp_ros2_interfaces/action/ExecuteTree",
            goal: goal,
            onGoalResponse: (response) =>
            {
                bool accepted = response?.ContainsKey("accepted") == true && Convert.ToBoolean(response["accepted"]);
                Debug.Log($"{actionDescription} goal {(accepted ? "ACCEPTED" : "REJECTED")}");
                if (!accepted)
                {
                    onComplete?.Invoke(false);
                }
            },
            onFeedback: (feedback) =>
            {
                Debug.Log($"{actionDescription} executing...");
            },
            onResult: (result) =>
            {
                // Handle status field - can be either string ("succeeded", "aborted", etc.) or int (4, 5, 6)
                bool success = false;
                string statusStr = "unknown";
                statusStr = result["status"].ToString().ToLower();
                success = statusStr == "succeeded" || statusStr == "success";
                Debug.Log($"{actionDescription} {(success ? "SUCCESS" : "FAILED")} (status: {statusStr})");
                onComplete?.Invoke(success);
            }
        );

        Debug.Log($"{actionDescription} action goal sent (ID: {goalId})");
        return goalId;
    }
    
    private void pose_callback(Dictionary<string, object> message)
    {
        try
        {
            // Fast path: Direct JSON deserialization if it's a JsonElement
            if (message.TryGetValue("data", out var dataObj) && dataObj is JsonElement dataElement)
            {
                // Use JsonDocument for efficient parsing
                using var doc = JsonDocument.Parse(dataElement.GetRawText());
                var root = doc.RootElement;
                // Extract position - direct property access
                if (root.TryGetProperty("position", out var pos))
                {
                    float x = pos.TryGetProperty("x", out var xProp) ? xProp.GetSingle() : 0f;
                    float y = pos.TryGetProperty("y", out var yProp) ? yProp.GetSingle() : 0f;
                    float z = pos.TryGetProperty("z", out var zProp) ? zProp.GetSingle() : 0f;
                    // Extract orientation
                    if (root.TryGetProperty("orientation", out var orient))
                    {
                        float qx = orient.TryGetProperty("x", out var qxProp) ? qxProp.GetSingle() : 0f;
                        float qy = orient.TryGetProperty("y", out var qyProp) ? qyProp.GetSingle() : 0f;
                        float qz = orient.TryGetProperty("z", out var qzProp) ? qzProp.GetSingle() : 0f;
                        float qw = orient.TryGetProperty("w", out var qwProp) ? qwProp.GetSingle() : 1f;
                        
                        Debug.Log($"Pose: Pos({x:F3}, {y:F3}, {z:F3}), Quat({qx:F3}, {qy:F3}, {qz:F3}, {qw:F3})");
                    }
                }
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error parsing pose: {ex.Message}");
        }
    }

    /// <summary>
    /// Call ROS service to start teleop
    /// </summary>
    public void CallTeleopStart(Action<bool> onComplete = null)
    {
        ExecuteBehaviorTree("EnterTeleopModeBT", "teleop_start", onComplete);
    }

    /// <summary>
    /// Call ROS service to stop teleop
    /// </summary>
    public void CallTeleopStop(Action<bool> onComplete = null)
    {
        ExecuteBehaviorTree("ExitTeleopModeBT", "teleop_stop", onComplete);
    }

    /// <summary>
    /// Call ROS service to start movebase control
    /// </summary>
    public void CallMovebaseModeStart(Action<bool> onComplete = null)
    {
        ExecuteBehaviorTree("EnterMovebaseModeBT", "movebase_control_start", onComplete);
    }

    /// <summary>
    /// Call ROS service to stop movebase control
    /// </summary>
    public void CallMovebaseModeStop(Action<bool> onComplete = null)
    {
        ExecuteBehaviorTree("ExitMovebaseModeBT", "movebase_control_stop", onComplete);
    }

    /// <summary>
    /// Call go to pick pose action
    /// </summary>
    public void CallGoToPickPose()
    {
        ExecuteBehaviorTree("ArmReadyPickPoseBT", "goto_pick_pose");
    }

    /// <summary>
    /// Call go to tap pose action
    /// </summary>
    public void CallGoToTapPose()
    {
        ExecuteBehaviorTree("ArmReadyTapPoseBT", "goto_tap_pose");
    }

    /// <summary>
    /// Call go to sleep pose action
    /// </summary>
    public void CallGoToSleepPose()
    {
        ExecuteBehaviorTree("ArmSleepPoseBT", "goto_sleep_pose");
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

using UnityEngine;
using System;

/// <summary>
/// VR input manager
/// </summary>
public class VRInputManager : MonoBehaviour
{
    // VR controller state
    private Vector3 initialRightControllerPosition;
    private Quaternion initialRightControllerRotation;
    private bool isInitialized = false;

    // Events
    public event Action OnTeleopStartRequested;
    public event Action OnTeleopStopRequested;

    void Update()
    {
        HandleVRInput();
    }

    /// <summary>
    /// Handle VR input
    /// </summary>
    private void HandleVRInput()
    {
        if (OVRInput.GetUp(OVRInput.RawButton.B))
        {
            var connectionManager = FindFirstObjectByType<ConnectionManager>();
            if (connectionManager != null)
            {
                switch (connectionManager.CurrentStatus)
                {
                    case ConnectionStatus.RobotOnlineIdle:
                        OnTeleopStartRequested?.Invoke();
                        break;
                    case ConnectionStatus.RobotOnlineTeleop:
                        OnTeleopStopRequested?.Invoke();
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Initialize controller pose
    /// </summary>
    public void InitializeControllerPose()
    {
        (initialRightControllerPosition, initialRightControllerRotation) = GetControllerPose();
        isInitialized = true;
        Debug.Log("Controller pose initialized");
    }

    /// <summary>
    /// Get controller pose
    /// </summary>
    public (Vector3, Quaternion) GetControllerPose()
    {
        Vector3 position = OVRInput.GetLocalControllerPosition(OVRInput.Controller.RTouch);
        Quaternion rotation = OVRInput.GetLocalControllerRotation(OVRInput.Controller.RTouch);
        return (position, rotation);
    }

    /// <summary>
    /// Get controller velocity
    /// </summary>
    public (Vector3, Vector3) GetControllerVelocity()
    {
        Vector3 linearVel = OVRInput.GetLocalControllerVelocity(OVRInput.Controller.RTouch);
        Vector3 angularVel = OVRInput.GetLocalControllerAngularVelocity(OVRInput.Controller.RTouch);
        return (linearVel, angularVel);
    }

    /// <summary>
    /// Get controller offset relative to initial position
    /// </summary>
    public (Vector3, Quaternion) GetControllerOffset()
    {
        if (!isInitialized)
        {
            Debug.LogWarning("Controller not initialized, returning zero offset");
            return (Vector3.zero, Quaternion.identity);
        }

        (Vector3 position, Quaternion rotation) = GetControllerPose();
        Vector3 offsetPosition = position - initialRightControllerPosition;
        Quaternion offsetRotation = rotation * Quaternion.Inverse(initialRightControllerRotation);
        return (offsetPosition, offsetRotation);
    }

    /// <summary>
    /// Get thumbstick input
    /// </summary>
    public (float, float) GetThumbstickInput()
    {
        Vector2 thumbstick = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick);
        float vx = thumbstick.y;  // Forward/backward
        float az = -thumbstick.x; // Left/right rotation
        return (vx, az);
    }

    /// <summary>
    /// Get trigger value
    /// </summary>
    public float GetTriggerValue()
    {
        return OVRInput.Get(OVRInput.RawAxis1D.RIndexTrigger);
    }

    /// <summary>
    /// Convert Unity pose to ROS coordinate system
    /// </summary>
    public (Vector3, Quaternion) UnityPoseToROSPose(Vector3 unityPos, Quaternion unityRot)
    {
        var (offsetPositionRos, offsetRotationRos) = CoordinateConverter.UnityToROS2.ConvertPose(unityPos, unityRot);
        transform.position = offsetPositionRos;
        transform.rotation = offsetRotationRos;
        transform.RotateAround(Vector3.zero, Vector3.up, 0f);
        return (transform.position, transform.rotation);
    }

    /// <summary>
    /// Convert Unity velocity to ROS coordinate system
    /// </summary>
    public (Vector3, Vector3) ConvertVelocityToROS(Vector3 unityLinearVel, Vector3 unityAngularVel)
    {
        // Unity: +X=right, +Y=up, +Z=forward
        // ROS: +X=forward, +Y=left, +Z=up
        Vector3 rosLinearVel = new Vector3(-unityLinearVel.y, -unityLinearVel.x, unityLinearVel.z);
        Vector3 rosAngularVel = new Vector3(-unityAngularVel.y, -unityAngularVel.x, unityAngularVel.z);
        
        return (rosLinearVel, rosAngularVel);
    }
}

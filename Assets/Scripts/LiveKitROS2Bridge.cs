using LiveKit;
using LiveKit.Proto;
using System;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using static LiveKit.Proto.DataStream.Types;

namespace LiveKitROS2Bridge
{
    /// <summary>
    /// ROS2 message base class
    /// </summary>
    public abstract class ROS2Message
    {
        public string MessageType { get; protected set; }
        public double Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        public abstract Dictionary<string, object> ToDict();
    }

    /// <summary>
    /// Twist message (geometry_msgs/Twist)
    /// </summary>
    public class TwistMessage : ROS2Message
    {
        public Vector3 Linear { get; set; } = new Vector3();
        public Vector3 Angular { get; set; } = new Vector3();

        public TwistMessage()
        {
            MessageType = "geometry_msgs/msg/Twist";
        }

        public override Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                ["linear"] = new Dictionary<string, object>
                {
                    ["x"] = Linear.x,
                    ["y"] = Linear.y,
                    ["z"] = Linear.z
                },
                ["angular"] = new Dictionary<string, object>
                {
                    ["x"] = Angular.x,
                    ["y"] = Angular.y,
                    ["z"] = Angular.z
                }
            };
        }
    }

    public class TwistStampedMessage : ROS2Message
    {
        public HeaderMessage Header { get; set; } = new HeaderMessage();
        public TwistMessage Twist { get; set; } = new TwistMessage();
        public TwistStampedMessage()
        {
            MessageType = "geometry_msgs/msg/TwistStamped";
        }
        public override Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                ["header"] = Header.ToDict(),
                ["twist"] = Twist.ToDict()
            };
        }
    }

    /// <summary>
    /// String message (std_msgs/String)
    /// </summary>
    public class StringMessage : ROS2Message
    {
        public string Data { get; set; } = "";

        public StringMessage()
        {
            MessageType = "std_msgs/msg/String";
        }

        public StringMessage(string data) : this()
        {
            Data = data;
        }

        public override Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                ["data"] = Data
            };
        }
    }

    /// <summary>
    /// PoseStamped message (geometry_msgs/PoseStamped)
    /// </summary>
    public class PoseStampedMessage : ROS2Message
    {
        public HeaderMessage Header { get; set; } = new HeaderMessage();
        public PoseMessage Pose { get; set; } = new PoseMessage();

        public PoseStampedMessage()
        {
            MessageType = "geometry_msgs/msg/PoseStamped";
        }

        public override Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                ["header"] = Header.ToDict(),
                ["pose"] = Pose.ToDict()
            };
        }
    }

    public class Float64Message: ROS2Message
    {
        public double Data { get; set; }
        public Float64Message()
        {
            MessageType = "std_msgs/msg/Float64";
        }

        public override Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                ["data"] = Data
            };
        }
    }

    /// <summary>
    /// Custom message support
    /// </summary>
    public class CustomMessage : ROS2Message
    {
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();

        public CustomMessage(string messageType)
        {
            MessageType = messageType;
        }

        public override Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>(Data);
        }
    }

    public class HeaderMessage
    {
        public string FrameId { get; set; } = "";
        public double Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                ["frame_id"] = FrameId,
                ["stamp"] = new Dictionary<string, object>
                {
                    ["sec"] = (int)Timestamp,
                    ["nanosec"] = (int)((Timestamp - (int)Timestamp) * 1e9)
                }
            };
        }
    }

    public class PoseMessage
    {
        public Vector3 Position { get; set; } = new Vector3();
        public Quaternion Orientation { get; set; } = new Quaternion();

        public Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                ["position"] = new Dictionary<string, object>
                {
                    ["x"] = Position.x,
                    ["y"] = Position.y,
                    ["z"] = Position.z
                },
                ["orientation"] = new Dictionary<string, object>
                {
                    ["x"] = Orientation.x,
                    ["y"] = Orientation.y,
                    ["z"] = Orientation.z,
                    ["w"] = Orientation.w
                }
            };
        }
    }

    public class JointStateMessage : ROS2Message
    {
        public List<string> Name { get; set; } = new List<string>();
        public List<double> Position { get; set; } = new List<double>();
        public List<double> Velocity { get; set; } = new List<double>();
        public List<double> Effort { get; set; } = new List<double>();
        public JointStateMessage()
        {
            MessageType = "sensor_msgs/msg/JointState";
        }
        public override Dictionary<string, object> ToDict()
        {
            return new Dictionary<string, object>
            {
                ["name"] = Name,
                ["position"] = Position,
                ["velocity"] = Velocity,
                ["effort"] = Effort
            };
        }
    }

    /// <summary>
    /// LiveKit ROS2 Publisher
    /// Provides an interface for ROS2 Publisher to transmit LiveKit Data Packets
    /// </summary>
    public class LiveKitROS2Publisher<T> where T : ROS2Message
    {
        private readonly Room _room;
        private readonly string _topicName;
        private readonly string _messageType;
        private readonly int _queueSize;
        private readonly JsonSerializerOptions _jsonOptions;

        public string TopicName => _topicName;
        public string MessageType => _messageType;
        public bool IsConnected => _room?.ConnectionState == ConnectionState.ConnConnected;

        public LiveKitROS2Publisher(Room room, string topicName, string messageType, int queueSize = 10)
        {
            _room = room ?? throw new ArgumentNullException(nameof(room));
            _topicName = topicName ?? throw new ArgumentNullException(nameof(topicName));
            _messageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
            _queueSize = queueSize;

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
        }

        /// <summary>
        /// Publish ROS2 message
        /// </summary>
        public void Publish(T message)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("LiveKit room is not connected");
            }

            try
            {
                // Create ROS2 message packet
                var ros2Packet = new ROS2DataPacket
                {
                    PacketType = "ros2_message",
                    TopicName = _topicName,
                    MessageType = _messageType,
                    Data = message.ToDict(),
                    Timestamp = message.Timestamp,
                    SequenceId = GenerateSequenceId()
                };

                // Serialize to JSON
                var jsonData = JsonSerializer.Serialize(ros2Packet, _jsonOptions);
                var dataBytes = System.Text.Encoding.Default.GetBytes(jsonData);

                // Publish data via LiveKit (same as before)
                _room.LocalParticipant.PublishData(dataBytes, reliable: true);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to publish message to topic '{_topicName}': {ex.Message}", ex);
            }
        }

        private static long _sequenceCounter = 0;
        private long GenerateSequenceId()
        {
            return System.Threading.Interlocked.Increment(ref _sequenceCounter);
        }
    }

    /// <summary>
    /// ROS2 data packet format
    /// </summary>
    public class ROS2DataPacket
    {
        public string PacketType { get; set; } = "ros2_message";
        public string TopicName { get; set; } = "";
        public string MessageType { get; set; } = "";
        public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
        public double Timestamp { get; set; }
        public long SequenceId { get; set; }
    }

    /// <summary>
    /// LiveKit ROS2 bridge manager
    /// </summary>
    public class LiveKitROS2BridgeManager
    {
        private readonly Room _room;
        private readonly Dictionary<string, object> _publishers;
        private readonly JsonSerializerOptions _jsonOptions;

        private readonly Dictionary<string, Action<Dictionary<string, object>>> _serviceResponseCallbacks;
        private readonly Dictionary<string, ROS2ActionCallbacks> _actionCallbacks;
        public Room Room => _room;
        public bool IsConnected => _room?.ConnectionState == ConnectionState.ConnConnected;

        public LiveKitROS2BridgeManager(Room room)
        {
            _room = room ?? throw new ArgumentNullException(nameof(room));
            _publishers = new Dictionary<string, object>();
            _serviceResponseCallbacks = new Dictionary<string, Action<Dictionary<string, object>>>();
            _actionCallbacks = new Dictionary<string, ROS2ActionCallbacks>();

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };

            // Subscribe to data received event
            _room.DataReceived += OnDataReceived;
        }

        /// <summary>
        /// Create a ROS2 message publisher
        /// </summary>
        public LiveKitROS2Publisher<T> CreatePublisher<T>(string topicName, int queueSize = 10) where T : ROS2Message, new()
        {
            var sampleMessage = new T();
            var messageType = sampleMessage.MessageType;

            var publisher = new LiveKitROS2Publisher<T>(_room, topicName, messageType, queueSize);
            var key = $"{topicName}:{messageType}";
            _publishers[key] = publisher;

            return publisher;
        }

        /// <summary>
        /// Create a custom ROS2 message publisher
        /// </summary>
        public LiveKitROS2Publisher<CustomMessage> CreateCustomPublisher(string topicName, string messageType, int queueSize = 10)
        {
            var publisher = new LiveKitROS2Publisher<CustomMessage>(_room, topicName, messageType, queueSize);
            var key = $"{topicName}:{messageType}";
            _publishers[key] = publisher;

            return publisher;
        }

        /// <summary>
        /// Call ROS2 service
        /// </summary>
        public void CallService(string serviceName, string serviceType, Dictionary<string, object> request, Action<Dictionary<string, object>> responseCallback = null)
        {
            var requestId = Guid.NewGuid().ToString();
            var servicePacket = new ROS2ServicePacket
            {
                PacketType = "ros2_service_call",
                ServiceName = serviceName,
                ServiceType = serviceType,
                Request = request,
                RequestId = requestId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0
            };

            if (responseCallback != null)
            {
                _serviceResponseCallbacks[requestId] = responseCallback;
            }

            var jsonData = JsonSerializer.Serialize(servicePacket, _jsonOptions);
            var dataBytes = System.Text.Encoding.UTF8.GetBytes(jsonData);
            _room.LocalParticipant.PublishData(dataBytes, reliable: true);
        }

        /// <summary>
        /// Send ROS2 action goal
        /// </summary>
        public string SendActionGoal(
            string actionName, 
            string actionType, 
            Dictionary<string, object> goal,
            Action<Dictionary<string, object>> onGoalResponse = null,
            Action<Dictionary<string, object>> onFeedback = null,
            Action<Dictionary<string, object>> onResult = null)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("LiveKit room is not connected");
            }

            var goalId = Guid.NewGuid().ToString();
            var actionPacket = new ROS2ActionGoalPacket
            {
                PacketType = "ros2_action_send_goal",
                ActionName = actionName,
                ActionType = actionType,
                Goal = goal,
                GoalId = goalId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0
            };

            // Register callbacks
            if (onGoalResponse != null || onFeedback != null || onResult != null)
            {
                _actionCallbacks[goalId] = new ROS2ActionCallbacks
                {
                    OnGoalResponse = onGoalResponse,
                    OnFeedback = onFeedback,
                    OnResult = onResult
                };
            }

            try
            {
                var jsonData = JsonSerializer.Serialize(actionPacket, _jsonOptions);
                var dataBytes = System.Text.Encoding.UTF8.GetBytes(jsonData);
                _room.LocalParticipant.PublishData(dataBytes, reliable: true);
                
                Debug.Log($"Action goal sent: {actionName} (ID: {goalId})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to send action goal: {ex.Message}");
                _actionCallbacks.Remove(goalId);
                throw;
            }

            return goalId;
        }

        /// <summary>
        /// Cancel ROS2 action goal
        /// </summary>
        public void CancelActionGoal(string actionName, string goalId)
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("LiveKit room is not connected");
            }

            var cancelPacket = new ROS2ActionCancelPacket
            {
                PacketType = "ros2_action_cancel_goal",
                ActionName = actionName,
                GoalId = goalId,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0
            };

            try
            {
                var jsonData = JsonSerializer.Serialize(cancelPacket, _jsonOptions);
                var dataBytes = System.Text.Encoding.UTF8.GetBytes(jsonData);
                _room.LocalParticipant.PublishData(dataBytes, reliable: true);
                
                Debug.Log($"Action goal cancel requested: {actionName} (ID: {goalId})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to cancel action goal: {ex.Message}");
                throw;
            }
        }

        private void OnDataReceived(byte[] data, Participant participant, DataPacketKind kind, string topic)
        {
            try
            {
                var jsonString = System.Text.Encoding.UTF8.GetString(data);
                var packet = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString, _jsonOptions);

                if (packet?.ContainsKey("packetType") == true)
                {
                    var packetType = packet["packetType"].ToString();

                    if (packetType == "ros2_service_response")
                    {
                        // Handle ROS2 service response
                        HandleServiceResponse(packet);
                    }
                    else if (packetType == "ros2_action_goal_response")
                    {
                        // Handle ROS2 action goal response (accepted/rejected)
                        HandleActionGoalResponse(packet);
                    }
                    else if (packetType == "ros2_action_feedback")
                    {
                        // Handle ROS2 action feedback
                        HandleActionFeedback(packet);
                    }
                    else if (packetType == "ros2_action_result")
                    {
                        // Handle ROS2 action result
                        HandleActionResult(packet);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error occurred while processing data: {ex.Message}");
            }
        }

        private void HandleServiceResponse(Dictionary<string, object> packet)
        {
            Console.WriteLine("Get service response.");
            try
            {
                if (packet.ContainsKey("requestId"))
                {
                    string requestId = packet["requestId"].ToString();

                    if (_serviceResponseCallbacks.TryGetValue(requestId, out var callback))
                    {
                        callback?.Invoke(packet);
                        _serviceResponseCallbacks.Remove(requestId);
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error occurred while processing service response: {ex.Message}");
            }
        }

        private void HandleActionGoalResponse(Dictionary<string, object> packet)
        {
            try
            {
                if (packet.ContainsKey("goalId"))
                {
                    string goalId = packet["goalId"].ToString();
                    bool accepted = packet.ContainsKey("accepted") && 
                                   Convert.ToBoolean(packet["accepted"]);

                    Debug.Log($"Action goal response: Goal {goalId} {(accepted ? "ACCEPTED" : "REJECTED")}");

                    if (_actionCallbacks.TryGetValue(goalId, out var callbacks))
                    {
                        callbacks.OnGoalResponse?.Invoke(packet);
                        
                        // If rejected, remove callbacks
                        if (!accepted)
                        {
                            _actionCallbacks.Remove(goalId);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing action goal response: {ex.Message}");
            }
        }

        private void HandleActionFeedback(Dictionary<string, object> packet)
        {
            try
            {
                if (packet.ContainsKey("goalId"))
                {
                    string goalId = packet["goalId"].ToString();

                    if (_actionCallbacks.TryGetValue(goalId, out var callbacks))
                    {
                        callbacks.OnFeedback?.Invoke(packet);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing action feedback: {ex.Message}");
            }
        }

        private void HandleActionResult(Dictionary<string, object> packet)
        {
            try
            {
                if (packet.ContainsKey("goalId"))
                {
                    string goalId = packet["goalId"].ToString();

                    Debug.Log($"Action result received for goal {goalId}");

                    if (_actionCallbacks.TryGetValue(goalId, out var callbacks))
                    {
                        callbacks.OnResult?.Invoke(packet);
                        
                        // Remove callbacks after result received
                        _actionCallbacks.Remove(goalId);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing action result: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_room != null)
            {
                _room.DataReceived -= OnDataReceived;
            }
            _serviceResponseCallbacks?.Clear();
            _actionCallbacks?.Clear();
        }
    }

    /// <summary>
    /// ROS2 service packet
    /// </summary>
    public class ROS2ServicePacket
    {
        public string PacketType { get; set; } = "ros2_service_call";
        public string ServiceName { get; set; } = "";
        public string ServiceType { get; set; } = "";
        public Dictionary<string, object> Request { get; set; } = new Dictionary<string, object>();
        public string RequestId { get; set; } = "";
        public double Timestamp { get; set; }
    }

    /// <summary>
    /// ROS2 action goal packet
    /// </summary>
    public class ROS2ActionGoalPacket
    {
        public string PacketType { get; set; } = "ros2_action_send_goal";
        public string ActionName { get; set; } = "";
        public string ActionType { get; set; } = "";
        public Dictionary<string, object> Goal { get; set; } = new Dictionary<string, object>();
        public string GoalId { get; set; } = "";
        public double Timestamp { get; set; }
    }

    /// <summary>
    /// ROS2 action cancel packet
    /// </summary>
    public class ROS2ActionCancelPacket
    {
        public string PacketType { get; set; } = "ros2_action_cancel_goal";
        public string ActionName { get; set; } = "";
        public string GoalId { get; set; } = "";
        public double Timestamp { get; set; }
    }

    /// <summary>
    /// ROS2 action callbacks
    /// </summary>
    public class ROS2ActionCallbacks
    {
        public Action<Dictionary<string, object>> OnGoalResponse { get; set; }
        public Action<Dictionary<string, object>> OnFeedback { get; set; }
        public Action<Dictionary<string, object>> OnResult { get; set; }
    }
}
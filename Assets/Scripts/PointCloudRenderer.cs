using Draco;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Point cloud frame data structure for multi-frame assembly
/// </summary>
public class PointCloudFrame
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

/// <summary>
/// Point cloud renderer
/// </summary>
public class PointCloudRenderer : MonoBehaviour
{
    [Header("Point Cloud Settings")]
    [Tooltip("Maximum number of point cloud frames to accumulate")]
    public int pointCloudBufferSize = 30;
    [Tooltip("Color tint applied to point cloud")]
    public Color pointTint = Color.white;
    [Tooltip("VR Camera transform (leave empty to auto-detect CenterEyeAnchor)")]
    public Transform vrCameraTransform = null;
    public Material pointCloudMaterial;

    // Single frame point cloud buffer
    private byte[] pointCloudBuffer = new byte[1024 * 1024]; // 1 MB buffer

    // Multi-frame point cloud assembly
    private Dictionary<int, PointCloudFrame> pointCloudFrames = new Dictionary<int, PointCloudFrame>();
    
    // Point cloud ring buffer for accumulating multiple frames
    private Queue<Mesh> pointCloudRingBuffer = new Queue<Mesh>();
    private Mesh accumulatedPointCloudMesh = null;
    private bool needsRebuild = false;

    // Camera pose when robot connects, serves as point cloud reference coordinate system
    private Vector3 initialCameraPosition;
    private Quaternion initialCameraRotation;
    private bool cameraPoseRecorded = false;

    void Update()
    {
        // Rebuild accumulated mesh if needed
        if (needsRebuild)
        {
            RebuildAccumulatedMesh();
            needsRebuild = false;
        }
    }

    /// <summary>
    /// Handle received point cloud data
    /// </summary>
    public void HandlePointCloudData(byte[] data, string topic)
    {
        if (topic == "pointcloud:meta")
        {
            HandlePointCloudMeta(data);
        }
        else if (topic == "pointcloud")
        {
            HandlePointCloudChunk(data);
        }
    }

    /// <summary>
    /// Handle point cloud metadata
    /// </summary>
    private void HandlePointCloudMeta(byte[] data)
    {
        try
        {
            var jsonString = System.Text.Encoding.Default.GetString(data);
            var meta = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonString);

            if (meta != null && meta.ContainsKey("id") && meta.ContainsKey("chunk") && meta.ContainsKey("total"))
            {
                int id = GetIntValue(meta["id"]);
                int chunk = GetIntValue(meta["chunk"]);
                int total = GetIntValue(meta["total"]);

                if (!pointCloudFrames.ContainsKey(id))
                {
                    pointCloudFrames[id] = new PointCloudFrame
                    {
                        Id = id,
                        TotalChunks = total,
                        Chunks = new Dictionary<int, byte[]>()
                    };

                    // Clean up old frames
                    while (pointCloudFrames.Count > 3)
                    {
                        var oldestKey = pointCloudFrames.Keys.Min();
                        pointCloudFrames.Remove(oldestKey);
                    }
                }

                // Check if last chunk and frame complete
                if (chunk == total - 1 && pointCloudFrames[id].IsComplete)
                {
                    var completeData = pointCloudFrames[id].GetCompleteData();
                    if (completeData != null && completeData.Length <= pointCloudBuffer.Length)
                    {
                        Array.Copy(completeData, 0, pointCloudBuffer, 0, completeData.Length);
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

    /// <summary>
    /// Handle point cloud data chunk
    /// </summary>
    private void HandlePointCloudChunk(byte[] data)
    {
        if (pointCloudFrames.Count > 0)
        {
            var latestFrame = pointCloudFrames.Values.OrderBy(f => f.Id).Last();
            var nextChunkIndex = latestFrame.ReceivedChunks;

            if (nextChunkIndex < latestFrame.TotalChunks)
            {
                latestFrame.Chunks[nextChunkIndex] = new byte[data.Length];
                Array.Copy(data, 0, latestFrame.Chunks[nextChunkIndex], 0, data.Length);

                // Check if frame is complete
                if (latestFrame.IsComplete)
                {
                    var completeData = latestFrame.GetCompleteData();
                    if (completeData != null && completeData.Length <= pointCloudBuffer.Length)
                    {
                        Array.Copy(completeData, 0, pointCloudBuffer, 0, completeData.Length);
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
            }
            else
            {
                Debug.LogWarning("Received pointcloud data chunk is too large for buffer");
            }
        }
    }

    /// <summary>
    /// Render point cloud from byte array data
    /// </summary>
    private void RenderPointCloud(byte[] pointCloudData, int frameId)
    {
        try
        {
            StartCoroutine(RenderPointCloudAsync(pointCloudData, frameId));
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error rendering point cloud frame {frameId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Async point cloud decoding and rendering coroutine
    /// </summary>
    private IEnumerator RenderPointCloudAsync(byte[] pointCloudData, int frameId)
    {
        var decodingTask = DracoDecoder.DecodeMesh(pointCloudData);

        // Wait for decoding to complete
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
    /// Add decoded mesh to ring buffer
    /// </summary>
    private void AddMeshToRingBuffer(Mesh newMesh)
    {
        pointCloudRingBuffer.Enqueue(newMesh);
        
        if (pointCloudRingBuffer.Count > pointCloudBufferSize)
        {
            var oldMesh = pointCloudRingBuffer.Dequeue();
            if (oldMesh != null)
            {
                Destroy(oldMesh);
            }
        }
        
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

        if (accumulatedPointCloudMesh != null)
        {
            Destroy(accumulatedPointCloudMesh);
        }

        accumulatedPointCloudMesh = new Mesh();
        accumulatedPointCloudMesh.name = "AccumulatedPointCloud";

        List<Vector3> allVertices = new List<Vector3>();
        List<Color32> allColors = new List<Color32>();
        List<int> allIndices = new List<int>();

        int vertexOffset = 0;
        foreach (var mesh in pointCloudRingBuffer)
        {
            if (mesh == null) continue;

            Vector3[] vertices = mesh.vertices;
            allVertices.AddRange(vertices);

            // Add colors
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
                Color32[] defaultColors = new Color32[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                {
                    defaultColors[i] = Color.white;
                }
                allColors.AddRange(defaultColors);
            }

            int[] indices = mesh.GetIndices(0);
            for (int i = 0; i < indices.Length; i++)
            {
                allIndices.Add(indices[i] + vertexOffset);
            }

            vertexOffset += vertices.Length;
        }

        accumulatedPointCloudMesh.indexFormat = allVertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        accumulatedPointCloudMesh.SetVertices(allVertices);
        accumulatedPointCloudMesh.SetColors(allColors);
        accumulatedPointCloudMesh.SetIndices(allIndices.ToArray(), MeshTopology.Points, 0, false);
        accumulatedPointCloudMesh.RecalculateBounds();

        // Debug.Log($"Accumulated {pointCloudRingBuffer.Count} frames with total {allVertices.Count} vertices");

        UpdatePointCloudDisplay(accumulatedPointCloudMesh, -1);
    }

    /// <summary>
    /// Update or create point cloud display object
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

            if (m != null)
            {
                if (m.HasProperty("_PointSize")) m.SetFloat("_PointSize", .1f);
                if (m.HasProperty("_Tint")) m.SetColor("_Tint", pointTint);
            }

            render.material = m;
            render.shadowCastingMode = ShadowCastingMode.Off;
            render.receiveShadows = false;

            if (cameraPoseRecorded)
            {
                pointCloudObject.transform.position = initialCameraPosition;
                pointCloudObject.transform.rotation = initialCameraRotation;
                Debug.Log($"Point cloud positioned at initial camera pose: Pos={initialCameraPosition}, Rot={initialCameraRotation.eulerAngles}");
            }
            else
            {
                Debug.LogWarning("Camera pose not recorded, point cloud at world origin");
            }
        }

        var meshFilter = pointCloudObject.GetComponent<MeshFilter>();
        meshFilter.mesh = mesh;
    }

    /// <summary>
    /// Record initial camera pose
    /// </summary>
    public void RecordInitialCameraPose()
    {
        Transform camera = GetVRCamera();
        if (camera != null)
        {
            initialCameraPosition = camera.position;
            initialCameraRotation = camera.rotation * Quaternion.Euler(-90f, 0f, 90f);
            cameraPoseRecorded = true;
            Debug.Log($"Recorded initial camera pose: Pos={initialCameraPosition}, Rot={initialCameraRotation.eulerAngles}");
        }
        else
        {
            Debug.LogError("Failed to record initial camera pose - no camera found!");
            cameraPoseRecorded = false;
        }
    }

    /// <summary>
    /// Get VR camera transform
    /// </summary>
    private Transform GetVRCamera()
    {
        if (vrCameraTransform != null)
        {
            return vrCameraTransform;
        }

        GameObject ovrCameraRig = GameObject.Find("OVRCameraRig");
        if (ovrCameraRig != null)
        {
            Transform centerEye = ovrCameraRig.transform.Find("TrackingSpace/CenterEyeAnchor");
            if (centerEye != null)
            {
                vrCameraTransform = centerEye;
                return centerEye;
            }
        }

        if (Camera.main != null)
        {
            return Camera.main.transform;
        }

        Debug.LogWarning("No VR camera found!");
        return null;
    }

    /// <summary>
    /// Clean up all meshes in point cloud buffer
    /// </summary>
    public void CleanupPointCloudBuffer()
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

    /// <summary>
    /// Helper method: Get integer value
    /// </summary>
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

    void OnDestroy()
    {
        CleanupPointCloudBuffer();
    }
}

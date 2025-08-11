using UnityEngine;

public static class CoordinateConverter
{
    public static class UnityToROS2
    {
        public static Vector3 ConvertPosition(Vector3 unityPos)
        {
            return new Vector3(
                -unityPos.y, 
                -unityPos.x,
                unityPos.z 
            );
        }

        public static Quaternion ConvertRotation(Quaternion unityRot)
        {
            Quaternion nu = unityRot.normalized;
            return new Quaternion(nu.y, nu.x, -nu.z, nu.w);
        }

        public static (Vector3 position, Quaternion rotation) ConvertPose(Vector3 unityPos, Quaternion unityRot)
        {
            return (ConvertPosition(unityPos), ConvertRotation(unityRot));
        }
    }
}
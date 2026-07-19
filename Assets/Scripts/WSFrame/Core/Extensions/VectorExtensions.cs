using UnityEngine;

public static class VectorExtensions
{
    /// <summary>
    /// 将 vec2 的 x，y 作为 vector3 的 x，y
    /// </summary>
    /// <param name="vector2"></param>
    /// <param name="z"></param>
    /// <returns></returns>
    public static Vector3 ToVector3_XY(this Vector2 vector2, float z = 0) => new(vector2.x, vector2.y, z);

    public static Vector3 ToVector3_XZ(this Vector2 vector2, float y = 0) => new(vector2.x, y, vector2.y);

    public static Vector3 ToVector3_YZ(this Vector2 vector2, float x = 0) => new(x, vector2.x, vector2.y);
}

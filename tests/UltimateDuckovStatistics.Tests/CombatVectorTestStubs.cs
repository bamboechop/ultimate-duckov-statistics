namespace UnityEngine;

public sealed class Vector2
{
    public float x { get; }
    public float y { get; }
    public Vector2(float x, float y) { this.x = x; this.y = y; }
    public static Vector2 operator +(Vector2 left, Vector2 right) => new(left.x + right.x, left.y + right.y);
}

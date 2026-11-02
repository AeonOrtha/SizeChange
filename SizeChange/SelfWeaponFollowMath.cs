using System;
using System.Numerics;

namespace SizeChange;

// Managed transform math, deliberately independent of the hook and game memory.
internal static class SelfWeaponFollowMath
{
    internal readonly record struct Pose(Vector3 Position, Quaternion Rotation, Vector3 Scale)
    {
        internal Matrix4x4 Matrix => Matrix4x4.CreateScale(Scale) *
            Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(Rotation)) * Matrix4x4.CreateTranslation(Position);
        internal bool Valid => Finite(Position) && Finite(Scale) && Scale.X > 0.00001f && Scale.Y > 0.00001f && Scale.Z > 0.00001f &&
            Scale.X < 10000 && Scale.Y < 10000 && Scale.Z < 10000 &&
            float.IsFinite(Rotation.LengthSquared()) && Rotation.LengthSquared() > 0.5f && Rotation.LengthSquared() < 1.5f;
    }

    // Carry the existing weapon/grip through the hand's change. Never multiply
    // weapon size: the game already owns its scale and normal attachment offset.
    internal static bool TryCorrect(Pose root, Pose before, Pose after, Pose weapon, out Pose corrected)
    {
        corrected = weapon;
        if (!root.Valid || !before.Valid || !after.Valid || !weapon.Valid) return false;
        if (!Matrix4x4.Invert(before.Matrix * root.Matrix, out var inverse)) return false;
        var delta = inverse * after.Matrix * root.Matrix;
        var position = Vector3.Transform(weapon.Position, delta);
        var oldRotation = Quaternion.Normalize(root.Rotation * before.Rotation);
        var newRotation = Quaternion.Normalize(root.Rotation * after.Rotation);
        var rotation = Quaternion.Normalize(newRotation * Quaternion.Inverse(oldRotation) * weapon.Rotation);
        corrected = new(position, rotation, weapon.Scale);
        return corrected.Valid;
    }

    internal static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) &&
        Math.Abs(value.X) < 10000000 && Math.Abs(value.Y) < 10000000 && Math.Abs(value.Z) < 10000000;
}

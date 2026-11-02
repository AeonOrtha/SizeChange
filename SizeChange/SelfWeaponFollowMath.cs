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

    // Carry the existing weapon/grip through the hand's change. Bone-follow
    // itself preserves weapon size; explicit user adjustments are applied next.
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

    internal static bool TryAdjust(Pose weapon, SelfWeaponAdjustment.Values adjustment, out Pose result)
    {
        result = weapon;
        if (!weapon.Valid || !float.IsFinite(adjustment.Roll) || !float.IsFinite(adjustment.Pitch) ||
            !float.IsFinite(adjustment.Yaw) || !float.IsFinite(adjustment.Scale) || adjustment.Scale < .01f || adjustment.Scale > 20) return false;
        if (adjustment.Neutral) return true;
        const float radians = MathF.PI / 180;
        var local = Quaternion.CreateFromYawPitchRoll(adjustment.Yaw * radians, adjustment.Pitch * radians, adjustment.Roll * radians);
        result = weapon with { Rotation = Quaternion.Normalize(weapon.Rotation * local), Scale = weapon.Scale * adjustment.Scale };
        return result.Valid;
    }

    // The engine can refresh position/rotation without refreshing scale. Restore
    // each still-owned component independently so manual scale never compounds.
    internal static Pose RestoreOwned(Pose current, Pose original, Pose written) => new(
        current.Position == written.Position ? original.Position : current.Position,
        current.Rotation == written.Rotation ? original.Rotation : current.Rotation,
        current.Scale == written.Scale ? original.Scale : current.Scale);
}

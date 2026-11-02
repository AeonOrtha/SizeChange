using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok.Animation.Rig;
using Transform = FFXIVClientStructs.FFXIV.Client.Graphics.Transform;
using Pose = SizeChange.SelfWeaponFollowMath.Pose;

namespace SizeChange;

// The only unsafe part of the experiment. Never writes attachment definitions,
// body bones, .atch data, files, or an actor other than the live local player.
internal sealed unsafe class SelfWeaponFollowNative : ISelfWeaponFollowTarget
{
    private readonly Func<bool> enabled;
    private readonly Func<SelfWeaponFollowSettings> settings;
    private readonly Dictionary<int, Applied> previous = new();
    private readonly record struct Identity(nint Actor, uint Entity, uint Territory, nint Body, nint Skeleton);
    private sealed record Item(Identity Owner, int Slot, nint Weapon, nint Skeleton, nint Pose,
        int Hand, int Parent, Pose Root, Pose HandBefore, Pose WeaponBefore);
    private sealed record Applied(Item Item, Pose Written);
    private sealed record Frame(List<Item> Items, SelfWeaponAdjustment.Values Adjustment);
    public string Status { get; private set; } = "Waiting for a drawn hand attachment";

    internal SelfWeaponFollowNative(Func<bool> enabled, Func<SelfWeaponFollowSettings> settings)
    { this.enabled = enabled; this.settings = settings; }

    private bool Resolve(out Character* actor, out CharacterBase* body, out Identity identity, bool requireActive)
    {
        actor = null; body = null; identity = default;
        // Check lifecycle flags before dereferencing any native actor/model.
        if (!Plugin.ClientState.IsLoggedIn || Plugin.Condition[ConditionFlag.BetweenAreas] ||
            Plugin.Condition[ConditionFlag.BetweenAreas51]) return false;
        var self = Plugin.ObjectTable.LocalPlayer;
        if (self == null || self.Address == 0) return false;
        actor = (Character*)self.Address;
        if (actor->DrawObject == null || actor->DrawObject->GetObjectType() != ObjectType.CharacterBase) return false;
        body = (CharacterBase*)actor->DrawObject;
        if (body->Skeleton == null) return false;
        identity = new(self.Address, self.EntityId, Plugin.ClientState.TerritoryType, (nint)body, (nint)body->Skeleton);
        if (!requireActive) return true;
        return enabled() && !Plugin.ClientState.IsPvP && !Plugin.ClientState.IsGPosing && self.CurrentHp > 0 &&
            !Plugin.Condition[ConditionFlag.OccupiedInCutSceneEvent] && !Plugin.Condition[ConditionFlag.WatchingCutscene78] &&
            !Plugin.Condition[ConditionFlag.Mounted] && actor->IsWeaponDrawn;
    }

    private static bool WeaponFor(Character* actor, CharacterBase* body, int slot, out Weapon* weapon)
    {
        weapon = null;
        if (slot < 0 || slot > 1) return false;
        var draw = actor->DrawData.Weapon((DrawDataContainer.WeaponSlot)slot).DrawData.DrawObject;
        if (draw == null || draw->GetObjectType() != ObjectType.CharacterBase) return false;
        var model = (CharacterBase*)draw;
        if (model->GetModelType() != CharacterBase.ModelType.Weapon || model->Skeleton == null || model->Skeleton == body->Skeleton) return false;
        weapon = (Weapon*)model;
        // Type 4 is a skeleton-bone attachment. Reject back/hip accessories and
        // detached/projectile weapons rather than guessing an anchor.
        return weapon->AttachTarget == body && model->Attach.ExecuteType == 4 &&
            model->Attach.OwnerSkeleton == body->Skeleton && model->Attach.TargetSkeleton == model->Skeleton &&
            model->Attach.AttachmentCount == 1 && model->Attach.SkeletonBoneAttachments != null;
    }

    private static bool HandFor(CharacterBase* body, Weapon* weapon, out hkaPose* pose, out int hand, out int parent)
    {
        pose = null; hand = -1; parent = -1;
        var skeleton = body->Skeleton;
        var mask = weapon->Attach.SkeletonBoneAttachments->BoneIndexMask;
        int partial = mask.PartialSkeletonIdx;
        parent = mask.BoneIdx;
        if (skeleton->PartialSkeletons == null || skeleton->PartialSkeletonCount > 32 || partial >= skeleton->PartialSkeletonCount) return false;
        pose = skeleton->PartialSkeletons[partial].GetHavokPose(0);
        if (pose == null || pose->Skeleton == null || pose->ModelInSync == 0 || pose->ModelPose.Data == null) return false;
        int count = pose->Skeleton->Bones.Length;
        if (count <= 0 || count > 4096 || parent < 0 || parent >= count ||
            pose->ModelPose.Length != count || pose->Skeleton->Bones.Data == null ||
            pose->Skeleton->ParentIndices.Data == null || pose->Skeleton->ParentIndices.Length != count) return false;
        // Resolve the actual attached hand, including left/right swaps. Stop at
        // the root; a sheathed weapon's anchor must never be forced to a hand.
        int index = parent;
        for (int depth = 0; depth < count && index >= 0 && index < count; depth++)
        {
            string? name = pose->Skeleton->Bones[index].Name.String;
            if (name == "j_te_r" || name == "j_te_l") { hand = index; return true; }
            int next = pose->Skeleton->ParentIndices[index];
            if (next == index) return false;
            index = next;
        }
        return false;
    }

    private static Pose Read(Transform value) => new(value.Position, value.Rotation, value.Scale);
    private static Pose ReadHand(hkaPose* pose, int index)
    {
        var value = pose->ModelPose[index];
        return new(new(value.Translation.X, value.Translation.Y, value.Translation.Z),
            new(value.Rotation.X, value.Rotation.Y, value.Rotation.Z, value.Rotation.W),
            new(value.Scale.X, value.Scale.Y, value.Scale.Z));
    }

    private static void Write(Skeleton* skeleton, Pose value)
    {
        // Runtime skeleton transform only. No attachment metadata writes.
        skeleton->Transform.Position = value.Position;
        skeleton->Transform.Rotation = value.Rotation;
        skeleton->Transform.Scale = value.Scale;
    }

    public object Capture()
    {
        Restore();
        var items = new List<Item>(2);
        var adjustment = settings().Current;
        if (!Resolve(out var actor, out var body, out var owner, true))
        { Status = "Waiting for Self with weapons drawn"; return new Frame(items, adjustment); }
        for (int slot = 0; slot < 2; slot++)
        {
            if (!WeaponFor(actor, body, slot, out var weapon) || !HandFor(body, weapon, out var pose, out int hand, out int parent)) continue;
            var root = Read(body->Skeleton->Transform);
            var before = ReadHand(pose, hand);
            var original = Read(weapon->Skeleton->Transform);
            if (!root.Valid || !before.Valid || !original.Valid) continue;
            items.Add(new(owner, slot, (nint)weapon, (nint)weapon->Skeleton, (nint)pose, hand, parent, root, before, original));
        }
        Status = items.Count == 0 ? "No supported drawn hand attachment" : "No hand change this frame";
        return new Frame(items, adjustment);
    }

    public int Apply(object state)
    {
        if (state is not Frame frame || !Resolve(out var actor, out var body, out var owner, true)) return 0;
        int count = 0;
        foreach (var item in frame.Items)
        {
            if (owner != item.Owner || !WeaponFor(actor, body, item.Slot, out var weapon) ||
                (nint)weapon != item.Weapon || (nint)weapon->Skeleton != item.Skeleton ||
                !HandFor(body, weapon, out var pose, out int hand, out int parent) || (nint)pose != item.Pose ||
                hand != item.Hand || parent != item.Parent) continue;
            // If the game/another plugin changed the weapon or body transform in
            // this interval, yield ownership instead of overwriting that change.
            if (Read(weapon->Skeleton->Transform) != item.WeaponBefore || Read(body->Skeleton->Transform) != item.Root) continue;
            var after = ReadHand(pose, hand);
            var corrected = item.WeaponBefore;
            if (after == item.HandBefore && frame.Adjustment.Neutral) continue;
            if (after != item.HandBefore && !SelfWeaponFollowMath.TryCorrect(item.Root, item.HandBefore, after, item.WeaponBefore, out corrected)) continue;
            if (!SelfWeaponFollowMath.TryAdjust(corrected, frame.Adjustment, out corrected)) continue;
            Write(weapon->Skeleton, corrected);
            previous[item.Slot] = new(item, corrected);
            count++;
        }
        return count;
    }

    public void Restore()
    {
        if (previous.Count == 0) return;
        if (Resolve(out var actor, out var body, out var owner, false))
        {
            foreach (var pair in previous)
            {
                var last = pair.Value;
                // Re-find via live Self; never dereference the saved pointers.
                if (owner == last.Item.Owner && WeaponFor(actor, body, pair.Key, out var weapon) &&
                    (nint)weapon == last.Item.Weapon && (nint)weapon->Skeleton == last.Item.Skeleton)
                {
                    var current = Read(weapon->Skeleton->Transform);
                    var restored = SelfWeaponFollowMath.RestoreOwned(current, last.Item.WeaponBefore, last.Written);
                    if (restored.Valid && restored != current) Write(weapon->Skeleton, restored);
                }
            }
        }
        previous.Clear();
    }
}

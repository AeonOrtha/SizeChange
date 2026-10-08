using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SizeChange;

// Portable configuration only. Actor state, preview state and the global switch
// deliberately do not belong to any profile file.
internal sealed class ProfileSettingsFile
{
    public string Format { get; set; } = "SizeChange.Profile";
    public int FormatVersion { get; set; } = 2;
    public string Profile { get; set; } = string.Empty;
    public GrowthSettings? Growth { get; set; }
    public bool? AffectSelf { get; set; }
    public bool? AffectPlayers { get; set; }
    public float? FlatHeightOffset { get; set; }
    public BoneHeartbeatSettings? Bones { get; set; }
    public List<string>? TrackedNames { get; set; }
    public PlayerProfileHeightSettings? LocalHeight { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true, IncludeFields = true, MaxDepth = 64,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private const int MaximumBytes = 8 * 1024 * 1024;
    internal static string Name(SCActorGroup group) => group switch
    {
        SCActorGroup.Self => "Self", SCActorGroup.Player => "AddedPlayers",
        SCActorGroup.Monster => "Monsters", _ => throw new ArgumentOutOfRangeException(nameof(group)),
    };
    internal static ProfileSettingsFile Capture(Configuration config, SCActorGroup group) => new()
    {
        Profile = Name(group),
        Growth = group switch { SCActorGroup.Self => config.SelfSettings, SCActorGroup.Player => config.PlayerSettings, _ => config.MonsterSettings },
        AffectPlayers = group == SCActorGroup.Player ? config.AffectPlayers : null,
        AffectSelf = group == SCActorGroup.Self ? config.AffectSelf : null,
        FlatHeightOffset = group == SCActorGroup.Self ? config.SelfFlatHeightOffset : group == SCActorGroup.Player ? config.PlayerProfileHeight.Offset : null,
        Bones = group == SCActorGroup.Self ? config.SelfBoneHeartbeat : group == SCActorGroup.Player ? config.PlayerBoneHeartbeat : null,
        TrackedNames = group == SCActorGroup.Player ? config.TrackedPlayerNames : group == SCActorGroup.Monster ? config.TrackedMonsterNames : null,
        LocalHeight = group == SCActorGroup.Player ? config.PlayerProfileHeight : null,
    };
    internal string ToJson() => JsonSerializer.Serialize(this, Options);
    internal static ProfileSettingsFile Parse(string json, SCActorGroup group)
    {
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Profile exceeds 8 MB.");
        // Check the envelope explicitly: missing metadata must not become defaults.
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("Format", out var format) || format.GetString() != "SizeChange.Profile" ||
            !root.TryGetProperty("FormatVersion", out var version) || !version.TryGetInt32(out int value) || value is not (1 or 2))
            throw new InvalidDataException("Unsupported SizeChange profile format.");
        CheckNumbers(root);
        var file = JsonSerializer.Deserialize<ProfileSettingsFile>(json, Options) ?? throw new InvalidDataException("Empty profile.");
        if (!Compatible(file.Profile, group)) throw new InvalidDataException($"This is a {file.Profile} profile, not compatible with {Name(group)}.");
        if (file.Growth == null) throw new InvalidDataException("Missing growth settings.");
        file.Growth.Validate();
        if (file.Profile is "Self" or "AddedPlayers")
        {
            file.Bones ??= new(); // Older Added Players exports predate bone settings.
            file.Bones.Validate();
            file.FlatHeightOffset ??= file.Profile == "AddedPlayers" ? file.LocalHeight?.Offset ?? 0.5f : 0f;
            if (!float.IsFinite(file.FlatHeightOffset.Value)) throw new InvalidDataException("Invalid height offset.");
            file.FlatHeightOffset = Math.Clamp(file.FlatHeightOffset.Value, 0f, 100f);
            file.AffectSelf ??= true;
            file.AffectPlayers ??= true;
        }
        if (file.Profile is "AddedPlayers" or "Monsters")
        {
            if (file.TrackedNames == null) throw new InvalidDataException("Missing tracked names.");
            file.TrackedNames = file.TrackedNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (file.Profile == "AddedPlayers")
            {
                file.LocalHeight ??= new();
                file.LocalHeight.Validate();
            }
        }
        return file;
    }
    private static void CheckNumbers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number &&
            (!element.TryGetDouble(out double number) || !double.IsFinite(number) || Math.Abs(number) > float.MaxValue))
            throw new InvalidDataException("A numeric setting is out of range.");
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) CheckNumbers(property.Value);
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckNumbers(item);
    }

    private static bool Compatible(string source, SCActorGroup target) => source == Name(target) ||
        (source is "Self" or "AddedPlayers" && target is SCActorGroup.Self or SCActorGroup.Player);
    private static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;

    internal void Apply(Configuration config, SCActorGroup group)
    {
        if (!Compatible(Profile, group)) throw new InvalidDataException("Incompatible profile tab.");
        bool cross = Profile != Name(group);
        var growth = Copy(Growth ?? throw new InvalidDataException("Missing growth settings."));
        growth.Validate();
        var bones = Copy(Bones ?? new BoneHeartbeatSettings());
        bones.Validate();
        float height = Math.Clamp(FlatHeightOffset ?? LocalHeight?.Offset ?? 0f, 0f, 100f);
        switch (group)
        {
            case SCActorGroup.Self:
                // A remote player's intercepted baseline must never replace the
                // local user's selected Customize+ base profile.
                if (cross) bones.BaseProfileId = config.SelfBoneHeartbeat.BaseProfileId;
                config.SelfSettings = growth;
                if (!cross) config.AffectSelf = AffectSelf ?? true;
                config.SelfFlatHeightOffset = height; config.SelfBoneHeartbeat = bones;
                break;
            case SCActorGroup.Player:
                if (cross) bones.BaseProfileId = config.PlayerBoneHeartbeat.BaseProfileId;
                config.PlayerSettings = growth; config.PlayerBoneHeartbeat = bones;
                if (!cross)
                {
                    config.AffectPlayers = AffectPlayers ?? true;
                    config.TrackedPlayerNames = Copy(TrackedNames ?? new());
                    config.PlayerProfileHeight = Copy(LocalHeight ?? new());
                }
                else config.PlayerProfileHeight = Copy(config.PlayerProfileHeight);
                config.PlayerProfileHeight.Offset = height;
                break;
            case SCActorGroup.Monster:
                config.MonsterSettings = growth; config.TrackedMonsterNames = Copy(TrackedNames ?? new());
                break;
        }
    }
    internal static ProfileSettingsFile Read(string path, SCActorGroup group)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > MaximumBytes) throw new InvalidDataException("Profile exceeds 8 MB.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), group);
    }
    internal void Write(string path)
    {
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) path += ".json";
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, ToJson()); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

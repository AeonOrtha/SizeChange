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
    public int FormatVersion { get; set; } = 1;
    public string Profile { get; set; } = string.Empty;
    public GrowthSettings? Growth { get; set; }
    public bool? AffectSelf { get; set; }
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
        AffectSelf = group == SCActorGroup.Self ? config.AffectSelf : null,
        FlatHeightOffset = group == SCActorGroup.Self ? config.SelfFlatHeightOffset : null,
        Bones = group == SCActorGroup.Self ? config.SelfBoneHeartbeat : null,
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
            !root.TryGetProperty("FormatVersion", out var version) || !version.TryGetInt32(out int value) || value != 1)
            throw new InvalidDataException("Unsupported SizeChange profile format.");
        CheckNumbers(root);
        var file = JsonSerializer.Deserialize<ProfileSettingsFile>(json, Options) ?? throw new InvalidDataException("Empty profile.");
        if (file.Profile != Name(group)) throw new InvalidDataException($"This is a {file.Profile} profile, not {Name(group)}.");
        if (file.Growth == null) throw new InvalidDataException("Missing growth settings.");
        file.Growth.Validate();
        if (group == SCActorGroup.Self)
        {
            if (file.Bones == null || file.AffectSelf == null || file.FlatHeightOffset == null || !float.IsFinite(file.FlatHeightOffset.Value))
                throw new InvalidDataException("Missing or invalid Self settings.");
            file.Bones.Validate();
            file.FlatHeightOffset = Math.Clamp(file.FlatHeightOffset.Value, 0f, 100f);
        }
        else
        {
            if (file.TrackedNames == null) throw new InvalidDataException("Missing tracked names.");
            file.TrackedNames = file.TrackedNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (group == SCActorGroup.Player)
            {
                if (file.LocalHeight == null) throw new InvalidDataException("Missing local height settings.");
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

    internal void Apply(Configuration config, SCActorGroup group)
    {
        if (Profile != Name(group)) throw new InvalidDataException("Wrong profile tab.");
        switch (group)
        {
            case SCActorGroup.Self:
                config.SelfSettings = Growth!; config.AffectSelf = AffectSelf!.Value;
                config.SelfFlatHeightOffset = FlatHeightOffset!.Value; config.SelfBoneHeartbeat = Bones!; break;
            case SCActorGroup.Player:
                config.PlayerSettings = Growth!; config.TrackedPlayerNames = TrackedNames!; config.PlayerProfileHeight = LocalHeight!; break;
            case SCActorGroup.Monster:
                config.MonsterSettings = Growth!; config.TrackedMonsterNames = TrackedNames!; break;
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

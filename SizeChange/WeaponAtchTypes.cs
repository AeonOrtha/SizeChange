using System;

namespace SizeChange;

internal static class WeaponAtchTypes
{
    // ATCH identifiers, not job IDs or a guessed one/two-handed classification.
    internal static readonly (string Code, string Name)[] Known =
    {
        ("swd", "Sword"), ("sld", "Shield"), ("2ax", "Greataxe"),
        ("2sw", "Greatsword"), ("2sp", "Spear"), ("2bw", "Bow"),
        ("2st", "Greatstaff"), ("stf", "Staff"), ("rod", "Cane"),
        ("2bk", "Book"), ("2gn", "Gun"), ("2gl", "Globe"),
        ("2gb", "Gunblade"), ("2kt", "Katana"), ("2rp", "Rapier"),
        ("2km", "Scythe"), ("2ff", "Nouliths"), ("dgr", "Daggers"),
        ("chk", "Chakrams"), ("clw", "Claws"), ("clg", "Gloves"),
        ("cls", "Fist variant"), ("bld", "Viper main blade"),
        ("bll", "Viper offhand blade"), ("bl2", "Viper double blade"),
        ("brs", "Brush"), ("plt", "Palette"), ("orb", "Focus"),
    };

    private static bool Valid(char c) => c is >= 'a' and <= 'z' or >= '0' and <= '9';
    internal static uint Encode(string? text)
    {
        if (text == null || text.Length is < 3 or > 4) return 0;
        uint value = 0;
        foreach (char c in text)
        {
            if (!Valid(c)) return 0;
            value = (value << 8) | c;
        }
        return value;
    }

    // Inline uint stores the displayed moniker in big-endian character order.
    // Validate all bytes; never follow a string pointer or read past four bytes.
    internal static bool IsValid(uint value)
    {
        if (value >> 24 != 0 && !Valid((char)(value >> 24))) return false;
        return Valid((char)((value >> 16) & 255)) &&
            Valid((char)((value >> 8) & 255)) && Valid((char)(value & 255));
    }

    internal static string Decode(uint value)
    {
        if (!IsValid(value)) return "unknown";
        Span<char> chars = stackalloc char[4];
        for (int i = 0; i < 4; i++) chars[i] = (char)((value >> (24 - i * 8)) & 255);
        return new string(value >> 24 == 0 ? chars[1..] : chars);
    }

    internal static string Label(string code)
    {
        foreach (var type in Known)
            if (type.Code == code) return $"{code} — {type.Name}";
        return code.Length == 0 ? "Custom type" : code;
    }
}

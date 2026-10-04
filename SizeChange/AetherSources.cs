using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace SizeChange;

internal sealed class AetherSources
{
    internal readonly record struct Source(nint Address, uint EntityId, Vector3 Position, bool Large, nint ModelAddress);
    internal readonly record struct Hit(Source Crystal, float Ratio);
    private readonly List<Source> sources = new();
    public IReadOnlyList<Source> Current => sources;
    private readonly HashSet<uint> largeIds = new();
    private bool loadedTypes;
    private readonly HashSet<nint> seen = new();
    // EObjName rows named Aethernet shard from xivapi/ffxiv-datamining.
    // Stable IDs avoid relying on the client's language.
    private static readonly HashSet<uint> ShardIds = new() { 2000151, 2000153, 2000154, 2000155, 2000156, 2000157, 2003395, 2003396, 2003397, 2003398, 2003399, 2003400, 2003401, 2003402, 2003403, 2003404, 2003405, 2003406, 2003407, 2003408, 2003409, 2003995, 2003996, 2003997, 2003998, 2003999, 2004000, 2004968, 2004969, 2004970, 2004971, 2004972, 2004973, 2004974, 2004976, 2004977, 2004978, 2004979, 2004980, 2004981, 2004982, 2004983, 2004984, 2004985, 2004986, 2004987, 2004988, 2004989, 2007434, 2007435, 2007436, 2007437, 2007438, 2007439, 2007855, 2007856, 2007857, 2007858, 2007859, 2007860, 2007861, 2007862, 2007863, 2007864, 2007865, 2007866, 2007867, 2007868, 2007869, 2007870, 2009421, 2009432, 2009433, 2009562, 2009563, 2009564, 2009565, 2009615, 2009616, 2009617, 2009618, 2009713, 2009714, 2009715, 2009981, 2010135, 2011142, 2011162, 2011163, 2011241, 2011243, 2011373, 2011374, 2011384, 2011385, 2011386, 2011387, 2011388, 2011389, 2011573, 2011574, 2011575, 2011677, 2011678, 2011679, 2011680, 2011681, 2011682, 2011683, 2011684, 2011685, 2011686, 2011687, 2011688, 2011689, 2011690, 2011691, 2011692, 2012252, 2012253, 2015052, 2015053, 2015054, 2015055, 2015056, 2015057, 2015058, 2015059, 2015060, 2015061, 2015062, 2015063, 2015064, 2015065, 2015066, 2015100, 2015422, 2015423, 2015424, 2015425, 2015426 };

    public unsafe void Refresh(IObjectTable objects, IDataManager data, bool allowed)
    {
        sources.Clear();
        seen.Clear();
        if (!allowed) return;
        if (!loadedTypes)
        {
            foreach (var row in data.GetExcelSheet<Lumina.Excel.Sheets.Aetheryte>())
                if (row.IsAetheryte) largeIds.Add(row.RowId);
            loadedTypes = true;
        }
        foreach (var obj in objects) Add((GameObject*)obj.Address);

        // Yard furniture can live outside the ordinary object table.
        var housing = HousingManager.Instance();
        if (housing == null || housing->OutdoorTerritory == null ||
            housing->CurrentTerritory != (HousingTerritory*)housing->OutdoorTerritory) return;
        var array = &housing->OutdoorTerritory->FurnitureManager.ObjectManager.ObjectArray;
        var yardObjects = array->Objects;
        for (int i = 0; i < Math.Min((int)array->ObjectCount, yardObjects.Length); i++)
            Add(yardObjects[i].Value);
    }

    private unsafe void Add(GameObject* native)
    {
        if (native == null || !seen.Add((nint)native)) return;
        bool source = native->ObjectKind == ObjectKind.Aetheryte ||
            (native->ObjectKind == ObjectKind.EventObj && ShardIds.Contains(native->BaseId));
        // HousingYardObject 131094 = Item 6600, Miniature Aetheryte.
        if (native->ObjectKind == ObjectKind.HousingEventObject)
            source |= ((HousingObject*)native)->HousingObjectId.Id == 131094;
        if (!source) return;
        var p = native->Position;
        if (float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z))
            sources.Add(new Source((nint)native, native->EntityId, new Vector3(p.X, p.Y, p.Z),
                native->ObjectKind == ObjectKind.Aetheryte && largeIds.Contains(native->BaseId), (nint)native->DrawObject));
    }

    // The destination belongs to one character; never share mutable exposure
    // results between Self, Players and Monsters.
    public IReadOnlyList<Hit> GetHits(float x, float y, float z, GrowthSettings settings, List<Hit> hits)
    {
        hits.Clear();
        var point = new Vector3(x, y, z);
        foreach (var source in sources)
        {
            float ratio = AetherExposure.HitRatio(Vector3.Distance(point, source.Position),
                settings.AetherProximityRange,
                source.Large ? settings.AetherLargeHitPercent : settings.AetherShardHitPercent);
            if (ratio > 0f) hits.Add(new Hit(source, ratio));
        }
        return hits;
    }
}

using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    public int ImportTechnologySnapshot(EntityUid uid, IEnumerable<string> technologies, TechnologyDatabaseComponent? database = null)
    {
        if (!Resolve(uid, ref database, false))
            return 0;
        var imported = 0;
        foreach (var id in technologies)
        {
            if (!PrototypeManager.TryIndex<TechnologyPrototype>(id, out _) || database.ResearchedTechnologies.Contains(id))
                continue;
            database.ResearchedTechnologies.Add(id);
            imported++;
        }
        if (imported == 0)
            return 0;
        RecalculateTechnologyState(uid, database);
        UpdateTechnologyCards(uid, database);
        Dirty(uid, database);
        return imported;
    }
}

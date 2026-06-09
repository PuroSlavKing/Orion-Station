// SPDX-License-Identifier: MIT

using Content.Server.Power.EntitySystems;
using Content.Server.Research.Components;
using Content.Shared._Orion.Research;
using Content.Shared._Orion.Research.Components;
using Content.Shared.Research.Components;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    private void InitializeSource()
    {
        SubscribeLocalEvent<ResearchPointSourceComponent, ResearchServerGetPointsPerSecondByTypeEvent>(OnGetPointsPerSecondByType);
    }

    private bool CanProduce(Entity<ResearchPointSourceComponent> source) => source.Comp.Active && this.IsPowered(source, EntityManager);

    private void OnGetPointsPerSecondByType(Entity<ResearchPointSourceComponent> source, ref ResearchServerGetPointsPerSecondByTypeEvent args)
    {
        if (TryComp<ResearchServerControlStatusComponent>(args.Server, out var status) && !status.GenerationEnabled)
            return;
        if (!CanProduce(source))
            return;
        if (source.Comp.RequiredInfrastructure != null &&
            (!TryComp<TechnologyDatabaseComponent>(args.Server, out var db) || !db.UnlockedInfrastructure.Contains(source.Comp.RequiredInfrastructure)))
            return;
        args.Points.Add(new ResearchPointAmount { Type = source.Comp.PointType, Amount = source.Comp.PointsPerSecond });
    }
}

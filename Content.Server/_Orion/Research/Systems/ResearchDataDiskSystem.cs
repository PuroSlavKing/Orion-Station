using System.Linq;
using Content.Server._Orion.Research.Components;
using Content.Server.Popups;
using Content.Server.Research.Systems;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Research.Components;

namespace Content.Server._Orion.Research.Systems;

public sealed class ResearchDataDiskSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly ResearchSystem _research = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ResearchDataDiskComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ResearchDataDiskComponent, ExaminedEvent>(OnExamined);
    }

    private void OnAfterInteract(EntityUid uid, ResearchDataDiskComponent component, AfterInteractEvent args)
    {
        if (!args.CanReach || args.Target is not { } target)
            return;

        if (!TryComp<ResearchServerComponent>(target, out var server) ||
            !TryComp<TechnologyDatabaseComponent>(target, out var database))
            return;

        if (component.HasDataSnapshot)
        {
            var imported = _research.ImportTechnologySnapshot(target, component.StoredTechnologies, database);
            _popupSystem.PopupEntity(Loc.GetString("research-disk-data-imported", ("count", imported)), target, args.User);
            _research.LogNetworkEvent(target, "disk", Loc.GetString("research-netlog-disk-imported", ("count", imported)), args.User);
            args.Handled = true;
            return;
        }

        component.HasDataSnapshot = true;
        component.StoredTechnologies = database.ResearchedTechnologies.Select(x => x.ToString()).ToList();
        component.SnapshotServerName = server.ServerName;
        Dirty(uid, component);
        _popupSystem.PopupEntity(Loc.GetString("research-disk-exported", ("count", component.StoredTechnologies.Count)), target, args.User);
        _research.LogNetworkEvent(target, "disk", Loc.GetString("research-netlog-disk-exported", ("count", component.StoredTechnologies.Count)), args.User);
        args.Handled = true;
    }

    private void OnExamined(EntityUid uid, ResearchDataDiskComponent component, ExaminedEvent args)
    {
        if (!component.HasDataSnapshot)
        {
            args.PushMarkup(Loc.GetString("research-disk-data-empty"));
            return;
        }

        args.PushMarkup(Loc.GetString("research-disk-data-examine",
            ("server", string.IsNullOrWhiteSpace(component.SnapshotServerName)
                ? Loc.GetString("research-disk-data-unknown-server")
                : component.SnapshotServerName),
            ("count", component.StoredTechnologies.Count)));
    }
}

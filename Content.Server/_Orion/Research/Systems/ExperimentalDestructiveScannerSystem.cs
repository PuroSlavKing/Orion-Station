using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.Research.Systems;
using Content.Shared._Orion.Research;
using Content.Shared._Orion.Research.Components;
using Content.Shared._Orion.Research.Prototypes;
using Content.Shared.Chat;
using Content.Shared.Item;
using Content.Shared.Research.Components;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Orion.Research.Systems;

public sealed class ExperimentalDestructiveScannerSystem : EntitySystem
{
    [Dependency] private readonly ResearchSystem _research = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly ChatSystem _chat = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ExperimentalDestructiveScannerComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<ExperimentalDestructiveScannerComponent, OpenResearchServerMenuMessage>(OnOpenServerMenu);
        SubscribeLocalEvent<ExperimentalDestructiveScannerComponent, ExperimentalDestructiveScannerPerformMessage>(OnPerform);
        SubscribeLocalEvent<ExperimentalDestructiveScannerComponent, ResearchServerPointsChangedEvent>(OnPointsChanged);
        SubscribeLocalEvent<ExperimentalDestructiveScannerComponent, ResearchServerPointTypeChangedEvent>(OnTypedPointsChanged);
        SubscribeLocalEvent<ExperimentalDestructiveScannerComponent, ResearchRegistrationChangedEvent>(OnRegistrationChanged);
        SubscribeLocalEvent<ExperimentalDestructiveScannerComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<ExperimentalDestructiveScannerComponent> entity, ref ComponentStartup args)
    {
        _container.EnsureContainer<Container>(entity, entity.Comp.ContainerId);
        SetVisualState(entity, ExperimentalDestructiveScannerVisualState.Idle);
    }

    private void OnUiOpened(Entity<ExperimentalDestructiveScannerComponent> entity, ref BoundUIOpenedEvent args)
        => UpdateUi(entity);

    private void OnOpenServerMenu(Entity<ExperimentalDestructiveScannerComponent> entity,
        ref OpenResearchServerMenuMessage args)
        => _ui.TryToggleUi(entity.Owner, ResearchClientUiKey.Key, args.Actor);

    private void OnPointsChanged(Entity<ExperimentalDestructiveScannerComponent> entity,
        ref ResearchServerPointsChangedEvent args)
    {
        if (_ui.IsUiOpen(entity.Owner, ExperimentalDestructiveScannerUiKey.Key))
            UpdateUi(entity);
    }

    private void OnTypedPointsChanged(Entity<ExperimentalDestructiveScannerComponent> entity,
        ref ResearchServerPointTypeChangedEvent args)
    {
        if (_ui.IsUiOpen(entity.Owner, ExperimentalDestructiveScannerUiKey.Key))
            UpdateUi(entity);
    }

    private void OnRegistrationChanged(Entity<ExperimentalDestructiveScannerComponent> entity,
        ref ResearchRegistrationChangedEvent args)
        => UpdateUi(entity);

    private void OnPerform(Entity<ExperimentalDestructiveScannerComponent> entity,
        ref ExperimentalDestructiveScannerPerformMessage args)
    {
        if (entity.Comp.IsProcessing)
        {
            Fail(entity, "research-machine-experimental-destructive-scanner-busy");
            return;
        }

        if (!TryResolveServer(entity.Owner, out var server))
        {
            entity.Comp.LastSubject = string.Empty;
            Fail(entity, "research-machine-common-no-server");
            return;
        }

        var transform = Transform(entity);
        var items = new List<EntityUid>();
        if (transform.GridUid is { } gridUid &&
            TryComp(gridUid, out MapGridComponent? grid) &&
            _maps.TryGetTileRef(gridUid, grid, transform.Coordinates, out var tile))
        {
            items = _lookup.GetLocalEntitiesIntersecting(tile, 0f)
                .Where(uid => uid != entity.Owner &&
                              HasComp<ItemComponent>(uid) &&
                              !HasComp<ResearchClientComponent>(uid) &&
                              !_container.TryGetContainingContainer(uid, out _))
                .Distinct()
                .ToList();
        }

        if (items.Count == 0)
        {
            entity.Comp.LastSubject = string.Empty;
            Fail(entity, "research-machine-experimental-destructive-scanner-no-items");
            return;
        }

        var itemContainer = _container.EnsureContainer<Container>(entity, entity.Comp.ContainerId);
        var scanned = items.Where(item => _container.Insert(item, itemContainer)).ToList();
        if (scanned.Count == 0)
        {
            Fail(entity, "research-machine-experimental-destructive-scanner-no-items");
            return;
        }

        entity.Comp.IsProcessing = true;
        entity.Comp.LastSubject = string.Join(", ", scanned.Select(Name));
        entity.Comp.LastResult = Loc.GetString("research-machine-experimental-destructive-scanner-processing",
            ("count", scanned.Count));
        SetVisualState(entity, ExperimentalDestructiveScannerVisualState.Down);
        UpdateUi(entity);

        _research.LogNetworkEvent(server,
            "experimental-destructive-scanner",
            Loc.GetString("research-netlog-experimental-destructive-scanner-started",
                ("count", scanned.Count),
                ("user", _research.GetResearchLogUserName(args.Actor))),
            args.Actor);

        Timer.Spawn(entity.Comp.CapsuleStepDuration, () =>
        {
            if (!TerminatingOrDeleted(entity) && entity.Comp.IsProcessing)
                SetVisualState(entity, ExperimentalDestructiveScannerVisualState.Scanning);
        });
        Timer.Spawn(entity.Comp.ScanDuration, () => CompleteScan(entity, server, scanned, args.Actor));
    }

    private void CompleteScan(Entity<ExperimentalDestructiveScannerComponent> entity,
        EntityUid server,
        List<EntityUid> scanned,
        EntityUid? user)
    {
        if (TerminatingOrDeleted(entity))
            return;

        var changedAny = false;
        var completed = new HashSet<string>();
        foreach (var item in scanned)
        {
            if (TerminatingOrDeleted(item))
                continue;

            if (!_research.TryProgressExperimentsWithEntity(server,
                    item,
                    user,
                    out var changed,
                    out var completedForItem,
                    out _,
                    ExperimentSourceFlags.MachineScanner))
                continue;

            changedAny |= changed;
            completed.UnionWith(completedForItem);
        }

        entity.Comp.IsProcessing = false;
        SetVisualState(entity, ExperimentalDestructiveScannerVisualState.Up);
        Timer.Spawn(entity.Comp.CapsuleStepDuration, () =>
        {
            if (TerminatingOrDeleted(entity) || entity.Comp.IsProcessing)
                return;

            var container = _container.EnsureContainer<Container>(entity, entity.Comp.ContainerId);
            _container.EmptyContainer(container, true, Transform(entity).Coordinates);
            SetVisualState(entity, ExperimentalDestructiveScannerVisualState.Idle);
        });

        entity.Comp.LastResult = completed.Count > 0
            ? Loc.GetString("research-machine-experimental-destructive-scanner-completed-named",
                ("count", completed.Count),
                ("experiments", string.Join(", ", completed.Select(GetExperimentName))))
            : changedAny
                ? Loc.GetString("research-machine-experimental-destructive-scanner-progressed")
                : Loc.GetString("research-machine-experimental-destructive-scanner-no-matching-experiment");

        _chat.TrySendInGameICMessage(entity.Owner,
            Loc.GetString("research-machine-experimental-destructive-scanner-chat-result",
                ("result", entity.Comp.LastResult)),
            InGameICChatType.Speak,
            false);
        _audio.PlayPvs(changedAny || completed.Count > 0 ? entity.Comp.SuccessSound : entity.Comp.FailureSound,
            entity,
            entity.Comp.AudioParams);
        _research.LogNetworkEvent(server,
            "experimental-destructive-scanner",
            Loc.GetString("research-netlog-experimental-destructive-scanner-result",
                ("completed", completed.Count),
                ("progressed", Loc.GetString(changedAny
                    ? "research-netlog-experimental-destructive-scanner-progress-yes"
                    : "research-netlog-experimental-destructive-scanner-progress-no")),
                ("user", _research.GetResearchLogUserName(user))),
            user);
        UpdateUi(entity);
    }

    private void Fail(Entity<ExperimentalDestructiveScannerComponent> entity, string key)
    {
        entity.Comp.LastResult = Loc.GetString(key);
        _audio.PlayPvs(entity.Comp.FailureSound, entity, entity.Comp.AudioParams);
        UpdateUi(entity);
    }

    private string GetExperimentName(string id)
        => _prototypes.TryIndex<ResearchExperimentPrototype>(id, out var prototype)
            ? Loc.GetString(prototype.Name)
            : id;

    private bool TryResolveServer(EntityUid uid, out EntityUid server)
    {
        server = EntityUid.Invalid;
        if (TryComp<ResearchClientComponent>(uid, out var client) && client.Server is { } selected)
        {
            server = selected;
            return true;
        }

        var fallback = _research.GetServers(uid).OrderBy(value => value.Comp.Id).FirstOrDefault();
        if (fallback.Owner == EntityUid.Invalid)
            return false;

        server = fallback.Owner;
        return true;
    }

    private void SetVisualState(Entity<ExperimentalDestructiveScannerComponent> entity,
        ExperimentalDestructiveScannerVisualState state)
        => _appearance.SetData(entity.Owner, ExperimentalDestructiveScannerVisuals.State, state);

    private void UpdateUi(Entity<ExperimentalDestructiveScannerComponent> entity)
    {
        string? serverName = null;
        var balances = new List<ResearchPointAmount>();
        var experiments = new List<ResearchMachineExperimentUiData>();
        if (_research.TryGetClientServer(entity.Owner, out var serverUid, out var server))
        {
            serverName = server.ServerName;
            balances = server.PointBalances
                .Select(value => new ResearchPointAmount { Type = value.Type, Amount = value.Amount })
                .ToList();

            if (TryComp<TechnologyDatabaseComponent>(serverUid, out var database))
            {
                foreach (var id in database.ActiveExperiments)
                {
                    if (!_prototypes.TryIndex<ResearchExperimentPrototype>(id, out var prototype) || prototype.Hidden)
                        continue;

                    var progress = database.ExperimentProgress.FirstOrDefault(value => value.ExperimentId == id);
                    experiments.Add(ResearchExperimentUiData.Create(prototype, progress, _prototypes));
                }
            }
        }

        var status = entity.Comp.IsProcessing
            ? Loc.GetString("research-machine-experimental-destructive-scanner-state-processing")
            : Loc.GetString("research-machine-common-none");
        _ui.SetUiState(entity.Owner,
            ExperimentalDestructiveScannerUiKey.Key,
            new ExperimentalDestructiveScannerBoundInterfaceState(serverName,
                balances,
                entity.Comp.LastSubject,
                entity.Comp.LastResult,
                experiments,
                status));
    }
}

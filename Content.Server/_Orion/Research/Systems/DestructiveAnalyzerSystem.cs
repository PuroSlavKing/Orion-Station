using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.Research.Systems;
using Content.Shared._Orion.Research;
using Content.Shared._Orion.Research.Components;
using Content.Shared.Chat;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Stacks;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Orion.Research.Systems;

public sealed class DestructiveAnalyzerSystem : EntitySystem
{
    [Dependency] private readonly ResearchSystem _research = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly ILocalizationManager _localization = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ChatSystem _chat = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DestructiveAnalyzerComponent, AfterInteractUsingEvent>(OnAfterInteractUsing);
        SubscribeLocalEvent<DestructiveAnalyzerComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<DestructiveAnalyzerComponent, OpenResearchServerMenuMessage>(OnOpenServerMenu);
        SubscribeLocalEvent<DestructiveAnalyzerComponent, DestructiveAnalyzerSelectMethodMessage>(OnSelectMethod);
        SubscribeLocalEvent<DestructiveAnalyzerComponent, DestructiveAnalyzerRunMessage>(OnRun);
        SubscribeLocalEvent<DestructiveAnalyzerComponent, DestructiveAnalyzerEjectMessage>(OnEject);
        SubscribeLocalEvent<DestructiveAnalyzerComponent, ResearchServerPointsChangedEvent>(OnPointsChanged);
        SubscribeLocalEvent<DestructiveAnalyzerComponent, ResearchServerPointTypeChangedEvent>(OnTypedPointsChanged);
        SubscribeLocalEvent<DestructiveAnalyzerComponent, ResearchRegistrationChangedEvent>(OnRegistrationChanged);
        SubscribeLocalEvent<DestructiveAnalyzerComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(Entity<DestructiveAnalyzerComponent> entity, ref ComponentStartup args)
    {
        _container.EnsureContainer<Container>(entity, entity.Comp.ContainerId);
        SetVisualState(entity, DestructiveAnalyzerVisualState.Idle);
    }

    private void OnUiOpened(Entity<DestructiveAnalyzerComponent> entity, ref BoundUIOpenedEvent args)
        => UpdateUi(entity);

    private void OnOpenServerMenu(Entity<DestructiveAnalyzerComponent> entity,
        ref OpenResearchServerMenuMessage args)
        => _ui.TryToggleUi(entity.Owner, ResearchClientUiKey.Key, args.Actor);

    private void OnPointsChanged(Entity<DestructiveAnalyzerComponent> entity,
        ref ResearchServerPointsChangedEvent args)
    {
        if (_ui.IsUiOpen(entity.Owner, DestructiveAnalyzerUiKey.Key))
            UpdateUi(entity);
    }

    private void OnTypedPointsChanged(Entity<DestructiveAnalyzerComponent> entity,
        ref ResearchServerPointTypeChangedEvent args)
    {
        if (_ui.IsUiOpen(entity.Owner, DestructiveAnalyzerUiKey.Key))
            UpdateUi(entity);
    }

    private void OnRegistrationChanged(Entity<DestructiveAnalyzerComponent> entity,
        ref ResearchRegistrationChangedEvent args)
        => UpdateUi(entity);

    private void OnAfterInteractUsing(Entity<DestructiveAnalyzerComponent> entity,
        ref AfterInteractUsingEvent args)
    {
        if (args.Handled || entity.Comp.InsertedItem != null)
            return;

        var container = _container.EnsureContainer<Container>(entity, entity.Comp.ContainerId);
        if (!_container.Insert(args.Used, container))
            return;

        entity.Comp.InsertedItem = args.Used;
        entity.Comp.LastItemAnalyzed = false;
        entity.Comp.IsProcessing = false;
        entity.Comp.LastSubject = Name(args.Used);
        entity.Comp.LastResult = Loc.GetString("research-machine-destructive-item-loaded");
        entity.Comp.SelectedMethod = null;
        SetVisualState(entity, DestructiveAnalyzerVisualState.Inserting);
        Timer.Spawn(entity.Comp.InsertAnimationDuration, () =>
        {
            if (!TerminatingOrDeleted(entity) && entity.Comp.InsertedItem == args.Used)
                SetVisualState(entity, DestructiveAnalyzerVisualState.Loaded);
        });
        UpdateUi(entity);
        args.Handled = true;
    }

    private void OnSelectMethod(Entity<DestructiveAnalyzerComponent> entity,
        ref DestructiveAnalyzerSelectMethodMessage args)
    {
        entity.Comp.SelectedMethod = args.MethodId;
        UpdateUi(entity);
    }

    private void OnRun(Entity<DestructiveAnalyzerComponent> entity, ref DestructiveAnalyzerRunMessage args)
    {
        if (entity.Comp.IsProcessing)
        {
            Fail(entity, "research-machine-destructive-busy");
            return;
        }

        if (entity.Comp.LastItemAnalyzed)
        {
            Fail(entity, "research-machine-destructive-already-analyzed");
            return;
        }

        if (entity.Comp.InsertedItem is not { } item)
        {
            Fail(entity, "research-machine-destructive-no-item");
            return;
        }

        if (!TryResolveServer(entity.Owner, out var server))
        {
            Fail(entity, "research-machine-common-no-server");
            return;
        }

        if (!_container.TryGetContainingContainer(item, out var containing) || containing.Owner != entity.Owner)
        {
            ResetInsertedItem(entity);
            Fail(entity, "research-machine-destructive-no-item");
            return;
        }

        if (TryComp<MobStateComponent>(item, out var mobState) && mobState.CurrentState == MobState.Alive)
        {
            Fail(entity, "research-machine-destructive-living-subject-blocked");
            return;
        }

        string rewardSummary;
        if (TryComp<ResearchAnalyzableComponent>(item, out var analyzable))
        {
            if (!TryRunAnalyzableMethod(entity, item, server, analyzable, args.Actor, out rewardSummary))
                return;
        }
        else if (!TryRunDiscoveryRevealMethod(entity, item, server, args.Actor, out rewardSummary))
        {
            return;
        }

        entity.Comp.IsProcessing = true;
        entity.Comp.LastResult = Loc.GetString("research-machine-destructive-processing", ("count", 1));
        SetVisualState(entity, DestructiveAnalyzerVisualState.Deconstructing);
        UpdateUi(entity);
        Timer.Spawn(entity.Comp.DeconstructAnimationDuration,
            () => CompleteAnalysis(entity, item, rewardSummary));
    }

    private bool TryRunAnalyzableMethod(Entity<DestructiveAnalyzerComponent> entity,
        EntityUid item,
        EntityUid server,
        ResearchAnalyzableComponent analyzable,
        EntityUid actor,
        out string summary)
    {
        summary = string.Empty;
        var methods = GetAvailableMethods(analyzable);
        var method = entity.Comp.SelectedMethod;
        if (string.IsNullOrWhiteSpace(method) || !methods.Contains(method))
            method = methods.FirstOrDefault();
        entity.Comp.SelectedMethod = method;

        if (string.IsNullOrWhiteSpace(method) || !analyzable.MethodPointRewards.TryGetValue(method, out var rewards))
        {
            Fail(entity, "research-machine-destructive-unsupported-method");
            return false;
        }

        var multiplier = TryComp<StackComponent>(item, out var stack) ? stack.Count : 1;
        foreach (var reward in rewards)
            _research.ModifyServerPoints(server, reward.Type, reward.Amount * multiplier);
        foreach (var technology in analyzable.RevealTechnologies)
            _research.RevealTechnology(server, technology, actor);
        foreach (var technology in analyzable.UnlockTechnologies)
            _research.AddTechnology(server, technology);
        foreach (var action in analyzable.ExperimentActions)
            _research.TryProgressExperimentsByAction(server, action);
        if (!string.IsNullOrWhiteSpace(analyzable.DiscoveryTrigger))
            _research.TriggerDiscovery(server, analyzable.DiscoveryTrigger);

        summary = BuildRewardSummary(rewards, multiplier, analyzable);
        _research.LogNetworkEvent(server,
            "destructive-analyzer",
            Loc.GetString("research-netlog-destructive-analysis-result",
                ("method", LocalizeMethod(method)),
                ("subject", Name(item)),
                ("result", summary),
                ("user", _research.GetResearchLogUserName(actor))),
            actor);
        return true;
    }

    private bool TryRunDiscoveryRevealMethod(Entity<DestructiveAnalyzerComponent> entity,
        EntityUid item,
        EntityUid server,
        EntityUid actor,
        out string summary)
    {
        summary = string.Empty;
        var methods = GetDiscoveryRevealMethods(item, server);
        var method = entity.Comp.SelectedMethod;
        if (string.IsNullOrWhiteSpace(method) || !methods.Contains(method))
            method = methods.FirstOrDefault();
        entity.Comp.SelectedMethod = method;

        if (string.IsNullOrWhiteSpace(method) || !TryGetRevealTechnology(method, out var technologyId))
        {
            Fail(entity, "research-machine-destructive-last-result-invalid-item");
            return false;
        }

        _research.RevealTechnology(server, technologyId, actor);
        summary = GetTechnologyName(technologyId);
        _research.LogNetworkEvent(server,
            "destructive-analyzer",
            Loc.GetString("research-netlog-destructive-analysis-result",
                ("method", LocalizeMethod(method)),
                ("subject", Name(item)),
                ("result", Loc.GetString("research-machine-destructive-result-revealed-tech", ("technology", summary))),
                ("user", _research.GetResearchLogUserName(actor))),
            actor);
        return true;
    }

    private void CompleteAnalysis(Entity<DestructiveAnalyzerComponent> entity,
        EntityUid item,
        string rewardSummary)
    {
        if (TerminatingOrDeleted(entity))
            return;

        entity.Comp.IsProcessing = false;
        if (TerminatingOrDeleted(item))
        {
            ResetInsertedItem(entity);
            UpdateUi(entity);
            return;
        }

        entity.Comp.LastItemAnalyzed = true;
        entity.Comp.LastResult = Loc.GetString("research-machine-destructive-last-result-success",
            ("result", rewardSummary));
        QueueDel(item);
        entity.Comp.InsertedItem = null;
        SetVisualState(entity, DestructiveAnalyzerVisualState.Idle);
        _audio.PlayPvs(entity.Comp.SuccessSound, entity, entity.Comp.AudioParams);
        _popup.PopupEntity(Loc.GetString("research-destructive-analyzer-success"), entity, PopupType.SmallCaution);
        _chat.TrySendInGameICMessage(entity.Owner,
            Loc.GetString("research-machine-destructive-chat-result", ("result", rewardSummary)),
            InGameICChatType.Speak,
            false);
        UpdateUi(entity);
    }

    private void OnEject(Entity<DestructiveAnalyzerComponent> entity, ref DestructiveAnalyzerEjectMessage args)
    {
        if (entity.Comp.IsProcessing || entity.Comp.InsertedItem is not { } item)
            return;

        var container = _container.EnsureContainer<Container>(entity, entity.Comp.ContainerId);
        if (!_container.Remove(item, container))
            return;

        if (!_hands.TryPickupAnyHand(args.Actor, item))
            _transform.SetCoordinates(item, Transform(entity).Coordinates);
        ResetInsertedItem(entity);
        UpdateUi(entity);
    }

    private void ResetInsertedItem(Entity<DestructiveAnalyzerComponent> entity)
    {
        entity.Comp.InsertedItem = null;
        entity.Comp.SelectedMethod = null;
        entity.Comp.LastResult = string.Empty;
        entity.Comp.LastItemAnalyzed = false;
        SetVisualState(entity, DestructiveAnalyzerVisualState.Idle);
    }

    private void Fail(Entity<DestructiveAnalyzerComponent> entity, string key)
    {
        entity.Comp.LastResult = Loc.GetString(key);
        _audio.PlayPvs(entity.Comp.FailureSound, entity, entity.Comp.AudioParams);
        UpdateUi(entity);
    }

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

    private void SetVisualState(Entity<DestructiveAnalyzerComponent> entity, DestructiveAnalyzerVisualState state)
        => _appearance.SetData(entity.Owner, DestructiveAnalyzerVisuals.State, state);

    private static List<string> GetAvailableMethods(ResearchAnalyzableComponent analyzable)
    {
        if (analyzable.SupportedMethods.Count > 0)
            return analyzable.SupportedMethods.Where(analyzable.MethodPointRewards.ContainsKey).ToList();
        return analyzable.MethodPointRewards.Keys.ToList();
    }

    private string BuildRewardSummary(List<ResearchPointAmount> rewards,
        int multiplier,
        ResearchAnalyzableComponent analyzable)
    {
        var totals = rewards.GroupBy(reward => reward.Type, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(reward => reward.Amount * multiplier));
        var segments = new List<string>();
        if (totals.Count > 0)
        {
            var points = totals.OrderBy(value => value.Key)
                .Select(value => Loc.GetString("research-machine-destructive-result-points-entry",
                    ("type", LocalizePointType(value.Key)),
                    ("amount", value.Value)));
            segments.Add(Loc.GetString("research-machine-destructive-result-points",
                ("points", string.Join(", ", points))));
        }
        if (analyzable.RevealTechnologies.Count > 0)
            segments.Add(Loc.GetString("research-machine-destructive-result-revealed-tech",
                ("technology", string.Join(", ", analyzable.RevealTechnologies.Select(GetTechnologyName)))));
        if (analyzable.UnlockTechnologies.Count > 0)
            segments.Add(Loc.GetString("research-machine-destructive-result-unlocked-tech",
                ("technology", string.Join(", ", analyzable.UnlockTechnologies.Select(GetTechnologyName)))));
        return segments.Count == 0
            ? Loc.GetString("research-machine-destructive-result-generic")
            : string.Join(", ", segments);
    }

    private string GetTechnologyName(string id)
        => _prototypes.TryIndex<TechnologyPrototype>(id, out var technology)
            ? Loc.GetString(technology.Name)
            : id;

    private string LocalizePointType(string type)
    {
        var key = $"research-point-type-{type.ToLowerInvariant()}";
        return _localization.TryGetString(key, out var value) ? value : type;
    }

    private string LocalizeMethod(string method)
    {
        if (TryGetRevealTechnology(method, out var technology))
            return Loc.GetString("research-machine-destructive-method-reveal-technology",
                ("technology", GetTechnologyName(technology)));
        var key = $"research-machine-destructive-method-{method.ToLowerInvariant()}";
        return _localization.TryGetString(key, out var value)
            ? value
            : Loc.GetString("research-machine-destructive-method-unknown");
    }

    private static bool TryGetRevealTechnology(string method, out string technology)
    {
        const string prefix = "reveal:";
        if (!method.StartsWith(prefix, StringComparison.Ordinal))
        {
            technology = string.Empty;
            return false;
        }
        technology = method[prefix.Length..];
        return !string.IsNullOrWhiteSpace(technology);
    }

    private List<string> GetDiscoveryRevealMethods(EntityUid item, EntityUid server)
        => _research.GetHiddenTechnologiesForRequiredItem(server, item)
            .Select(technology => $"reveal:{technology}")
            .ToList();

    private void UpdateUi(Entity<DestructiveAnalyzerComponent> entity)
    {
        string? serverName = null;
        var balances = new List<ResearchPointAmount>();
        var methods = new List<string>();

        if (_research.TryGetClientServer(entity.Owner, out _, out var server))
        {
            serverName = server.ServerName;
            balances = server.PointBalances
                .Select(value => new ResearchPointAmount { Type = value.Type, Amount = value.Amount })
                .ToList();
        }

        if (entity.Comp.InsertedItem is { } item)
        {
            if (TryComp<ResearchAnalyzableComponent>(item, out var analyzable))
                methods = GetAvailableMethods(analyzable);
            else if (_research.TryGetClientServer(entity.Owner, out var serverUid, out _))
                methods = GetDiscoveryRevealMethods(item, serverUid.Value);

            if (entity.Comp.SelectedMethod == null || !methods.Contains(entity.Comp.SelectedMethod))
                entity.Comp.SelectedMethod = methods.FirstOrDefault();
        }

        _ui.SetUiState(entity.Owner,
            DestructiveAnalyzerUiKey.Key,
            new DestructiveAnalyzerBoundInterfaceState(serverName,
                balances,
                entity.Comp.LastSubject,
                entity.Comp.LastResult,
                entity.Comp.InsertedItem is { } inserted ? Name(inserted) : null,
                entity.Comp.InsertedItem is { } netItem ? GetNetEntity(netItem) : null,
                entity.Comp.SelectedMethod,
                methods));
    }
}

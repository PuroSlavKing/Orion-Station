using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Server.Research.Systems;
using Content.Shared._Orion.Research.Components;
using Content.Shared._Orion.Research.Prototypes;
using Content.Shared.Chat;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Research.Components;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Orion.Research.Systems;

public sealed class ExperiScannerSystem : EntitySystem
{
    [Dependency] private readonly ResearchSystem _research = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly ChatSystem _chat = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ExperiScannerComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<ExperiScannerComponent, OpenResearchServerMenuMessage>(OnOpenServerMenu);
        SubscribeLocalEvent<ExperiScannerComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<ExperiScannerComponent, ResearchRegistrationChangedEvent>(OnRegistrationChanged);
    }

    private void OnUiOpened(Entity<ExperiScannerComponent> entity, ref BoundUIOpenedEvent args) => UpdateUi(entity);
    private void OnRegistrationChanged(Entity<ExperiScannerComponent> entity, ref ResearchRegistrationChangedEvent args) => UpdateUi(entity);

    private void OnOpenServerMenu(Entity<ExperiScannerComponent> entity, ref OpenResearchServerMenuMessage args)
        => _ui.TryToggleUi(entity.Owner, ResearchClientUiKey.Key, args.Actor);

    private void OnAfterInteract(Entity<ExperiScannerComponent> entity, ref AfterInteractEvent args)
    {
        if (args.Handled || args.Target is not { } target ||
            !_interaction.InRangeUnobstructed(args.User, target, range: entity.Comp.ScanRange))
            return;

        args.Handled = true;
        if (!TryResolveServer(entity.Owner, out var server))
        {
            Fail(entity, args.User, "research-experi-scanner-no-server");
            return;
        }

        if (!_research.TryProgressExperimentsWithEntity(server,
                target,
                args.User,
                out var changed,
                out var completed,
                out var result,
                ExperimentSourceFlags.HandheldScanner))
        {
            var key = result switch
            {
                ExperimentProgressAttemptResult.NoSourceCompatibleExperiment => "research-experi-scanner-no-compatible-experiments",
                ExperimentProgressAttemptResult.AlreadyScanned => "research-experi-scanner-already-scanned",
                _ => "research-experi-scanner-no-match",
            };
            Fail(entity, args.User, key);
            return;
        }

        var targetName = Name(target);
        entity.Comp.LastResult = Loc.GetString("research-experi-scanner-progress", ("target", targetName));
        _audio.PlayPvs(entity.Comp.SuccessSound, entity, AudioParams.Default.WithVolume(-2f));
        _popup.PopupEntity(entity.Comp.LastResult, entity, args.User, PopupType.SmallCaution);
        _chat.TrySendInGameICMessage(entity.Owner, entity.Comp.LastResult, InGameICChatType.Speak, false);
        UpdateUi(entity);

        _research.LogNetworkEvent(server,
            "experi-scanner",
            Loc.GetString("research-netlog-experi-scanner-scan",
                ("user", _research.GetResearchLogUserName(args.User)),
                ("scanner", Name(entity.Owner)),
                ("target", targetName),
                ("completed", completed.Count),
                ("progressed", Loc.GetString(changed
                    ? "research-netlog-experimental-destructive-scanner-progress-yes"
                    : "research-netlog-experimental-destructive-scanner-progress-no"))),
            args.User);
    }

    private void Fail(Entity<ExperiScannerComponent> entity, EntityUid user, string message)
    {
        entity.Comp.LastResult = Loc.GetString(message);
        _audio.PlayPvs(entity.Comp.FailureSound, entity, AudioParams.Default.WithVolume(-4f));
        _popup.PopupEntity(entity.Comp.LastResult, entity, user);
        UpdateUi(entity);
    }

    private void UpdateUi(Entity<ExperiScannerComponent> entity)
    {
        string? serverName = null;
        var experiments = new List<ResearchMachineExperimentUiData>();
        if (_research.TryGetClientServer(entity.Owner, out var serverUid, out var server) &&
            TryComp<TechnologyDatabaseComponent>(serverUid, out var database))
        {
            serverName = server.ServerName;
            foreach (var id in database.ActiveExperiments)
            {
                if (!_prototypes.TryIndex<ResearchExperimentPrototype>(id, out var experiment) ||
                    experiment.Hidden ||
                    !experiment.SupportedSources.HasFlag(ExperimentSourceFlags.HandheldScanner))
                    continue;
                var progress = database.ExperimentProgress.FirstOrDefault(value => value.ExperimentId == id);
                experiments.Add(ResearchExperimentUiData.Create(experiment, progress, _prototypes));
            }
        }

        _ui.SetUiState(entity.Owner,
            ExperiScannerUiKey.Key,
            new ExperiScannerBoundInterfaceState(serverName, experiments, entity.Comp.LastResult));
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
}

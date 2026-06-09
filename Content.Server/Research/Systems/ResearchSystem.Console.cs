// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Common.Research;
using Content.Server.Power.EntitySystems;
using Content.Server.Research.Components;
using Content.Shared._Orion.Research;
using Content.Shared._Orion.Research.Prototypes;
using Content.Shared.Access.Components;
using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Research.Systems;
using Content.Shared.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    [Dependency] private readonly EmagSystem _emag = default!;

    private void InitializeConsole()
    {
        SubscribeLocalEvent<ResearchConsoleComponent, ConsoleUnlockTechnologyMessage>(OnConsoleUnlock);
        SubscribeLocalEvent<ResearchConsoleComponent, BeforeActivatableUIOpenEvent>(OnConsoleBeforeUiOpened);
        SubscribeLocalEvent<ResearchConsoleComponent, ResearchServerPointsChangedEvent>(OnPointsChanged);
        SubscribeLocalEvent<ResearchConsoleComponent, ResearchServerPointTypeChangedEvent>(OnTypedPointsChanged);
        SubscribeLocalEvent<ResearchConsoleComponent, ResearchRegistrationChangedEvent>(OnConsoleRegistrationChanged);
        SubscribeLocalEvent<ResearchConsoleComponent, TechnologyDatabaseModifiedEvent>(OnConsoleDatabaseModified);
        SubscribeLocalEvent<ResearchConsoleComponent, TechnologyDatabaseSynchronizedEvent>(OnConsoleDatabaseSynchronized);
        SubscribeLocalEvent<ResearchConsoleComponent, GotEmaggedEvent>(OnEmagged);
    }

    private void OnConsoleUnlock(EntityUid uid,
        ResearchConsoleComponent component,
        ConsoleUnlockTechnologyMessage args)
    {
        if (!this.IsPowered(uid, EntityManager) ||
            !PrototypeManager.TryIndex<TechnologyPrototype>(args.Id, out var technology))
            return;

        if (TryComp<AccessReaderComponent>(uid, out var access) &&
            !_accessReader.IsAllowed(args.Actor, uid, access))
        {
            _popup.PopupEntity(Loc.GetString("research-console-no-access-popup"), args.Actor);
            return;
        }

        if (!UnlockTechnology(uid, technology, args.Actor))
        {
            _popup.PopupEntity(Loc.GetString("research-console-unlock-failed-popup"), args.Actor);
            return;
        }

        if (!_emag.CheckFlag(uid, EmagType.Interaction) && technology.AnnounceOnUnlock)
        {
            var cost = FormatResearchPointAmounts(GetPrototypePointCosts(technology));
            var message = Loc.GetString("research-console-unlock-technology-radio-broadcast",
                ("technology", Loc.GetString(technology.Name)),
                ("amount", cost));
            var channels = technology.AnnounceChannels.Count > 0
                ? technology.AnnounceChannels
                : [component.AnnouncementChannel];
            foreach (var channel in channels)
                _radio.SendRadioMessage(uid, message, channel, uid, escapeMarkup: false);
        }

        SyncClientWithServer(uid);
        UpdateConsoleInterface(uid, component);
    }

    private void OnConsoleBeforeUiOpened(EntityUid uid,
        ResearchConsoleComponent component,
        BeforeActivatableUIOpenEvent args)
    {
        SyncClientWithServer(uid);
        UpdateConsoleInterface(uid, component);
    }

    private void UpdateConsoleInterface(EntityUid uid,
        ResearchConsoleComponent? component = null,
        ResearchClientComponent? client = null)
    {
        if (!Resolve(uid, ref component, ref client, false))
            return;

        var points = 0;
        var researches = new Dictionary<string, ResearchAvailability>();
        var visible = new List<ProtoId<TechnologyPrototype>>();
        var available = new List<ProtoId<TechnologyPrototype>>();
        var researched = new List<ProtoId<TechnologyPrototype>>();
        var completedExperiments = new List<string>();
        var experiments = new List<ResearchConsoleExperimentData>();
        var locks = new Dictionary<string, ResearchTechnologyLockReason>();
        var networkId = string.Empty;
        var balances = new List<ResearchPointAmount>();
        var logs = new List<ResearchLogEntry>();

        if (TryGetClientServer(uid, out var serverUid, out var server, client) &&
            TryComp<TechnologyDatabaseComponent>(serverUid, out var database))
        {
            points = client.ConnectedToServer ? server.Points : 0;
            networkId = server.NetworkId;
            balances = server.PointBalances.Select(ClonePointAmountForUi).ToList();
            logs = new List<ResearchLogEntry>(server.Logs);
            visible = new(database.VisibleTechnologies);
            available = new(database.AvailableTechnologies);
            researched = new(database.ResearchedTechnologies);
            completedExperiments = new(database.CompletedExperiments);

            var availableSet = new HashSet<ProtoId<TechnologyPrototype>>(available);
            var researchedSet = new HashSet<ProtoId<TechnologyPrototype>>(researched);
            foreach (var technologyId in visible)
            {
                if (!PrototypeManager.TryIndex<TechnologyPrototype>(technologyId, out var technology))
                    continue;

                var state = researchedSet.Contains(technologyId)
                    ? ResearchAvailability.Researched
                    : availableSet.Contains(technologyId) && HasSufficientPoints(serverUid.Value,
                        GetTechnologyFinalPointCosts(database, technology), server)
                        ? ResearchAvailability.Available
                        : availableSet.Contains(technologyId)
                            ? ResearchAvailability.Unavailable
                            : ResearchAvailability.PrereqsMet;
                researches[technology.ID] = state;
            }

            foreach (var technology in PrototypeManager.EnumeratePrototypes<TechnologyPrototype>())
            {
                if (!database.SupportedDisciplines.Contains(technology.Discipline))
                    continue;

                var reason = SharedResearchSystem.GetTechnologyLockReason(database, technology);
                if (reason == ResearchTechnologyLockReason.None &&
                    !HasSufficientPoints(serverUid.Value, GetTechnologyFinalPointCosts(database, technology), server))
                    reason = ResearchTechnologyLockReason.InsufficientPoints;
                locks[technology.ID] = reason;
            }

            experiments = BuildExperimentUiData(database);
        }

        _uiSystem.SetUiState(uid,
            ResearchConsoleUiKey.Key,
            new ResearchConsoleBoundInterfaceState(points,
                researches,
                visible,
                available,
                researched,
                completedExperiments,
                experiments,
                locks,
                networkId,
                balances,
                logs));
    }

    private List<ResearchConsoleExperimentData> BuildExperimentUiData(TechnologyDatabaseComponent database)
    {
        var result = new List<ResearchConsoleExperimentData>();
        foreach (var experiment in PrototypeManager.EnumeratePrototypes<ResearchExperimentPrototype>())
        {
            if (experiment.Hidden)
                continue;

            var progress = database.ExperimentProgress.FirstOrDefault(value => value.ExperimentId == experiment.ID);
            var state = database.SkippedExperiments.Contains(experiment.ID)
                ? ResearchExperimentState.Skipped
                : database.CompletedExperiments.Contains(experiment.ID)
                    ? ResearchExperimentState.Completed
                    : database.ActiveExperiments.Contains(experiment.ID)
                        ? ResearchExperimentState.Active
                        : database.AvailableExperiments.Contains(experiment.ID)
                            ? ResearchExperimentState.Available
                            : ResearchExperimentState.Unavailable;

            if (state == ResearchExperimentState.Unavailable)
                continue;

            result.Add(new ResearchConsoleExperimentData(experiment.ID,
                progress.Progress,
                progress.Target > 0 ? progress.Target : Math.Max(1, experiment.Objective.Target),
                state));
        }

        return result.OrderByDescending(value => value.State == ResearchExperimentState.Active)
            .ThenBy(value => value.Id)
            .ToList();
    }

    private void OnPointsChanged(EntityUid uid,
        ResearchConsoleComponent component,
        ref ResearchServerPointsChangedEvent args)
    {
        if (_uiSystem.IsUiOpen(uid, ResearchConsoleUiKey.Key))
            UpdateConsoleInterface(uid, component);
    }

    private void OnTypedPointsChanged(EntityUid uid,
        ResearchConsoleComponent component,
        ref ResearchServerPointTypeChangedEvent args)
    {
        if (_uiSystem.IsUiOpen(uid, ResearchConsoleUiKey.Key))
            UpdateConsoleInterface(uid, component);
    }

    private void OnConsoleRegistrationChanged(EntityUid uid,
        ResearchConsoleComponent component,
        ref ResearchRegistrationChangedEvent args)
    {
        SyncClientWithServer(uid);
        UpdateConsoleInterface(uid, component);
    }

    private void OnConsoleDatabaseModified(EntityUid uid,
        ResearchConsoleComponent component,
        ref TechnologyDatabaseModifiedEvent args)
    {
        SyncClientWithServer(uid);
        UpdateConsoleInterface(uid, component);
    }

    private void OnConsoleDatabaseSynchronized(EntityUid uid,
        ResearchConsoleComponent component,
        ref TechnologyDatabaseSynchronizedEvent args)
    {
        UpdateConsoleInterface(uid, component);
    }

    private void OnEmagged(Entity<ResearchConsoleComponent> entity, ref GotEmaggedEvent args)
    {
        if (_emag.CompareFlag(args.Type, EmagType.Interaction) &&
            !_emag.CheckFlag(entity, EmagType.Interaction))
            args.Handled = true;
    }

    private static ResearchPointAmount ClonePointAmountForUi(ResearchPointAmount source)
        => new() { Type = source.Type, Amount = source.Amount };
}

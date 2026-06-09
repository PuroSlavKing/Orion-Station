// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Shared.Fishing.Components;
using Content.Server.Chat.Systems;
using Content.Server.Research.Components;
using Content.Shared._EinsteinEngines.Silicon.Components;
using Content.Shared._Orion.Research;
using Content.Shared._Orion.Research.Components;
using Content.Shared._Orion.Research.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chat;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage.Components;
using Content.Shared.Explosion.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Research.Components;
using Content.Shared.Tag;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    [Dependency] private readonly TagSystem _tag = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;
    [Dependency] private readonly ChatSystem _chat = default!;

    private void InitializeExperiments()
    {
        SubscribeLocalEvent<ResearchConsoleComponent, AfterInteractUsingEvent>(OnConsoleAfterInteractUsing);
    }

    private void OnConsoleAfterInteractUsing(EntityUid uid, ResearchConsoleComponent component, ref AfterInteractUsingEvent args)
    {
        if (!TryGetClientServer(uid, out var serverUid, out _))
            return;

        NotifyDiscoveryEvent(serverUid.Value, new DiscoveryEventData
        {
            Type = ResearchDiscoveryEventType.ScanEntity,
            Subject = args.Used,
            User = args.User,
        });

        if (args.Handled)
            return;

        if (!TryProgressExperimentsWithEntity(serverUid.Value,
                args.Used,
                args.User,
                out _,
                out _,
                out _,
                ExperimentSourceFlags.ResearchConsole))
            return;

        args.Handled = true;
        SyncClientWithServer(uid);
        UpdateConsoleInterface(uid, component);
    }

    public bool TryProgressExperimentsWithEntity(EntityUid serverUid,
        EntityUid subject,
        EntityUid? user,
        out bool changed,
        out List<string> completed,
        out ExperimentProgressAttemptResult result,
        ExperimentSourceFlags source = ExperimentSourceFlags.AnyScanner,
        TechnologyDatabaseComponent? database = null,
        ResearchServerComponent? server = null)
    {
        changed = false;
        completed = new();
        result = ExperimentProgressAttemptResult.NoMatchingExperiment;

        if (!Resolve(serverUid, ref database, ref server))
            return false;

        var foundSourceCompatible = false;
        var foundDuplicate = false;

        foreach (var experimentId in database.ActiveExperiments.ToArray())
        {
            if (!PrototypeManager.TryIndex<ResearchExperimentPrototype>(experimentId, out var experiment) ||
                !SupportsExperimentSource(experiment, source))
                continue;

            foundSourceCompatible = true;

            if (!TryGetExperimentProgress(database, experimentId, out var progressIndex) ||
                !MatchesExperimentObjective(subject, experiment.Objective))
                continue;

            if (!TryIncrementExperimentProgress(database, progressIndex, experiment, subject, out var delta))
            {
                foundDuplicate = true;
                continue;
            }

            if (delta <= 0)
                continue;

            changed = true;
            var progress = database.ExperimentProgress[progressIndex];
            progress.Progress = Math.Min(progress.Target, progress.Progress + delta);
            database.ExperimentProgress[progressIndex] = progress;

            if (progress.Progress < progress.Target)
                continue;

            completed.Add(experiment.ID);
            CompleteExperiment(serverUid, experiment, user, database, server);
        }

        if (!changed)
        {
            result = !foundSourceCompatible
                ? ExperimentProgressAttemptResult.NoSourceCompatibleExperiment
                : foundDuplicate
                    ? ExperimentProgressAttemptResult.AlreadyScanned
                    : ExperimentProgressAttemptResult.NoMatchingExperiment;
            return false;
        }

        result = ExperimentProgressAttemptResult.Progressed;
        RecalculateTechnologyState(serverUid, database);
        UpdateTechnologyCards(serverUid, database);
        Dirty(serverUid, database);
        return true;
    }

    public bool TryProgressExperimentsByAction(EntityUid serverUid,
        string actionId,
        TechnologyDatabaseComponent? database = null,
        ResearchServerComponent? server = null)
    {
        if (!Resolve(serverUid, ref database, ref server))
            return false;

        var progressed = false;
        foreach (var experimentId in database.ActiveExperiments.ToArray())
        {
            if (!PrototypeManager.TryIndex<ResearchExperimentPrototype>(experimentId, out var experiment) ||
                experiment.Objective is not ActionCountExperimentObjective objective ||
                objective.ActionId != actionId)
                continue;

            progressed |= IncrementSimpleProgress(serverUid, database, server, experiment, 1);
        }

        if (!progressed)
            return false;

        RecalculateTechnologyState(serverUid, database);
        UpdateTechnologyCards(serverUid, database);
        Dirty(serverUid, database);
        return true;
    }

    public bool TryTriggerExperiments(EntityUid serverUid,
        string triggerId,
        TechnologyDatabaseComponent? database = null,
        ResearchServerComponent? server = null)
    {
        if (!Resolve(serverUid, ref database, ref server))
            return false;

        var progressed = false;
        foreach (var experimentId in database.ActiveExperiments.ToArray())
        {
            if (!PrototypeManager.TryIndex<ResearchExperimentPrototype>(experimentId, out var experiment) ||
                experiment.Objective is not ServerTriggerExperimentObjective objective ||
                objective.TriggerId != triggerId)
                continue;

            progressed |= IncrementSimpleProgress(serverUid, database, server, experiment, 1);
        }

        if (!progressed)
            return false;

        RecalculateTechnologyState(serverUid, database);
        UpdateTechnologyCards(serverUid, database);
        Dirty(serverUid, database);
        return true;
    }

    private void CompleteExperiment(EntityUid serverUid,
        ResearchExperimentPrototype experiment,
        EntityUid? user,
        TechnologyDatabaseComponent database,
        ResearchServerComponent server)
    {
        if (!database.CompletedExperiments.Contains(experiment.ID))
            database.CompletedExperiments.Add(experiment.ID);

        database.ActiveExperiments.Remove(experiment.ID);

        for (var i = 0; i < database.ExperimentProgress.Count; i++)
        {
            if (database.ExperimentProgress[i].ExperimentId != experiment.ID)
                continue;

            var progress = database.ExperimentProgress[i];
            progress.Progress = progress.Target;
            progress.CompletedAt = _timing.CurTime;
            database.ExperimentProgress[i] = progress;
            break;
        }

        ApplyExperimentReward(serverUid, experiment, user, database, server);

        var speech = Loc.GetString("research-experiment-completed-ic", ("experiment", Loc.GetString(experiment.Name)));
        foreach (var client in server.Clients)
        {
            if (HasComp<ResearchConsoleComponent>(client) ||
                HasComp<ExperiScannerComponent>(client) ||
                HasComp<ExperimentalDestructiveScannerComponent>(client))
                _chat.TrySendInGameICMessage(client, speech, InGameICChatType.Speak, hideChat: false);
        }

        TriggerDiscovery(serverUid, $"experiment:{experiment.ID}", database);
        LogNetworkEvent(serverUid,
            "experiment",
            Loc.GetString("research-netlog-experiment-completed",
                ("experiment", Loc.GetString(experiment.Name)),
                ("user", GetResearchLogUserName(user))),
            user);
    }

    private void ApplyExperimentReward(EntityUid serverUid,
        ResearchExperimentPrototype experiment,
        EntityUid? user,
        TechnologyDatabaseComponent database,
        ResearchServerComponent server)
    {
        if (experiment.Reward.ResearchPoints != 0)
            ModifyServerPoints(serverUid, experiment.Reward.ResearchPoints, server);

        foreach (var reward in experiment.Reward.PointRewards)
            ModifyServerPoints(serverUid, reward.Type, reward.Amount, server);

        foreach (var unlocked in experiment.Reward.UnlockExperiments)
            if (!database.UnlockedExperiments.Contains(unlocked))
                database.UnlockedExperiments.Add(unlocked);

        foreach (var technology in experiment.Reward.RevealTechnologies)
            RevealTechnology(serverUid, technology, user, database);

        LogNetworkEvent(serverUid,
            "experiment",
            Loc.GetString("research-netlog-experiment-reward-applied",
                ("experiment", Loc.GetString(experiment.Name)),
                ("user", GetResearchLogUserName(user))),
            user);
    }

    private static bool SupportsExperimentSource(ResearchExperimentPrototype experiment, ExperimentSourceFlags source)
        => source == ExperimentSourceFlags.None || (experiment.SupportedSources & source) != 0;

    private bool MatchesExperimentObjective(EntityUid subject, ExperimentObjective objective)
        => objective is ScanEntityExperimentObjective scan && MatchesEntityObjective(subject, scan);

    private bool TryIncrementExperimentProgress(TechnologyDatabaseComponent database,
        int progressIndex,
        ResearchExperimentPrototype experiment,
        EntityUid subject,
        out int delta)
    {
        delta = 0;
        var progress = database.ExperimentProgress[progressIndex];
        var netSubject = GetNetEntity(subject);

        if (progress.ScannedEntities.Contains(netSubject))
            return false;

        if (experiment.Objective is not ScanEntityExperimentObjective objective ||
            !MatchesEntityObjective(subject, objective))
            return false;

        if (objective is ScanDifferentEntitiesExperimentObjective)
        {
            var key = GetEntityObjectiveUniqueKey(subject);
            if (!progress.UniqueProgressKeys.Add(key))
                return false;
        }

        progress.ScannedEntities.Add(netSubject);
        database.ExperimentProgress[progressIndex] = progress;
        delta = 1;
        return true;
    }

    private string GetEntityObjectiveUniqueKey(EntityUid subject)
    {
        var prototype = MetaData(subject).EntityPrototype;
        return prototype != null ? $"proto:{prototype.ID}" : $"ent:{subject}";
    }

    private bool IncrementSimpleProgress(EntityUid serverUid,
        TechnologyDatabaseComponent database,
        ResearchServerComponent server,
        ResearchExperimentPrototype experiment,
        int delta)
    {
        if (!TryGetExperimentProgress(database, experiment.ID, out var progressIndex))
            return false;

        var progress = database.ExperimentProgress[progressIndex];
        progress.Progress = Math.Min(progress.Target, progress.Progress + delta);
        database.ExperimentProgress[progressIndex] = progress;

        if (progress.Progress >= progress.Target)
            CompleteExperiment(serverUid, experiment, null, database, server);

        return true;
    }

    internal bool MatchesEntityObjective(EntityUid subject, ScanEntityExperimentObjective objective)
    {
        if (objective.RequiredEntityPrototypes.Count > 0)
        {
            var prototype = MetaData(subject).EntityPrototype;
            if (prototype == null || !objective.RequiredEntityPrototypes.Contains(prototype.ID))
                return false;
        }

        foreach (var tag in objective.RequiredTags)
            if (!_tag.HasTag(subject, tag))
                return false;

        foreach (var componentName in objective.RequiredComponents)
        {
            if (!EntityManager.ComponentFactory.TryGetRegistration(componentName, out var registration) ||
                !EntityManager.HasComponent(subject, registration.Type))
                return false;
        }

        if (!MatchesReagentObjective(subject, objective) ||
            !MatchesGasObjective(subject, objective) ||
            !MatchesExplosiveObjective(subject, objective))
            return false;

        return objective.RequiredConditions.All(condition => MatchesEntityCondition(subject, condition));
    }

    private bool MatchesExplosiveObjective(EntityUid subject, ScanEntityExperimentObjective objective)
        => objective.MinExplosiveIntensity is not { } minimum ||
           TryComp<ExplosiveComponent>(subject, out var explosive) && explosive.TotalIntensity >= minimum;

    private bool MatchesReagentObjective(EntityUid subject, ScanEntityExperimentObjective objective)
    {
        if (string.IsNullOrWhiteSpace(objective.RequiredReagent))
            return true;

        if (!TryComp<SolutionContainerManagerComponent>(subject, out var manager))
            return false;

        var required = FixedPoint2.Zero;
        var other = FixedPoint2.Zero;
        foreach (var (_, solution) in _solution.EnumerateSolutions((subject, manager), includeSelf: true))
        {
            foreach (var reagent in solution.Comp.Solution.Contents)
            {
                if (reagent.Reagent.Prototype == objective.RequiredReagent)
                    required += reagent.Quantity;
                else
                    other += reagent.Quantity;
            }
        }

        if (required <= FixedPoint2.Zero)
            return false;

        if (objective.MinReagentPurity is not { } minimum)
            return true;

        var total = required + other;
        return total > FixedPoint2.Zero && (float) (required / total) >= minimum;
    }

    private bool MatchesGasObjective(EntityUid subject, ScanEntityExperimentObjective objective)
    {
        if (string.IsNullOrWhiteSpace(objective.RequiredGas))
            return true;

        if (!Enum.TryParse<Gas>(objective.RequiredGas, true, out var gas))
            return false;

        var mix = TryComp<GasCanisterComponent>(subject, out var canister)
            ? canister.Air
            : TryComp<GasTankComponent>(subject, out var tank)
                ? tank.Air
                : null;

        if (mix == null || mix.GetMoles(gas) <= 0f)
            return false;

        return objective.MinGasPurity is not { } minimum ||
               mix.TotalMoles > 0f && mix.GetMoles(gas) / mix.TotalMoles >= minimum;
    }

    private bool MatchesEntityCondition(EntityUid subject, ExperimentEntityCondition condition)
    {
        return condition switch
        {
            ExperimentEntityCondition.AnyFish => HasComp<FishComponent>(subject),
            ExperimentEntityCondition.RareFish => TryComp<FishComponent>(subject, out var fish) && fish.FishDifficulty >= 0.035f,
            ExperimentEntityCondition.IpcOrCyborg => HasComp<SiliconComponent>(subject) ||
                                                     TryComp<HumanoidAppearanceComponent>(subject, out var ipc) && ipc.Species == "IPC",
            ExperimentEntityCondition.NonBaselineHumanoid => TryComp<HumanoidAppearanceComponent>(subject, out var humanoid) &&
                                                               humanoid.Species is not "Human" and not "IPC",
            ExperimentEntityCondition.HasAugmentedOrgans => HasAugmentedOrgans(subject),
            ExperimentEntityCondition.Damaged => TryComp<DamageableComponent>(subject, out var damage) && damage.TotalDamage > FixedPoint2.Zero,
            _ => false,
        };
    }

    private bool HasAugmentedOrgans(EntityUid subject)
    {
        if (!TryComp<HumanoidAppearanceComponent>(subject, out _) || !TryComp<BodyComponent>(subject, out var body))
            return false;

        foreach (var (organUid, _) in _body.GetBodyOrgans(subject, body))
        {
            var prototype = MetaData(organUid).EntityPrototype;
            if (prototype != null && !prototype.ID.StartsWith("OrganHuman", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static bool TryGetExperimentProgress(TechnologyDatabaseComponent database, string experimentId, out int progressIndex)
    {
        for (var i = 0; i < database.ExperimentProgress.Count; i++)
        {
            if (database.ExperimentProgress[i].ExperimentId != experimentId)
                continue;
            progressIndex = i;
            return true;
        }

        progressIndex = -1;
        return false;
    }
}

public enum ExperimentProgressAttemptResult : byte
{
    Progressed,
    NoSourceCompatibleExperiment,
    NoMatchingExperiment,
    AlreadyScanned,
}

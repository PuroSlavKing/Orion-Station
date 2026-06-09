// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared._Orion.Research;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    private readonly ISawmill _sawmill = Logger.GetSawmill("research.tech-web");

    private void Sync(EntityUid primaryUid,
        EntityUid otherUid,
        TechnologyDatabaseComponent? primary = null,
        TechnologyDatabaseComponent? other = null)
    {
        if (!Resolve(primaryUid, ref primary) || !Resolve(otherUid, ref other))
            return;

        primary.MainDiscipline = other.MainDiscipline;
        primary.CurrentTechnologyCards = new(other.CurrentTechnologyCards);
        primary.SupportedDisciplines = new(other.SupportedDisciplines);
        primary.VisibleTechnologies = new(other.VisibleTechnologies);
        primary.AvailableTechnologies = new(other.AvailableTechnologies);
        primary.ResearchedTechnologies = new(other.ResearchedTechnologies);
        primary.AvailableExperiments = new(other.AvailableExperiments);
        primary.UnlockedExperiments = new(other.UnlockedExperiments);
        primary.ActiveExperiments = new(other.ActiveExperiments);
        primary.CompletedExperiments = new(other.CompletedExperiments);
        primary.SkippedExperiments = new(other.SkippedExperiments);
        primary.ExperimentProgress = other.ExperimentProgress.Select(CloneExperimentProgress).ToList();
        primary.UnlockedRecipes = new(other.UnlockedRecipes);
        primary.RevealedTechnologies = new(other.RevealedTechnologies);
        primary.DiscoveryProgress = new(other.DiscoveryProgress);
        primary.UnlockedInfrastructure = new(other.UnlockedInfrastructure);
        Dirty(primaryUid, primary);
        var ev = new TechnologyDatabaseSynchronizedEvent();
        RaiseLocalEvent(primaryUid, ref ev);
    }

    private void SyncClientWithServer(EntityUid uid,
        TechnologyDatabaseComponent? database = null,
        ResearchClientComponent? client = null)
    {
        if (!Resolve(uid, ref database, ref client, false) ||
            client.Server is not { } serverUid ||
            !TryComp(serverUid, out ResearchServerComponent? server))
            return;

        var authority = GetNetworkAuthority(serverUid, server);
        if (authority != serverUid)
        {
            UnregisterClient(uid, serverUid, client, server, dirtyServer: false);
            RegisterClient(uid, authority, client, dirtyServer: false);
            return;
        }

        if (TryComp<TechnologyDatabaseComponent>(authority, out var serverDatabase))
            Sync(uid, authority, database, serverDatabase);
    }

    private bool UnlockTechnology(EntityUid client,
        string id,
        EntityUid user,
        ResearchClientComponent? researchClient = null,
        TechnologyDatabaseComponent? database = null)
    {
        return PrototypeManager.TryIndex<TechnologyPrototype>(id, out var technology) &&
               UnlockTechnology(client, technology, user, researchClient, database);
    }

    private bool UnlockTechnology(EntityUid client,
        TechnologyPrototype technology,
        EntityUid user,
        ResearchClientComponent? researchClient = null,
        TechnologyDatabaseComponent? database = null)
    {
        if (!Resolve(client, ref researchClient, ref database, false) ||
            !TryGetClientServer(client, out var serverUid, out _, researchClient) ||
            !CanServerUnlockTechnology(client, technology, out var costs, database, researchClient) ||
            !TryConsumePoints(serverUid.Value, costs))
            return false;

        AddTechnology(serverUid.Value, technology);
        UpdateTechnologyCards(serverUid.Value);
        _adminLog.Add(LogType.Action,
            LogImpact.Medium,
            $"{ToPrettyString(user):player} unlocked {technology.ID} at {ToPrettyString(client)}.");
        LogNetworkEvent(serverUid.Value,
            "technology",
            Loc.GetString("research-netlog-technology-unlocked",
                ("technology", Loc.GetString(technology.Name)),
                ("user", GetResearchLogUserName(user))),
            user);
        return true;
    }

    [PublicAPI]
    public void AddTechnology(EntityUid uid, string id, TechnologyDatabaseComponent? component = null)
    {
        if (PrototypeManager.TryIndex<TechnologyPrototype>(id, out var technology))
            AddTechnology(uid, technology, component);
    }

    private void AddTechnology(EntityUid uid,
        TechnologyPrototype technology,
        TechnologyDatabaseComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        foreach (var generic in technology.GenericUnlocks)
            if (generic.PurchaseEvent != null)
                RaiseLocalEvent(generic.PurchaseEvent);

        if (!component.ResearchedTechnologies.Contains(technology.ID))
            component.ResearchedTechnologies.Add(technology.ID);

        foreach (var infrastructure in technology.InfrastructureUnlocks)
            if (!component.UnlockedInfrastructure.Contains(infrastructure))
                component.UnlockedInfrastructure.Add(infrastructure);

        foreach (var experiment in technology.UnlockedExperiments)
            if (!component.UnlockedExperiments.Contains(experiment))
                component.UnlockedExperiments.Add(experiment);

        RecalculateTechnologyState(uid, component);
        Dirty(uid, component);
        var ev = new TechnologyDatabaseModifiedEvent(technology.RecipeUnlocks.ToList());
        RaiseLocalEvent(uid, ref ev);
    }

    private bool CanServerUnlockTechnology(EntityUid uid,
        TechnologyPrototype technology,
        out List<ResearchPointAmount> costs,
        TechnologyDatabaseComponent? database = null,
        ResearchClientComponent? client = null)
    {
        costs = new();
        if (!Resolve(uid, ref client, ref database, false) ||
            !TryGetClientServer(uid, out var serverUid, out var server, client) ||
            !TryComp<TechnologyDatabaseComponent>(serverUid, out var serverDatabase) ||
            !CanUnlockTechnology(serverDatabase, technology))
            return false;

        costs = GetTechnologyFinalPointCosts(serverDatabase, technology);
        return HasSufficientPoints(serverUid.Value, costs, server);
    }

    private void OnDatabaseRegistrationChanged(EntityUid uid,
        TechnologyDatabaseComponent component,
        ref ResearchRegistrationChangedEvent args)
    {
        if (args.Server != null)
            return;

        component.MainDiscipline = null;
        component.CurrentTechnologyCards.Clear();
        component.SupportedDisciplines.Clear();
        component.VisibleTechnologies.Clear();
        component.AvailableTechnologies.Clear();
        component.ResearchedTechnologies.Clear();
        component.AvailableExperiments.Clear();
        component.UnlockedExperiments.Clear();
        component.ActiveExperiments.Clear();
        component.CompletedExperiments.Clear();
        component.SkippedExperiments.Clear();
        component.ExperimentProgress.Clear();
        component.UnlockedRecipes.Clear();
        component.RevealedTechnologies.Clear();
        component.DiscoveryProgress.Clear();
        component.UnlockedInfrastructure.Clear();
        Dirty(uid, component);
    }

    private static ResearchExperimentProgress CloneExperimentProgress(ResearchExperimentProgress source)
        => new()
        {
            ExperimentId = source.ExperimentId,
            Progress = source.Progress,
            Target = source.Target,
            UniqueProgressKeys = new(source.UniqueProgressKeys),
            ScannedEntities = new(source.ScannedEntities),
            CompletedAt = source.CompletedAt,
        };

    private void InitializeResearchDatabase(EntityUid uid, TechnologyDatabaseComponent? database = null)
    {
        if (!Resolve(uid, ref database, false))
            return;
        RecalculateTechnologyState(uid, database);
        UpdateTechnologyCards(uid, database);
    }
}

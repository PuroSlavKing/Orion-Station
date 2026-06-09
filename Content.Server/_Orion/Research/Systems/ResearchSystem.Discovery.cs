// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Server.Construction.Completions;
using Content.Shared._Orion.Research;
using Content.Shared._Orion.Research.Components;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Containers;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    private void InitializeDiscovery()
    {
        SubscribeLocalEvent<ResearchClientComponent, EntInsertedIntoContainerMessage>(OnDiscoveryMachineInsertion);
        SubscribeLocalEvent<MetaDataComponent, ConstructionBeforeDeleteEvent>(OnDiscoveryDeconstruct);
    }

    private void OnDiscoveryMachineInsertion(EntityUid uid, ResearchClientComponent component, ref EntInsertedIntoContainerMessage args)
    {
        if (component.Server is not { } server)
            return;

        NotifyDiscoveryEvent(server, new DiscoveryEventData
        {
            Type = ResearchDiscoveryEventType.MachineInsertion,
            Subject = args.Entity,
            Machine = uid,
        });
    }

    private void OnDiscoveryDeconstruct(EntityUid uid, MetaDataComponent component, ref ConstructionBeforeDeleteEvent args)
    {
        if (!HasComp<ResearchAnalyzableComponent>(uid) ||
            !TryGetResearchServerForEntity(uid, out var server) ||
            server is null)
            return;

        NotifyDiscoveryEvent(server.Value, new DiscoveryEventData
        {
            Type = ResearchDiscoveryEventType.DeconstructEntity,
            Subject = uid,
        });
    }

    public bool NotifyDiscoveryEvent(EntityUid serverUid,
        DiscoveryEventData data,
        TechnologyDatabaseComponent? database = null)
    {
        if (!Resolve(serverUid, ref database))
            return false;

        var revealedAny = false;
        foreach (var technology in PrototypeManager.EnumeratePrototypes<TechnologyPrototype>())
        {
            if (!technology.Hidden ||
                !database.SupportedDisciplines.Contains(technology.Discipline) ||
                database.RevealedTechnologies.Contains(technology.ID))
                continue;

            var revealed = false;
            if (technology.RevealRequirements.Count > 0)
            {
                foreach (var requirement in technology.RevealRequirements)
                {
                    if (IsRevealRequirementSatisfied(database, technology.ID, requirement) ||
                        !DoesDiscoveryEventMatch(requirement, data))
                        continue;

                    IncrementDiscoveryProgress(database, technology.ID, requirement);
                }

                revealed = technology.RevealRequirements.All(requirement =>
                    IsRevealRequirementSatisfied(database, technology.ID, requirement));
            }

            if (!revealed && data.Subject is { } subject)
            {
                var prototype = MetaData(subject).EntityPrototype?.ID;
                revealed = prototype != null && data.Type switch
                {
                    ResearchDiscoveryEventType.MachineInsertion => technology.ItemUnlocks.Contains(prototype),
                    ResearchDiscoveryEventType.DeconstructEntity => technology.DeconstructionUnlocks.Contains(prototype),
                    _ => false,
                };
            }

            if (!revealed)
                continue;

            database.RevealedTechnologies.Add(technology.ID);
            revealedAny = true;
            LogNetworkEvent(serverUid,
                "discovery",
                Loc.GetString("research-netlog-discovery-hidden-tech",
                    ("technology", Loc.GetString(technology.Name)),
                    ("user", GetResearchLogUserName(data.User))),
                data.User);
        }

        if (!revealedAny)
            return false;

        RecalculateTechnologyState(serverUid, database);
        UpdateTechnologyCards(serverUid, database);
        Dirty(serverUid, database);

        if (TryComp<ResearchServerComponent>(serverUid, out var server))
        {
            foreach (var client in server.Clients)
            {
                SyncClientWithServer(client);
                UpdateConsoleInterface(client);
            }
        }

        return true;
    }

    public bool TriggerDiscovery(EntityUid serverUid, string triggerId, TechnologyDatabaseComponent? database = null)
        => NotifyDiscoveryEvent(serverUid,
            new DiscoveryEventData { Type = ResearchDiscoveryEventType.ServerTrigger, TriggerId = triggerId },
            database);

    private static bool IsRevealRequirementSatisfied(TechnologyDatabaseComponent database,
        string technologyId,
        TechnologyRevealRequirement requirement)
    {
        return requirement switch
        {
            RevealedTechnologyRevealRequirement revealed => database.RevealedTechnologies.Contains(revealed.Technology) ||
                                                             database.ResearchedTechnologies.Contains(revealed.Technology),
            ResearchedTechnologyRevealRequirement researched => database.ResearchedTechnologies.Contains(researched.Technology),
            CompletedExperimentRevealRequirement experiment => database.CompletedExperiments.Contains(experiment.Experiment),
            _ => database.DiscoveryProgress.Any(entry =>
                entry.TechnologyId == technologyId &&
                entry.RequirementId == requirement.Id &&
                entry.Progress >= Math.Max(1, requirement.Target)),
        };
    }

    private bool DoesDiscoveryEventMatch(TechnologyRevealRequirement requirement, DiscoveryEventData data)
    {
        return requirement switch
        {
            ScanEntityRevealRequirement scan when requirement.Kind == TechnologyRevealRequirementKind.ScanEntity =>
                data.Type == ResearchDiscoveryEventType.ScanEntity && MatchesScanRequirement(scan, data.Subject),
            MachineInsertionRevealRequirement insertion =>
                data.Type == ResearchDiscoveryEventType.MachineInsertion &&
                MatchesScanRequirement(insertion, data.Subject) &&
                (insertion.RequiredMachinePrototype == null || GetPrototypeId(data.Machine) == insertion.RequiredMachinePrototype),
            DeconstructEntityRevealRequirement deconstruct =>
                data.Type == ResearchDiscoveryEventType.DeconstructEntity &&
                MatchesDeconstructRequirement(deconstruct, data.Subject),
            ServerTriggerRevealRequirement trigger =>
                data.Type == ResearchDiscoveryEventType.ServerTrigger && trigger.TriggerId == data.TriggerId,
            _ => false,
        };
    }

    private bool MatchesScanRequirement(ScanEntityRevealRequirement requirement, EntityUid? subject)
    {
        if (subject is null ||
            requirement.RequiredEntityPrototype != null && GetPrototypeId(subject) != requirement.RequiredEntityPrototype)
            return false;

        return requirement.RequiredTags.All(tag => _tag.HasTag(subject.Value, tag));
    }

    private bool MatchesDeconstructRequirement(DeconstructEntityRevealRequirement requirement, EntityUid? subject)
    {
        if (subject is null ||
            requirement.RequiredEntityPrototype != null && GetPrototypeId(subject) != requirement.RequiredEntityPrototype)
            return false;

        return requirement.RequiredTags.All(tag => _tag.HasTag(subject.Value, tag));
    }

    private void IncrementDiscoveryProgress(TechnologyDatabaseComponent database,
        string technologyId,
        TechnologyRevealRequirement requirement)
    {
        var target = Math.Max(1, requirement.Target);
        for (var i = 0; i < database.DiscoveryProgress.Count; i++)
        {
            var entry = database.DiscoveryProgress[i];
            if (entry.TechnologyId != technologyId || entry.RequirementId != requirement.Id)
                continue;

            entry.Progress = Math.Min(target, entry.Progress + 1);
            if (entry.Progress >= target)
                entry.CompletedAt = _timing.CurTime;
            database.DiscoveryProgress[i] = entry;
            return;
        }

        database.DiscoveryProgress.Add(new TechnologyDiscoveryProgress
        {
            TechnologyId = technologyId,
            RequirementId = requirement.Id,
            Progress = 1,
            Target = target,
            CompletedAt = target <= 1 ? _timing.CurTime : null,
        });
    }

    public List<string> GetHiddenTechnologiesForRequiredItem(EntityUid serverUid,
        EntityUid subject,
        TechnologyDatabaseComponent? database = null)
    {
        if (!Resolve(serverUid, ref database))
            return new();

        var prototype = GetPrototypeId(subject);
        if (prototype == null)
            return new();

        return PrototypeManager.EnumeratePrototypes<TechnologyPrototype>()
            .Where(technology => technology.Hidden &&
                                 database.SupportedDisciplines.Contains(technology.Discipline) &&
                                 !database.RevealedTechnologies.Contains(technology.ID) &&
                                 technology.RequiredItemsToUnlock.Contains(prototype))
            .Select(technology => technology.ID)
            .ToList();
    }

    private string? GetPrototypeId(EntityUid? uid)
        => uid is { } entity && TryComp<MetaDataComponent>(entity, out var meta)
            ? meta.EntityPrototype?.ID
            : null;

    private bool TryGetResearchServerForEntity(EntityUid uid, out EntityUid? serverUid)
    {
        serverUid = null;

        if (TryComp<ResearchClientComponent>(uid, out var direct) && direct.Server is { } directServer)
        {
            serverUid = directServer;
            return true;
        }

        var xform = Transform(uid);
        if (TryComp<ResearchClientComponent>(xform.ParentUid, out var owner) && owner.Server is { } ownerServer)
        {
            serverUid = ownerServer;
            return true;
        }

        if (xform.GridUid is not { } grid)
            return false;

        var query = EntityQueryEnumerator<ResearchServerComponent>();
        while (query.MoveNext(out var candidate, out _))
        {
            if (Transform(candidate).GridUid != grid)
                continue;
            serverUid = candidate;
            return true;
        }

        return false;
    }

    public sealed record DiscoveryEventData
    {
        public ResearchDiscoveryEventType Type;
        public EntityUid? Subject;
        public EntityUid? Machine;
        public EntityUid? User;
        public string? TriggerId;
    }
}

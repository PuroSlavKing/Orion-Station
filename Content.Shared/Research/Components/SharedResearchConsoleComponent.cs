// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Common.Research;
using Content.Shared._Orion.Research;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Research.Components;

[NetSerializable, Serializable]
public enum ResearchConsoleUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ConsoleUnlockTechnologyMessage(string id) : BoundUserInterfaceMessage
{
    public string Id = id;
}

[Serializable, NetSerializable]
public sealed class ConsoleServerSelectionMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ResearchConsoleBoundInterfaceState : BoundUserInterfaceState
{
    public int Points;
    public Dictionary<string, ResearchAvailability> Researches;
    public List<ProtoId<TechnologyPrototype>> VisibleTechnologies;
    public List<ProtoId<TechnologyPrototype>> AvailableTechnologies;
    public List<ProtoId<TechnologyPrototype>> ResearchedTechnologies;
    public List<string> CompletedExperiments;
    public List<ResearchConsoleExperimentData> Experiments;
    public Dictionary<string, ResearchTechnologyLockReason> TechnologyLockReasons;
    public string NetworkId;
    public List<ResearchPointAmount> PointBalances;
    public List<ResearchLogEntry> Logs;

    public ResearchConsoleBoundInterfaceState(int points)
        : this(points, new(), new(), new(), new(), new(), new(), new(), string.Empty,
            [new ResearchPointAmount { Type = "General", Amount = points }], new())
    {
    }

    public ResearchConsoleBoundInterfaceState(int points, Dictionary<string, ResearchAvailability> researches)
        : this(points, researches, new(), new(), new(), new(), new(), new(), string.Empty,
            [new ResearchPointAmount { Type = "General", Amount = points }], new())
    {
    }

    public ResearchConsoleBoundInterfaceState(
        int points,
        Dictionary<string, ResearchAvailability> researches,
        List<ProtoId<TechnologyPrototype>> visibleTechnologies,
        List<ProtoId<TechnologyPrototype>> availableTechnologies,
        List<ProtoId<TechnologyPrototype>> researchedTechnologies,
        List<string> completedExperiments,
        List<ResearchConsoleExperimentData> experiments,
        Dictionary<string, ResearchTechnologyLockReason> technologyLockReasons,
        string networkId,
        List<ResearchPointAmount> pointBalances,
        List<ResearchLogEntry> logs)
    {
        Points = points;
        Researches = researches;
        VisibleTechnologies = visibleTechnologies;
        AvailableTechnologies = availableTechnologies;
        ResearchedTechnologies = researchedTechnologies;
        CompletedExperiments = completedExperiments;
        Experiments = experiments;
        TechnologyLockReasons = technologyLockReasons;
        NetworkId = networkId;
        PointBalances = pointBalances;
        Logs = logs;
    }
}

[Serializable, NetSerializable]
public sealed class ResearchConsoleExperimentData(string id, int progress, int target, ResearchExperimentState state)
{
    public string Id = id;
    public int Progress = progress;
    public int Target = target;
    public ResearchExperimentState State = state;
}

using System.Globalization;
using System.Linq;
using Content.Shared._Orion.Research.Components;
using Content.Shared._Orion.Research.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Prototypes;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Research.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Orion.Research.Systems;

public static class ResearchExperimentUiData
{
    public static ResearchMachineExperimentUiData Create(ResearchExperimentPrototype prototype,
        ResearchExperimentProgress progress,
        IPrototypeManager prototypes)
    {
        var target = progress.Target > 0 ? progress.Target : Math.Max(1, prototype.Objective.Target);
        var objective = Loc.GetString($"research-experiment-objective-{prototype.Objective.Kind.ToString().ToLowerInvariant()}");
        var goal = BuildGoalText(prototype.Objective, prototypes);
        return new ResearchMachineExperimentUiData(prototype.ID,
            Loc.GetString(prototype.Name),
            Loc.GetString(prototype.Description),
            progress.Progress,
            target,
            objective,
            goal);
    }

    private static string BuildGoalText(ExperimentObjective objective, IPrototypeManager prototypes)
    {
        var action = Loc.GetString($"research-experiment-goal-action-{objective.Kind.ToString().ToLowerInvariant()}");
        if (objective is not ScanEntityExperimentObjective scan)
            return action;

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(scan.RequiredReagent))
        {
            var name = scan.RequiredReagent;
            if (prototypes.TryIndex<ReagentPrototype>(scan.RequiredReagent, out var reagent))
                name = reagent.LocalizedName;
            details.Add(Loc.GetString("research-experiment-goal-detail-reagent", ("name", name)));
            if (scan.MinReagentPurity is { } purity)
                details.Add(Loc.GetString("research-experiment-goal-detail-purity",
                    ("name", Loc.GetString("research-experiment-goal-purity-reagent")),
                    ("value", FormatPercent(purity))));
        }

        if (!string.IsNullOrWhiteSpace(scan.RequiredGas))
        {
            details.Add(Loc.GetString("research-experiment-goal-detail-gas", ("name", GetGasName(scan.RequiredGas, prototypes))));
            if (scan.MinGasPurity is { } purity)
                details.Add(Loc.GetString("research-experiment-goal-detail-purity",
                    ("name", Loc.GetString("research-experiment-goal-purity-gas")),
                    ("value", FormatPercent(purity))));
        }

        if (scan.RequiredEntityPrototypes.Count > 0)
            details.Add(Loc.GetString("research-experiment-goal-detail-entities",
                ("names", string.Join(", ", scan.RequiredEntityPrototypes.Select(id => GetEntityName(id, prototypes))))));

        if (scan.RequiredConditions.Count > 0)
            details.Add(Loc.GetString("research-experiment-goal-detail-conditions",
                ("names", string.Join(", ", scan.RequiredConditions.Select(condition =>
                    Loc.GetString($"research-experiment-goal-condition-{condition.ToString().ToLowerInvariant()}"))))));

        if (scan.MinExplosiveIntensity is { } intensity)
            details.Add(Loc.GetString("research-experiment-goal-detail-intensity",
                ("value", intensity.ToString("0.##", CultureInfo.InvariantCulture))));

        return details.Count == 0
            ? action
            : Loc.GetString("research-experiment-goal-with-details", ("action", action), ("details", string.Join(", ", details)));
    }

    private static string FormatPercent(float value)
        => (value * 100f).ToString("0.#", CultureInfo.InvariantCulture);

    private static string GetEntityName(string prototypeId, IPrototypeManager prototypes)
        => prototypes.TryIndex<EntityPrototype>(prototypeId, out var prototype) ? prototype.Name : prototypeId;

    private static string GetGasName(string gasId, IPrototypeManager prototypes)
    {
        if (!Enum.TryParse<Gas>(gasId, true, out var gas) ||
            !prototypes.TryIndex<GasPrototype>(((int) gas).ToString(), out var prototype))
            return gasId;
        return Loc.GetString(prototype.Name);
    }
}

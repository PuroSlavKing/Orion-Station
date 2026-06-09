// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared._Orion.Research;
using Content.Shared._Orion.Research.Prototypes;
using Content.Shared.Lathe;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Shared.Research.Systems;

public abstract partial class SharedResearchSystem : EntitySystem
{
    [Dependency] protected readonly IPrototypeManager PrototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedLatheSystem _lathe = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TechnologyDatabaseComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(EntityUid uid, TechnologyDatabaseComponent component, MapInitEvent args)
    {
        RecalculateTechnologyState(uid, component);
        UpdateTechnologyCards(uid, component);
    }

    public void UpdateTechnologyCards(EntityUid uid, TechnologyDatabaseComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        var available = GetAvailableTechnologies(uid, component);
        _random.Shuffle(available);
        component.CurrentTechnologyCards.Clear();

        foreach (var discipline in component.SupportedDisciplines)
        {
            var technology = available.FirstOrDefault(proto => proto.Discipline == discipline);
            if (technology != null)
                component.CurrentTechnologyCards.Add(technology.ID);
        }

        Dirty(uid, component);
    }

    public List<TechnologyPrototype> GetAvailableTechnologies(EntityUid uid, TechnologyDatabaseComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return new();

        return component.AvailableTechnologies
            .Select(id => PrototypeManager.TryIndex(id, out TechnologyPrototype? technology) ? technology : null)
            .WhereNotNull()
            .ToList();
    }

    public bool IsTechnologyAvailable(TechnologyDatabaseComponent component,
        TechnologyPrototype technology,
        Dictionary<string, int>? disciplineTiers = null)
    {
        return component.VisibleTechnologies.Contains(technology.ID) &&
               component.AvailableTechnologies.Contains(technology.ID) &&
               !component.ResearchedTechnologies.Contains(technology.ID);
    }

    public Dictionary<string, int> GetDisciplineTiers(TechnologyDatabaseComponent component)
    {
        var result = new Dictionary<string, int>();
        foreach (var discipline in component.SupportedDisciplines)
            result[discipline] = GetHighestDisciplineTier(component, discipline);
        return result;
    }

    public int GetHighestDisciplineTier(TechnologyDatabaseComponent component, string disciplineId)
        => GetHighestDisciplineTier(component, PrototypeManager.Index<TechDisciplinePrototype>(disciplineId));

    public int GetHighestDisciplineTier(TechnologyDatabaseComponent component, TechDisciplinePrototype discipline)
    {
        var all = PrototypeManager.EnumeratePrototypes<TechnologyPrototype>()
            .Where(proto => proto.Discipline == discipline.ID && !proto.Hidden)
            .ToList();
        var researched = component.ResearchedTechnologies
            .Select(id => PrototypeManager.TryIndex(id, out TechnologyPrototype? proto) ? proto : null)
            .WhereNotNull()
            .Where(proto => proto.Discipline == discipline.ID)
            .ToList();

        if (discipline.TierPrerequisites.Count == 0)
            return 1;

        var highest = discipline.TierPrerequisites.Keys.Max();
        var tier = 2;
        while (tier <= highest)
        {
            var allPrevious = all.Count(proto => proto.Tier == tier - 1);
            if (allPrevious == 0)
                break;
            var researchedPrevious = researched.Count(proto => proto.Tier == tier - 1);
            if ((float) researchedPrevious / allPrevious < discipline.TierPrerequisites[tier])
                break;
            tier++;
        }

        return tier - 1;
    }

    public FormattedMessage GetTechnologyDescription(TechnologyPrototype technology,
        bool includeCost = true,
        bool includeTier = true,
        bool includePrereqs = false,
        TechDisciplinePrototype? disciplinePrototype = null)
    {
        var description = new FormattedMessage();

        if (includeTier)
        {
            disciplinePrototype ??= PrototypeManager.Index(technology.Discipline);
            description.AddMarkupOrThrow(Loc.GetString("research-console-tier-discipline-info",
                ("tier", technology.Tier),
                ("color", disciplinePrototype.Color),
                ("discipline", Loc.GetString(disciplinePrototype.Name))));
            description.PushNewline();
        }

        if (includeCost)
        {
            description.AddMarkupOrThrow(Loc.GetString("research-console-cost",
                ("amount", FormatResearchPointAmounts(GetPrototypePointCosts(technology)))));
            description.PushNewline();
        }

        if (!string.IsNullOrWhiteSpace(technology.Description))
        {
            description.AddText(Loc.GetString(technology.Description));
            description.PushNewline();
        }

        if (includePrereqs && technology.TechnologyPrerequisites.Count > 0)
        {
            description.AddMarkupOrThrow(Loc.GetString("research-console-prereqs-list-start"));
            foreach (var prerequisite in technology.TechnologyPrerequisites)
            {
                var proto = PrototypeManager.Index(prerequisite);
                description.PushNewline();
                description.AddMarkupOrThrow(Loc.GetString("research-console-prereqs-list-entry",
                    ("text", Loc.GetString(proto.Name))));
            }
            description.PushNewline();
        }

        description.AddMarkupOrThrow(Loc.GetString("research-console-unlocks-list-start"));
        foreach (var recipe in technology.RecipeUnlocks)
        {
            var proto = PrototypeManager.Index(recipe);
            description.PushNewline();
            description.AddMarkupOrThrow(Loc.GetString("research-console-unlocks-list-entry",
                ("name", _lathe.GetRecipeName(proto))));
        }

        foreach (var generic in technology.GenericUnlocks)
        {
            description.PushNewline();
            description.AddMarkupOrThrow(Loc.GetString("research-console-unlocks-list-entry-generic",
                ("text", Loc.GetString(generic.UnlockDescription))));
        }

        return description;
    }

    public bool IsTechnologyUnlocked(EntityUid uid,
        TechnologyPrototype technology,
        TechnologyDatabaseComponent? component = null)
        => Resolve(uid, ref component) && component.ResearchedTechnologies.Contains(technology.ID);

    public bool IsTechnologyUnlocked(EntityUid uid,
        string technologyId,
        TechnologyDatabaseComponent? component = null)
        => Resolve(uid, ref component, false) && component.ResearchedTechnologies.Contains(technologyId);

    public virtual bool CanUnlockTechnology(TechnologyDatabaseComponent component, TechnologyPrototype technology)
    {
        return GetTechnologyLockReason(component, technology) == ResearchTechnologyLockReason.None;
    }

    public void RecalculateTechnologyState(EntityUid uid, TechnologyDatabaseComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        EnsureStartingTechnologies(component);
        ApplyPassiveReveals(component);

        var visible = new HashSet<ProtoId<TechnologyPrototype>>();
        var available = new HashSet<ProtoId<TechnologyPrototype>>();

        foreach (var technology in PrototypeManager.EnumeratePrototypes<TechnologyPrototype>())
        {
            if (!component.SupportedDisciplines.Contains(technology.Discipline))
                continue;

            var researched = component.ResearchedTechnologies.Contains(technology.ID);
            var revealed = !technology.Hidden || component.RevealedTechnologies.Contains(technology.ID) || researched;
            if (!revealed)
                continue;

            visible.Add(technology.ID);
            if (!researched &&
                technology.AllRequiredTechnologies.All(component.ResearchedTechnologies.Contains) &&
                HasRequiredExperiments(component, technology))
                available.Add(technology.ID);
        }

        component.VisibleTechnologies = visible.ToList();
        component.AvailableTechnologies = available.ToList();
        RefreshAvailableExperiments(component);
        RebuildUnlockedRecipes(component);
        Dirty(uid, component);
    }

    protected static bool HasRequiredExperiments(TechnologyDatabaseComponent component, TechnologyPrototype technology)
        => technology.RequiredExperiments.All(component.CompletedExperiments.Contains);

    protected List<ResearchPointAmount> GetPrototypePointCosts(TechnologyPrototype technology)
    {
        if (technology.PointCosts.Count > 0)
            return technology.PointCosts.Select(ClonePointAmount).ToList();

        return
        [
            new ResearchPointAmount
            {
                Type = "General",
                Amount = technology.Cost,
            },
        ];
    }

    protected List<ResearchPointAmount> GetTechnologyFinalPointCosts(TechnologyDatabaseComponent component,
        TechnologyPrototype technology)
    {
        var costs = GetPrototypePointCosts(technology);
        for (var i = 0; i < costs.Count; i++)
        {
            if (!string.Equals(costs[i].Type, "General", StringComparison.OrdinalIgnoreCase))
                continue;
            var amount = costs[i];
            amount.Amount = GetTechnologyFinalCost(component, technology);
            costs[i] = amount;
            break;
        }
        return costs;
    }

    protected int GetTechnologyFinalCost(TechnologyDatabaseComponent component, TechnologyPrototype technology)
    {
        var baseCost = GetPrototypePointCosts(technology)
            .FirstOrDefault(amount => string.Equals(amount.Type, "General", StringComparison.OrdinalIgnoreCase))
            .Amount;
        var flatDiscount = 0;
        var percentageDiscount = 0f;

        foreach (var experimentId in technology.DiscountExperiments)
        {
            if (!component.CompletedExperiments.Contains(experimentId))
                continue;

            if (technology.DiscountExperimentCosts.TryGetValue(experimentId, out var fixedDiscount))
            {
                flatDiscount += fixedDiscount;
                continue;
            }

            if (!PrototypeManager.TryIndex<ResearchExperimentPrototype>(experimentId, out var experiment))
                continue;

            flatDiscount += experiment.Reward.FlatDiscount;
            percentageDiscount += experiment.Reward.PercentageDiscount;
        }

        var percentageValue = (int) MathF.Round(baseCost * Math.Clamp(percentageDiscount, 0f, 1f));
        return Math.Max(0, baseCost - flatDiscount - percentageValue);
    }

    protected string FormatResearchPointAmounts(IEnumerable<ResearchPointAmount> amounts)
    {
        return string.Join(", ", amounts.Select(amount =>
            $"{amount.Amount} {LocalizeResearchPointType(amount.Type)}"));
    }

    protected string LocalizeResearchPointType(string type)
    {
        var key = $"research-point-type-{type.ToLowerInvariant()}";
        return Loc.TryGetString(key, out var localized) ? localized : type;
    }

    public static ResearchTechnologyLockReason GetTechnologyLockReason(TechnologyDatabaseComponent component,
        TechnologyPrototype technology)
    {
        if (!component.SupportedDisciplines.Contains(technology.Discipline))
            return ResearchTechnologyLockReason.NotSupported;
        if (component.ResearchedTechnologies.Contains(technology.ID))
            return ResearchTechnologyLockReason.AlreadyResearched;
        if (!component.VisibleTechnologies.Contains(technology.ID))
            return technology.Hidden
                ? ResearchTechnologyLockReason.MissingDiscovery
                : ResearchTechnologyLockReason.Hidden;
        if (!technology.AllRequiredTechnologies.All(component.ResearchedTechnologies.Contains))
            return ResearchTechnologyLockReason.MissingPrerequisites;
        if (!HasRequiredExperiments(component, technology))
            return ResearchTechnologyLockReason.MissingExperiments;
        return component.AvailableTechnologies.Contains(technology.ID)
            ? ResearchTechnologyLockReason.None
            : ResearchTechnologyLockReason.MissingPrerequisites;
    }

    public ResearchTechnologyVisibilityState GetTechnologyVisibilityState(TechnologyDatabaseComponent component,
        TechnologyPrototype technology)
    {
        if (component.ResearchedTechnologies.Contains(technology.ID))
            return ResearchTechnologyVisibilityState.Researched;
        if (!component.VisibleTechnologies.Contains(technology.ID))
            return ResearchTechnologyVisibilityState.Hidden;
        return component.AvailableTechnologies.Contains(technology.ID)
            ? ResearchTechnologyVisibilityState.Available
            : ResearchTechnologyVisibilityState.RevealedLocked;
    }

    public void TrySetMainDiscipline(TechnologyPrototype technology,
        EntityUid uid,
        TechnologyDatabaseComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;
        var discipline = PrototypeManager.Index(technology.Discipline);
        if (technology.Tier < discipline.LockoutTier)
            return;
        component.MainDiscipline = technology.Discipline;
        Dirty(uid, component);
        var ev = new TechnologyDatabaseModifiedEvent();
        RaiseLocalEvent(uid, ref ev);
    }

    [PublicAPI]
    public bool TryRemoveTechnology(Entity<TechnologyDatabaseComponent> entity, ProtoId<TechnologyPrototype> technology)
        => TryRemoveTechnology(entity, PrototypeManager.Index(technology));

    [PublicAPI]
    public bool TryRemoveTechnology(Entity<TechnologyDatabaseComponent> entity, TechnologyPrototype technology)
    {
        if (!entity.Comp.ResearchedTechnologies.Remove(technology.ID))
            return false;
        RecalculateTechnologyState(entity, entity.Comp);
        UpdateTechnologyCards(entity, entity.Comp);
        return true;
    }

    [PublicAPI]
    public void ClearTechs(EntityUid uid, TechnologyDatabaseComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;
        component.ResearchedTechnologies.Clear();
        RecalculateTechnologyState(uid, component);
        UpdateTechnologyCards(uid, component);
    }

    public void AddLatheRecipe(EntityUid uid, string recipe, TechnologyDatabaseComponent? component = null)
    {
        if (!Resolve(uid, ref component) || component.UnlockedRecipes.Contains(recipe))
            return;
        component.UnlockedRecipes.Add(recipe);
        Dirty(uid, component);
        var ev = new TechnologyDatabaseModifiedEvent(new List<string> { recipe });
        RaiseLocalEvent(uid, ref ev);
    }

    private void EnsureStartingTechnologies(TechnologyDatabaseComponent component)
    {
        foreach (var technology in PrototypeManager.EnumeratePrototypes<TechnologyPrototype>())
        {
            if (technology.StartingTechnology &&
                component.SupportedDisciplines.Contains(technology.Discipline) &&
                !component.ResearchedTechnologies.Contains(technology.ID))
                component.ResearchedTechnologies.Add(technology.ID);
        }
    }

    private void ApplyPassiveReveals(TechnologyDatabaseComponent component)
    {
        foreach (var technology in PrototypeManager.EnumeratePrototypes<TechnologyPrototype>())
        {
            if (!technology.Hidden || component.RevealedTechnologies.Contains(technology.ID))
                continue;
            if (technology.RevealRequirements.Count == 0)
                continue;

            var satisfied = technology.RevealRequirements.All(requirement => requirement switch
            {
                ResearchedTechnologyRevealRequirement researched =>
                    component.ResearchedTechnologies.Contains(researched.Technology),
                RevealedTechnologyRevealRequirement revealed =>
                    component.RevealedTechnologies.Contains(revealed.Technology) ||
                    component.ResearchedTechnologies.Contains(revealed.Technology),
                CompletedExperimentRevealRequirement experiment =>
                    component.CompletedExperiments.Contains(experiment.Experiment),
                _ => false,
            });

            if (satisfied)
                component.RevealedTechnologies.Add(technology.ID);
        }
    }

    private void RebuildUnlockedRecipes(TechnologyDatabaseComponent component)
    {
        var recipes = new HashSet<ProtoId<LatheRecipePrototype>>();
        foreach (var technologyId in component.ResearchedTechnologies)
        {
            if (PrototypeManager.TryIndex(technologyId, out TechnologyPrototype? technology))
                recipes.UnionWith(technology.RecipeUnlocks);
        }
        component.UnlockedRecipes = recipes.ToList();
    }

    private void RefreshAvailableExperiments(TechnologyDatabaseComponent component)
    {
        var available = new HashSet<string>(component.UnlockedExperiments);

        foreach (var technologyId in component.ResearchedTechnologies)
        {
            if (PrototypeManager.TryIndex(technologyId, out TechnologyPrototype? technology))
                available.UnionWith(technology.UnlockedExperiments);
        }

        foreach (var experiment in PrototypeManager.EnumeratePrototypes<ResearchExperimentPrototype>())
        {
            var prerequisites = experiment.RequiredTechnologies.All(component.ResearchedTechnologies.Contains) &&
                                experiment.RequiredExperiments.All(component.CompletedExperiments.Contains);
            if (experiment.StartingExperiment || prerequisites)
                available.Add(experiment.ID);
        }

        available.ExceptWith(component.CompletedExperiments);
        available.ExceptWith(component.SkippedExperiments);
        component.AvailableExperiments = available.ToList();
        component.ActiveExperiments = available.ToList();

        foreach (var experimentId in component.ActiveExperiments)
        {
            if (component.ExperimentProgress.Any(progress => progress.ExperimentId == experimentId) ||
                !PrototypeManager.TryIndex<ResearchExperimentPrototype>(experimentId, out var experiment))
                continue;
            component.ExperimentProgress.Add(new ResearchExperimentProgress
            {
                ExperimentId = experimentId,
                Target = Math.Max(1, experiment.Objective.Target),
            });
        }
    }

    private static ResearchPointAmount ClonePointAmount(ResearchPointAmount source)
        => new() { Type = source.Type, Amount = source.Amount };
}

// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Orion.Research.Prototypes;
using Content.Shared.Lathe;
using Content.Shared.Research.Prototypes;
using Content.Shared.Research.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared.Research.Components;

[RegisterComponent, NetworkedComponent, Access(typeof(SharedResearchSystem), typeof(SharedLatheSystem)), AutoGenerateComponentState]
public sealed partial class TechnologyDatabaseComponent : Component
{
    [AutoNetworkedField, DataField("mainDiscipline", customTypeSerializer: typeof(PrototypeIdSerializer<TechDisciplinePrototype>))]
    public string? MainDiscipline;

    [AutoNetworkedField, DataField("currentTechnologyCards")]
    public List<string> CurrentTechnologyCards = new();

    [AutoNetworkedField, DataField]
    public List<ProtoId<TechDisciplinePrototype>> SupportedDisciplines = new();

    [AutoNetworkedField, DataField]
    public List<ProtoId<TechnologyPrototype>> VisibleTechnologies = new();

    [AutoNetworkedField, DataField]
    public List<ProtoId<TechnologyPrototype>> AvailableTechnologies = new();

    [AutoNetworkedField, DataField]
    public List<ProtoId<TechnologyPrototype>> ResearchedTechnologies = new();

    // Compatibility surface for inherited systems that still use the old name.
    public List<ProtoId<TechnologyPrototype>> UnlockedTechnologies
    {
        get => ResearchedTechnologies;
        set => ResearchedTechnologies = value;
    }

    [AutoNetworkedField, DataField]
    public List<string> AvailableExperiments = new();

    [AutoNetworkedField, DataField]
    public List<string> UnlockedExperiments = new();

    [AutoNetworkedField, DataField]
    public List<string> ActiveExperiments = new();

    [AutoNetworkedField, DataField]
    public List<string> CompletedExperiments = new();

    [AutoNetworkedField, DataField]
    public List<ResearchExperimentProgress> ExperimentProgress = new();

    [AutoNetworkedField, DataField]
    public List<string> SkippedExperiments = new();

    [AutoNetworkedField, DataField]
    public List<ProtoId<LatheRecipePrototype>> UnlockedRecipes = new();

    [AutoNetworkedField, DataField]
    public List<ProtoId<TechnologyPrototype>> RevealedTechnologies = new();

    [AutoNetworkedField, DataField]
    public List<TechnologyDiscoveryProgress> DiscoveryProgress = new();

    [AutoNetworkedField, DataField]
    public List<string> UnlockedInfrastructure = new();
}

[ByRefEvent]
public readonly record struct TechnologyDatabaseModifiedEvent
{
    public readonly List<ProtoId<LatheRecipePrototype>> UnlockedRecipes;

    public TechnologyDatabaseModifiedEvent(List<ProtoId<LatheRecipePrototype>>? unlockedRecipes = null)
    {
        UnlockedRecipes = unlockedRecipes ?? new();
    }

    public TechnologyDatabaseModifiedEvent(List<string> unlockedRecipes)
    {
        UnlockedRecipes = unlockedRecipes.Select(id => new ProtoId<LatheRecipePrototype>(id)).ToList();
    }
}

[ByRefEvent]
public readonly record struct TechnologyDatabaseSynchronizedEvent;

[DataDefinition, Serializable, NetSerializable]
public partial record struct ResearchExperimentProgress
{
    [DataField] public ProtoId<ResearchExperimentPrototype> ExperimentId;
    [DataField] public int Progress;
    [DataField] public int Target;
    [DataField] public HashSet<string> UniqueProgressKeys = new();
    [DataField] public HashSet<NetEntity> ScannedEntities = new();
    [DataField] public TimeSpan? CompletedAt;
}

[DataDefinition, Serializable, NetSerializable]
public partial record struct TechnologyDiscoveryProgress
{
    [DataField] public ProtoId<TechnologyPrototype> TechnologyId;
    [DataField] public string RequirementId = string.Empty;
    [DataField] public int Progress;
    [DataField] public int Target;
    [DataField] public TimeSpan? CompletedAt;
}

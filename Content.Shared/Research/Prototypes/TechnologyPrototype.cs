// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Orion.Research;
using Content.Shared._Orion.Research.Prototypes;
using Content.Shared.Lathe;
using Content.Shared.Radio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared.Research.Prototypes;

[Prototype]
public sealed partial class TechnologyPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public LocId Name = string.Empty;
    [DataField] public LocId Description = string.Empty;
    [DataField(required: true)] public SpriteSpecifier Icon = default!;
    [DataField(required: true)] public ProtoId<TechDisciplinePrototype> Discipline;
    [DataField(required: true)] public int Tier;
    [DataField] public bool Hidden;
    [DataField] public bool StartingTechnology;

    // Legacy scalar cost is retained for unported Reforged prototypes.
    [DataField]
    public int Cost = 10000;

    [DataField]
    public List<ResearchPointAmount> PointCosts = new();

    [DataField] public List<ProtoId<TechnologyPrototype>> TechnologyPrerequisites = new();
    [DataField] public List<string> RequiredExperiments = new();
    [DataField] public List<string> DiscountExperiments = new();
    [DataField] public Dictionary<string, int> DiscountExperimentCosts = new();
    [DataField] public List<string> UnlockedExperiments = new();
    [DataField] public bool AnnounceOnUnlock = true;
    [DataField] public List<ProtoId<RadioChannelPrototype>> AnnounceChannels = new();
    [DataField] public bool InfrastructureUnlock;
    [DataField] public List<string> InfrastructureUnlocks = new();
    [DataField] public List<TechnologyRevealRequirement> RevealRequirements = new();
    [DataField] public List<ProtoId<LatheRecipePrototype>> RecipeUnlocks = new();
    [DataField] public IReadOnlyList<GenericUnlock> GenericUnlocks = new List<GenericUnlock>();
    [DataField] public List<string> ItemUnlocks = new();
    [DataField] public List<string> DeconstructionUnlocks = new();
    [DataField] public List<string> RequiredItemsToUnlock = new();
    [DataField] public Vector2i Position { get; private set; }
    public IEnumerable<ProtoId<TechnologyPrototype>> AllRequiredTechnologies => TechnologyPrerequisites;
}

[Serializable, NetSerializable]
public enum TechnologyRevealRequirementKind : byte
{
    RevealedTechnology,
    ResearchedTechnology,
    CompletedExperiment,
    ScanEntity,
    MachineInsertion,
    DeconstructEntity,
    ServerTrigger,
}

[DataDefinition, Serializable, NetSerializable]
[ImplicitDataDefinitionForInheritors]
public abstract partial record TechnologyRevealRequirement
{
    [DataField(required: true)] public string Id = string.Empty;
    [DataField] public TechnologyRevealRequirementKind Kind;
    [DataField] public int Target = 1;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial record RevealedTechnologyRevealRequirement : TechnologyRevealRequirement
{
    [DataField(required: true)] public ProtoId<TechnologyPrototype> Technology;
    public RevealedTechnologyRevealRequirement() => Kind = TechnologyRevealRequirementKind.RevealedTechnology;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial record ResearchedTechnologyRevealRequirement : TechnologyRevealRequirement
{
    [DataField(required: true)] public ProtoId<TechnologyPrototype> Technology;
    public ResearchedTechnologyRevealRequirement() => Kind = TechnologyRevealRequirementKind.ResearchedTechnology;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial record CompletedExperimentRevealRequirement : TechnologyRevealRequirement
{
    [DataField(required: true)] public ProtoId<ResearchExperimentPrototype> Experiment;
    public CompletedExperimentRevealRequirement() => Kind = TechnologyRevealRequirementKind.CompletedExperiment;
}

[DataDefinition, Serializable, NetSerializable]
public partial record ScanEntityRevealRequirement : TechnologyRevealRequirement
{
    [DataField] public string? RequiredEntityPrototype;
    [DataField] public List<string> RequiredTags = new();
    public ScanEntityRevealRequirement() => Kind = TechnologyRevealRequirementKind.ScanEntity;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial record MachineInsertionRevealRequirement : ScanEntityRevealRequirement
{
    [DataField] public string? RequiredMachinePrototype;
    public MachineInsertionRevealRequirement() => Kind = TechnologyRevealRequirementKind.MachineInsertion;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial record DeconstructEntityRevealRequirement : TechnologyRevealRequirement
{
    [DataField] public string? RequiredEntityPrototype;
    [DataField] public List<string> RequiredTags = new();
    public DeconstructEntityRevealRequirement() => Kind = TechnologyRevealRequirementKind.DeconstructEntity;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial record ServerTriggerRevealRequirement : TechnologyRevealRequirement
{
    [DataField(required: true)] public string TriggerId = string.Empty;
    public ServerTriggerRevealRequirement() => Kind = TechnologyRevealRequirementKind.ServerTrigger;
}

[DataDefinition]
public partial record struct GenericUnlock()
{
    [DataField] public object? PurchaseEvent = null;
    [DataField] public string UnlockDescription = string.Empty;
}

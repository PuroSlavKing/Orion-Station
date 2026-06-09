using Robust.Shared.Serialization;

namespace Content.Orion.Shared.Research;

[DataDefinition, Serializable, NetSerializable]
public partial record struct ResearchPointAmount
{
    [DataField(required: true)]
    public string Type;

    [DataField]
    public int Amount;
}

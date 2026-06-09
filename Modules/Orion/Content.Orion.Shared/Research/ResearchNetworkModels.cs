using Robust.Shared.GameObjects;
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

[DataDefinition, Serializable, NetSerializable]
public partial record struct ResearchLogEntry
{
    [DataField]
    public TimeSpan Timestamp;

    [DataField(required: true)]
    public string Category;

    [DataField(required: true)]
    public string Message;

    [DataField]
    public NetEntity? Actor;
}

using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Orion.Shared.Economy.Prototypes;

[Prototype]
public sealed partial class MarketCommodityPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public string Material = default!;

    [DataField]
    public float HighDemandMultiplier = 1.8f;

    [DataField]
    public float LowDemandMultiplier = 0.8f;
}

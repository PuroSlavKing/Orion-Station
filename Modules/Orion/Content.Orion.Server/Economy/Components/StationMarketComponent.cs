using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Orion.Server.Economy.Components;

[RegisterComponent]
public sealed partial class StationMarketComponent : Component
{
    [DataField]
    public Dictionary<string, float> MaterialMultipliers = new();

    [DataField]
    public List<MarketChangeSnapshot> RecentChanges = new();

    [DataField]
    public int MaxRecentChanges = 20;

    [DataField]
    public int ChangeSequence;
}

[Serializable]
public sealed record MarketChangeSnapshot(string Material, float Multiplier, int Sequence);

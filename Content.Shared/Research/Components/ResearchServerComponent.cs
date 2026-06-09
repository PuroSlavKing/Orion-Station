// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Orion.Research;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.Research.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ResearchServerComponent : Component
{
    [AutoNetworkedField, DataField("serverName"), ViewVariables(VVAccess.ReadWrite)]
    public string ServerName = "RND-Server";

    [AutoNetworkedField, DataField("points"), ViewVariables(VVAccess.ReadWrite)]
    public int Points;

    [AutoNetworkedField, DataField]
    public List<ResearchPointAmount> PointBalances =
    [
        new() { Type = "General", Amount = 0 },
    ];

    [AutoNetworkedField, DataField]
    public string NetworkId = "ResearchNet";

    [AutoNetworkedField, DataField]
    public List<ResearchLogEntry> Logs = new();

    [AutoNetworkedField, ViewVariables(VVAccess.ReadOnly)]
    public int Id;

    [ViewVariables(VVAccess.ReadOnly)]
    public List<EntityUid> Clients = new();

    [DataField("nextUpdateTime", customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextUpdateTime = TimeSpan.Zero;

    [DataField("researchConsoleUpdateTime"), ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan ResearchConsoleUpdateTime = TimeSpan.FromSeconds(1);
}

[ByRefEvent]
public readonly record struct ResearchServerPointsChangedEvent(EntityUid Server, int Total, int Delta);

[ByRefEvent]
public readonly record struct ResearchServerPointTypeChangedEvent(EntityUid Server, string Type, int Total, int Delta);

[ByRefEvent]
public record struct ResearchServerGetPointsPerSecondEvent(EntityUid Server, int Points);

[ByRefEvent]
public record struct ResearchServerGetPointsPerSecondByTypeEvent(EntityUid Server, List<ResearchPointAmount> Points);

// SPDX-License-Identifier: MIT

namespace Content.Server.Research.Components;

[RegisterComponent]
public sealed partial class ResearchPointSourceComponent : Component
{
    [DataField]
    public string PointType = "General";

    [DataField("pointspersecond"), ViewVariables(VVAccess.ReadWrite)]
    public int PointsPerSecond;

    [DataField]
    public string? RequiredInfrastructure;

    [DataField("active"), ViewVariables(VVAccess.ReadWrite)]
    public bool Active;
}

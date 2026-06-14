using Content.Orion.Server.EnergyDome.Systems;
using Robust.Shared.Analyzers;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Orion.Server.EnergyDome.Components;

/// <summary>
/// Allows linking the dome generator with the dome itself
/// </summary>
[RegisterComponent, Access(typeof(EnergyDomeSystem))]
public sealed partial class EnergyDomeComponent : Component
{
    /// <summary>
    /// Linked generator that uses energy
    /// </summary>
    [DataField]
    public EntityUid? Generator;
}

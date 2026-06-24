using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Orion.Shared.Economy.Components;

[RegisterComponent]
public sealed partial class CreditHolochipComponent : Component;

[Serializable, NetSerializable]
public enum CreditHolochipVisuals
{
    BaseState,
    OverlayState,
    BaseColor,
}

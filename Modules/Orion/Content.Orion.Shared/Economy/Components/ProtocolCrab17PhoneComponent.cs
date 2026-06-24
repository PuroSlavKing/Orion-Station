using System;
using Robust.Shared.Audio;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Orion.Shared.Economy.Components;

[RegisterComponent]
public sealed partial class ProtocolCrab17PhoneComponent : Component
{
    [DataField]
    public bool Used;

    [DataField]
    public TimeSpan ConfirmationWindow = TimeSpan.FromSeconds(8);

    [DataField]
    public TimeSpan SpawnDelay = TimeSpan.FromSeconds(5);

    [DataField]
    public TimeSpan PendingConfirmationUntil;

    [DataField]
    public EntProtoId MarketPrototype = "CheckoutMachine";

    [DataField]
    public SoundSpecifier ActivateSound = new SoundPathSpecifier("/Audio/Effects/alert.ogg");
}

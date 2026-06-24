using System;
using System.Collections.Generic;
using Content.Shared.Cargo.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager.Attributes;

namespace Content.Orion.Server.Economy.Components;

[RegisterComponent]
public sealed partial class StationAccountComponent : System.ComponentModel.Component
{
    [DataField]
    public string AccountId = string.Empty;

    [DataField]
    public string OwnerName = string.Empty;

    [DataField]
    public int Balance;

    [DataField]
    public ProtoId<CargoAccountPrototype>? Department;

    [DataField]
    public EntityUid? OwningStation;

    [DataField]
    public string? JobId;

    [DataField]
    public int MaxHistory = 67;

    [DataField]
    public List<AccountTransaction> History = new();

    [DataField]
    public bool StartingPayrollReceived;

    [DataField]
    public bool BeingCrabbed;

    [DataField]
    public int MoneyCrabbed;

    [DataField]
    public EntityUid? CurrentCrab17Machine;
}

[Serializable]
public sealed record AccountTransaction(TimeSpan Time, int Delta, int ResultBalance, string Reason, string? ReasonData, NetEntity? Receiver);

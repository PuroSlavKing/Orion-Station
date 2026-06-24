using System;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Orion.Shared.Economy;

[Serializable, NetSerializable]
public enum EconomyCardUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class EconomyCardWithdrawMessage(int amount, string? accountIdOverride) : BoundUserInterfaceMessage
{
    public readonly int Amount = amount;
    public readonly string? AccountIdOverride = accountIdOverride;
}

[Serializable, NetSerializable]
public sealed class EconomyCardSelectAccountMessage(string? accountIdOverride) : BoundUserInterfaceMessage
{
    public readonly string? AccountIdOverride = accountIdOverride;
}

[Serializable, NetSerializable]
public sealed class EconomyCardBoundUiState(string? accountId, int balance) : BoundUserInterfaceState
{
    public readonly string? AccountId = accountId;
    public readonly int Balance = balance;
}

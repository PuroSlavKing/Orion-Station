using Content.Orion.Server.Economy.Rules.Components;
using Content.Orion.Server.Economy.Systems;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Timing;

namespace Content.Orion.Server.Economy.Rules;

public sealed partial class PaydayRuleSystem : GameRuleSystem<PaydayRuleComponent>
{
    [Dependency] private PayrollSystem _payroll = default!;
    [Dependency] private IGameTiming _timing = default!;

    protected override void Started(EntityUid uid, PaydayRuleComponent component, GameRuleComponent gameRule, GameRuleStartedEvent args)
    {
        component.NextPayday = _timing.CurTime + component.Interval;
    }

    protected override void ActiveTick(EntityUid uid, PaydayRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        if (_timing.CurTime < component.NextPayday)
            return;

        component.NextPayday = _timing.CurTime + component.Interval;
        _payroll.ProcessPayroll();
    }
}

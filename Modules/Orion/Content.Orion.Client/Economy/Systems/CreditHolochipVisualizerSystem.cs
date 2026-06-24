using Content.Orion.Shared.Economy.Components;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;

namespace Content.Orion.Client.Economy.Systems;

public sealed partial class CreditHolochipVisualizerSystem : EntitySystem
{
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CreditHolochipComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnAppearanceChange(Entity<CreditHolochipComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is not { } sprite)
            return;

        if (_appearance.TryGetData<string>(ent, CreditHolochipVisuals.BaseState, out var baseState, args.Component))
            _sprite.LayerSetRsiState((ent, sprite), "base", baseState);

        if (_appearance.TryGetData<string>(ent, CreditHolochipVisuals.OverlayState, out var overlayState, args.Component))
            _sprite.LayerSetRsiState((ent, sprite), "overlay", overlayState);

        if (_appearance.TryGetData<Color>(ent, CreditHolochipVisuals.BaseColor, out var baseColor, args.Component))
            _sprite.LayerSetColor((ent, sprite), "base", baseColor);
    }
}

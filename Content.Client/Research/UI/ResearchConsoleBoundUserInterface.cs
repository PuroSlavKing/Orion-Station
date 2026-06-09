using Content.Client._Orion.Research.UI;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client.Research.UI;

[UsedImplicitly]
public sealed class ResearchConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private OrionResearchConsoleMenu? _consoleMenu;

    public ResearchConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _consoleMenu = this.CreateWindow<OrionResearchConsoleMenu>();
        _consoleMenu.SetEntity(Owner);
        _consoleMenu.OnClose += () => _consoleMenu = null;
        _consoleMenu.TechnologyRequested += id => SendMessage(new ConsoleUnlockTechnologyMessage(id));
        _consoleMenu.ServerSelectionRequested += () => SendMessage(new ConsoleServerSelectionMessage());
    }

    public override void OnProtoReload(PrototypesReloadedEventArgs args)
    {
        base.OnProtoReload(args);
        if (!args.WasModified<TechnologyPrototype>() || State is not ResearchConsoleBoundInterfaceState state)
            return;
        _consoleMenu?.UpdateState(state);
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is ResearchConsoleBoundInterfaceState researchState)
            _consoleMenu?.UpdateState(researchState);
    }
}

// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2026 PuroSlavKing <103608145+PuroSlavKing@users.noreply.github.com>
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using System.Numerics;
using Content.Client.Research;
using Content.Common.Research;
using Content.Shared.Research.Prototypes;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.Client._Orion.Research.UI;

public sealed class OrionResearchGraph : LayoutContainer
{
    private const float GridSpacing = 150f;
    private const float MinZoom = 0.5f;
    private const float MaxZoom = 2f;
    private const float ZoomStep = 0.125f;

    [Dependency] private readonly IEntityManager _entity = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private readonly SpriteSystem _sprite;
    private Vector2 _origin = new(45, 250);
    private float _zoom = 1f;
    private bool _dragging;

    public event Action<TechnologyPrototype, ResearchAvailability>? TechnologySelected;

    public OrionResearchGraph()
    {
        IoCManager.InjectDependencies(this);
        _sprite = _entity.System<SpriteSystem>();
        RectClipContent = true;
        HorizontalExpand = true;
        VerticalExpand = true;
        OnKeyBindDown += HandleKeyDown;
        OnKeyBindUp += HandleKeyUp;
    }

    public void SetTechnologies(Dictionary<string, ResearchAvailability> technologies)
    {
        RemoveAllChildren();

        foreach (var entry in technologies)
        {
            if (!_prototypes.TryIndex<TechnologyPrototype>(entry.Key, out var technology))
                continue;

            var node = new OrionResearchNode(technology, entry.Value, _sprite, _prototypes);
            node.Selected += (proto, availability) => TechnologySelected?.Invoke(proto, availability);
            AddChild(node);
            SetPosition(node, _origin + technology.Position * GridSpacing * _zoom);
            node.SetScale(_zoom);
        }
    }

    public void Recenter()
    {
        _origin = new Vector2(45, 250);
        RepositionNodes();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        if (!_dragging)
            return;

        _origin += args.Relative;
        foreach (var child in Children)
            SetPosition(child, child.Position + args.Relative);
        args.Handle();
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        var oldZoom = _zoom;
        _zoom = Math.Clamp(_zoom + (args.Delta.Y > 0 ? ZoomStep : -ZoomStep), MinZoom, MaxZoom);
        if (MathHelper.CloseTo(oldZoom, _zoom))
            return;

        RepositionNodes();
        args.Handle();
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var nodes = Children.OfType<OrionResearchNode>().ToDictionary(node => node.Prototype.ID, node => node);

        foreach (var node in nodes.Values)
        {
            foreach (var prerequisiteId in node.Prototype.TechnologyPrerequisites)
            {
                if (!nodes.TryGetValue(prerequisiteId, out var prerequisite))
                    continue;

                var start = Center(node);
                var end = Center(prerequisite);
                var middleX = (start.X + end.X) / 2f;
                handle.DrawLine(start, new Vector2(middleX, start.Y), Color.White);
                handle.DrawLine(new Vector2(middleX, start.Y), new Vector2(middleX, end.Y), Color.White);
                handle.DrawLine(new Vector2(middleX, end.Y), end, Color.White);
            }
        }
    }

    private void RepositionNodes()
    {
        foreach (var node in Children.OfType<OrionResearchNode>())
        {
            SetPosition(node, _origin + node.Prototype.Position * GridSpacing * _zoom);
            node.SetScale(_zoom);
        }
    }

    private static Vector2 Center(Control control)
        => new(control.PixelPosition.X + control.PixelWidth / 2f, control.PixelPosition.Y + control.PixelHeight / 2f);

    private void HandleKeyDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.Use)
            _dragging = true;
    }

    private void HandleKeyUp(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.Use)
            _dragging = false;
    }

    protected override DragMode GetDragModeFor(Vector2 relativeMousePos)
        => _dragging ? DragMode.None : base.GetDragModeFor(relativeMousePos);

    protected override void ExitedTree()
    {
        OnKeyBindDown -= HandleKeyDown;
        OnKeyBindUp -= HandleKeyUp;
        base.ExitedTree();
    }
}

public sealed class OrionResearchNode : LayoutContainer
{
    private readonly PanelContainer _panel;
    private readonly TextureRect _technologyIcon;
    private readonly TextureRect _disciplineIcon;
    private readonly OrionDrawButton _button;

    public TechnologyPrototype Prototype { get; }
    public ResearchAvailability Availability { get; }

    public event Action<TechnologyPrototype, ResearchAvailability>? Selected;

    public OrionResearchNode(TechnologyPrototype technology, ResearchAvailability availability, SpriteSystem sprite, IPrototypeManager prototypes)
    {
        Prototype = technology;
        Availability = availability;
        SetSize = new Vector2(80, 80);

        _panel = new PanelContainer
        {
            SetSize = new Vector2(80, 80),
            PanelOverride = BuildStyle(availability, false),
        };
        AddChild(_panel);

        _button = new OrionDrawButton
        {
            SetSize = new Vector2(80, 80),
            ModulateSelfOverride = Color.Transparent,
        };
        _button.OnPressed += _ => Selected?.Invoke(Prototype, Availability);
        _button.DrawModeChangedEvent += UpdateStyle;
        _panel.AddChild(_button);

        _technologyIcon = new TextureRect
        {
            SetSize = new Vector2(80, 80),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            TextureScale = new Vector2(2, 2),
            MouseFilter = MouseFilterMode.Ignore,
            Texture = sprite.Frame0(technology.Icon),
        };
        _panel.AddChild(_technologyIcon);

        _disciplineIcon = new TextureRect
        {
            SetSize = new Vector2(20, 20),
            Stretch = TextureRect.StretchMode.KeepAspectCentered,
            TextureScale = new Vector2(2, 2),
            MouseFilter = MouseFilterMode.Ignore,
        };
        if (prototypes.TryIndex(technology.Discipline, out TechDisciplinePrototype? discipline))
            _disciplineIcon.Texture = sprite.Frame0(discipline.Icon);
        _panel.AddChild(_disciplineIcon);
    }

    public void SetScale(float scale)
    {
        var size = new Vector2(80f * scale, 80f * scale);
        SetSize = size;
        _panel.SetSize = size;
        _button.SetSize = size;
        _technologyIcon.SetSize = size;
        _disciplineIcon.SetSize = new Vector2(20f * scale, 20f * scale);
        SetPosition(_disciplineIcon, Vector2.Zero);
    }

    private void UpdateStyle() => _panel.PanelOverride = BuildStyle(Availability, _button.IsHovered);

    private static StyleBoxFlat BuildStyle(ResearchAvailability availability, bool hovered)
    {
        var (background, hover, border) = availability switch
        {
            ResearchAvailability.Researched => (Color.DarkOliveGreen, Color.PaleGreen, Color.LimeGreen),
            ResearchAvailability.Available => (Color.FromHex("#7C7D2A"), Color.FromHex("#ECFA52"), Color.FromHex("#E8FA25")),
            ResearchAvailability.PrereqsMet => (Color.FromHex("#6B572F"), Color.FromHex("#FAD398"), Color.FromHex("#CCA031")),
            _ => (Color.DarkRed, Color.PaleVioletRed, Color.Crimson),
        };

        return new StyleBoxFlat
        {
            BackgroundColor = hovered ? hover : background,
            BorderColor = border,
            BorderThickness = new Thickness(2),
        };
    }
}

public sealed class OrionDrawButton : Button
{
    public event Action? DrawModeChangedEvent;

    protected override void DrawModeChanged()
    {
        base.DrawModeChanged();
        DrawModeChangedEvent?.Invoke();
    }
}

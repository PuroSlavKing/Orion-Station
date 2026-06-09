// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Power.EntitySystems;
using Content.Shared.Research.Components;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    private void InitializeClient()
    {
        SubscribeLocalEvent<ResearchClientComponent, MapInitEvent>(OnClientMapInit);
        SubscribeLocalEvent<ResearchClientComponent, ComponentShutdown>(OnClientShutdown);
        SubscribeLocalEvent<ResearchClientComponent, BoundUIOpenedEvent>(OnClientUIOpen);
        SubscribeLocalEvent<ResearchClientComponent, ConsoleServerSelectionMessage>(OnConsoleSelect);
        SubscribeLocalEvent<ResearchClientComponent, AnchorStateChangedEvent>(OnClientAnchorStateChanged);
        SubscribeLocalEvent<ResearchClientComponent, ResearchClientSyncMessage>(OnClientSyncMessage);
        SubscribeLocalEvent<ResearchClientComponent, ResearchClientServerSelectedMessage>(OnClientSelected);
        SubscribeLocalEvent<ResearchClientComponent, ResearchClientServerDeselectedMessage>(OnClientDeselected);
        SubscribeLocalEvent<ResearchClientComponent, ResearchRegistrationChangedEvent>(OnClientRegistrationChanged);
        SubscribeLocalEvent<ResearchClientComponent, TechnologyDatabaseModifiedEvent>(OnClientDatabaseModified);
    }

    private void OnClientSelected(EntityUid uid, ResearchClientComponent component, ResearchClientServerSelectedMessage args)
    {
        if (!TryGetServerById(uid, args.ServerId, out var serverUid, out var serverComponent))
            return;
        if (!GetServers(uid).Any(server => server.Owner == serverUid.Value))
            return;
        UnregisterClient(uid, component);
        RegisterClient(uid, serverUid.Value, component, serverComponent);
    }

    private void OnClientDeselected(EntityUid uid, ResearchClientComponent component, ResearchClientServerDeselectedMessage args) => UnregisterClient(uid, component);
    private void OnClientSyncMessage(EntityUid uid, ResearchClientComponent component, ResearchClientSyncMessage args) => UpdateClientInterface(uid, component);

    private void OnConsoleSelect(EntityUid uid, ResearchClientComponent component, ConsoleServerSelectionMessage args)
    {
        if (this.IsPowered(uid, EntityManager))
            _uiSystem.TryToggleUi(uid, ResearchClientUiKey.Key, args.Actor);
    }

    private void OnClientRegistrationChanged(EntityUid uid, ResearchClientComponent component, ref ResearchRegistrationChangedEvent args) => UpdateClientInterface(uid, component);
    private void OnClientDatabaseModified(EntityUid uid, ResearchClientComponent component, ref TechnologyDatabaseModifiedEvent args) => SyncClientWithServer(uid, clientComponent: component);
    private void OnClientMapInit(EntityUid uid, ResearchClientComponent component, MapInitEvent args) => TryAutoRegisterClient(uid, component);
    private void OnClientShutdown(EntityUid uid, ResearchClientComponent component, ComponentShutdown args) => UnregisterClient(uid, component);
    private void OnClientUIOpen(EntityUid uid, ResearchClientComponent component, BoundUIOpenedEvent args) => UpdateClientInterface(uid, component);

    private void OnClientAnchorStateChanged(Entity<ResearchClientComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
        {
            if (ent.Comp.Server is null)
                TryAutoRegisterClient(ent, ent.Comp);
        }
        else
            UnregisterClient(ent, ent.Comp);
    }

    private void TryAutoRegisterClient(EntityUid uid, ResearchClientComponent component)
    {
        if (component.Server is not null)
            return;
        var servers = GetServers(uid);
        if (servers.Count > 0)
            RegisterClient(uid, servers[0], component, servers[0]);
    }

    private void UpdateClientInterface(EntityUid uid, ResearchClientComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;
        TryGetClientServer(uid, out _, out var serverComponent, component);
        var names = GetServerNames(uid);
        _uiSystem.SetUiState(uid, ResearchClientUiKey.Key,
            new ResearchClientBoundInterfaceState(names.Length, names, GetServerIds(uid), serverComponent?.Id ?? -1));
    }

    public bool TryGetClientServer(EntityUid uid,
        [NotNullWhen(true)] out EntityUid? server,
        [NotNullWhen(true)] out ResearchServerComponent? serverComponent,
        ResearchClientComponent? component = null)
    {
        server = null;
        serverComponent = null;
        if (!Resolve(uid, ref component, false) || component.Server is null || !TryComp(component.Server, out serverComponent))
            return false;
        server = component.Server;
        return true;
    }
}

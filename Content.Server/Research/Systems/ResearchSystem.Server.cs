// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Server.Power.EntitySystems;
using Content.Shared._Orion.Research;
using Content.Shared.Emag.Systems;
using Content.Shared.Examine;
using Content.Shared.Research.Components;

namespace Content.Server.Research.Systems;

public sealed partial class ResearchSystem
{
    private void InitializeServer()
    {
        SubscribeLocalEvent<ResearchServerComponent, ComponentStartup>(OnServerStartup);
        SubscribeLocalEvent<ResearchServerComponent, MapInitEvent>(OnServerMapInit);
        SubscribeLocalEvent<ResearchServerComponent, ComponentShutdown>(OnServerShutdown);
        SubscribeLocalEvent<ResearchServerComponent, TechnologyDatabaseModifiedEvent>(OnServerDatabaseModified);
        SubscribeLocalEvent<ResearchServerComponent, ExaminedEvent>(OnServerExamined);
        SubscribeLocalEvent<ResearchClientComponent, GotEmaggedEvent>(OnResearchClientEmagged);
    }

    private void OnServerStartup(EntityUid uid, ResearchServerComponent component, ComponentStartup args)
    {
        component.Id = EntityQuery<ResearchServerComponent>(true).Select(x => x.Id).DefaultIfEmpty(-1).Max() + 1;
        EnsurePointBalance(component, "General");
        Dirty(uid, component);
    }

    private void OnServerMapInit(EntityUid uid, ResearchServerComponent component, MapInitEvent args)
    {
        AssignServerName(component);
        InitializeResearchDatabase(uid);
        LogNetworkEvent(uid, "network", Loc.GetString("research-netlog-server-joined", ("server", component.ServerName)));
        Dirty(uid, component);
        ConnectUnregisteredClientsOnServerGrid(uid);
        Timer.Spawn(0, () => SyncServerClients(uid));
    }

    private void SyncServerClients(EntityUid uid)
    {
        if (!TryComp<ResearchServerComponent>(uid, out var server))
            return;
        foreach (var client in server.Clients.ToArray())
            if (TryComp<ResearchClientComponent>(client, out var clientComponent))
                SyncClientWithServer(client, clientComponent: clientComponent);
    }

    private void ConnectUnregisteredClientsOnServerGrid(EntityUid serverUid)
    {
        var grid = Transform(serverUid).GridUid;
        if (grid is null)
            return;
        var query = EntityQueryEnumerator<ResearchClientComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var client, out var xform))
            if (client.Server is null && xform.GridUid == grid)
                TryAutoRegisterClient(uid, client);
    }

    private void OnServerShutdown(EntityUid uid, ResearchServerComponent component, ComponentShutdown args)
    {
        var survivor = GetNetworkServers(uid, component).Where(x => x != uid).OrderBy(GetServerSortId).FirstOrDefault();
        if (survivor != default)
            LogNetworkEvent(survivor, "network", Loc.GetString("research-netlog-server-left", ("server", component.ServerName)));
        foreach (var client in component.Clients.ToArray())
            UnregisterClient(client, uid, serverComponent: component, dirtyServer: false);
    }

    private int GetServerSortId(EntityUid uid) => TryComp<ResearchServerComponent>(uid, out var comp) ? comp.Id : int.MaxValue;

    private void AssignServerName(ResearchServerComponent component)
    {
        if (!string.IsNullOrWhiteSpace(component.ServerName) && component.ServerName != "RND-Server")
            return;
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        component.ServerName = "RND-Server " + new string(Enumerable.Range(0, 6).Select(_ => chars[_random.Next(chars.Length)]).ToArray());
    }

    private void OnServerDatabaseModified(EntityUid uid, ResearchServerComponent component, ref TechnologyDatabaseModifiedEvent args)
    {
        foreach (var client in component.Clients.ToArray())
            RaiseLocalEvent(client, ref args);
    }

    private void OnResearchClientEmagged(EntityUid uid, ResearchClientComponent component, ref GotEmaggedEvent args)
    {
        if (!_emag.CompareFlag(args.Type, EmagType.Interaction) || _emag.CheckFlag(uid, EmagType.Interaction))
            return;
        if (!TryGetClientServer(uid, out var serverUid, out _))
            return;
        LogNetworkEvent(serverUid.Value, "security", Loc.GetString("research-netlog-emag-device-interference", ("device", MetaData(uid).EntityName)));
        args.Handled = true;
    }

    private bool CanRun(EntityUid uid) => this.IsPowered(uid, EntityManager);

    private void UpdateServer(EntityUid uid, int seconds, ResearchServerComponent? component = null)
    {
        if (!Resolve(uid, ref component) || GetNetworkAuthority(uid, component) != uid || !CanRun(uid))
            return;
        foreach (var generation in GetPointGenerationPerSecond(uid, component))
            ModifyServerPoints(uid, generation.Type, generation.Amount * seconds, component);
    }

    private void RegisterClient(EntityUid client, EntityUid server, ResearchClientComponent? clientComponent = null, ResearchServerComponent? serverComponent = null, bool dirtyServer = true)
    {
        if (!Resolve(client, ref clientComponent, false) || !Resolve(server, ref serverComponent, false))
            return;
        server = GetNetworkAuthority(server, serverComponent);
        serverComponent = null;
        if (!Resolve(server, ref serverComponent, false) || serverComponent.Clients.Contains(client))
            return;
        serverComponent.Clients.Add(client);
        clientComponent.Server = server;
        SyncClientWithServer(client, clientComponent: clientComponent);
        if (dirtyServer && !TerminatingOrDeleted(server))
            Dirty(server, serverComponent);
        var ev = new ResearchRegistrationChangedEvent(server);
        RaiseLocalEvent(client, ref ev);
    }

    private void UnregisterClient(EntityUid client, ResearchClientComponent? clientComponent = null, bool dirtyServer = true)
    {
        if (!Resolve(client, ref clientComponent) || clientComponent.Server is not { } server)
            return;
        UnregisterClient(client, server, clientComponent, dirtyServer: dirtyServer);
    }

    private void UnregisterClient(EntityUid client, EntityUid server, ResearchClientComponent? clientComponent = null, ResearchServerComponent? serverComponent = null, bool dirtyServer = true)
    {
        if (!Resolve(client, ref clientComponent, false) || !Resolve(server, ref serverComponent, false))
            return;
        serverComponent.Clients.Remove(client);
        clientComponent.Server = null;
        if (dirtyServer && !TerminatingOrDeleted(server))
            Dirty(server, serverComponent);
        var ev = new ResearchRegistrationChangedEvent(null);
        RaiseLocalEvent(client, ref ev);
    }

    public List<ResearchPointAmount> GetPointGenerationPerSecond(EntityUid uid, ResearchServerComponent? component = null)
    {
        if (!Resolve(uid, ref component) || !CanRun(uid))
            return new();
        var result = new Dictionary<string, int>();
        foreach (var serverUid in GetNetworkServers(uid, component))
        {
            var ev = new ResearchServerGetPointsPerSecondByTypeEvent(serverUid, new());
            if (TryComp<ResearchServerComponent>(serverUid, out var server))
                foreach (var client in server.Clients.ToArray())
                    RaiseLocalEvent(client, ref ev);
            RaiseLocalEvent(serverUid, ref ev);
            foreach (var amount in ev.Points)
                result[amount.Type] = result.GetValueOrDefault(amount.Type) + amount.Amount;
        }
        return result.Select(x => new ResearchPointAmount { Type = x.Key, Amount = x.Value }).ToList();
    }

    public void ModifyServerPoints(EntityUid uid, int points, ResearchServerComponent? component = null) => ModifyServerPoints(uid, "General", points, component);

    public void ModifyServerPoints(EntityUid uid, string type, int points, ResearchServerComponent? component = null)
    {
        if (points == 0 || !Resolve(uid, ref component))
            return;
        var authority = GetNetworkAuthority(uid, component);
        if (authority != uid)
        {
            uid = authority;
            component = null;
            if (!Resolve(uid, ref component, false))
                return;
        }
        EnsurePointBalance(component, type);
        var total = 0;
        for (var i = 0; i < component.PointBalances.Count; i++)
        {
            var balance = component.PointBalances[i];
            if (balance.Type != type)
                continue;
            balance.Amount = Math.Max(0, balance.Amount + points);
            component.PointBalances[i] = balance;
            total = balance.Amount;
            break;
        }
        component.Points = GetPointBalance(uid, "General", component);
        var legacy = new ResearchServerPointsChangedEvent(uid, component.Points, points);
        var typed = new ResearchServerPointTypeChangedEvent(uid, type, total, points);
        foreach (var client in component.Clients.ToArray())
        {
            RaiseLocalEvent(client, ref legacy);
            RaiseLocalEvent(client, ref typed);
        }
        Dirty(uid, component);
    }

    private int GetPointBalance(EntityUid uid, string type, ResearchServerComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return 0;
        return component.PointBalances.FirstOrDefault(x => x.Type == type).Amount;
    }

    private bool HasSufficientPoints(EntityUid uid, IEnumerable<ResearchPointAmount> costs, ResearchServerComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return false;
        return costs.GroupBy(x => x.Type).All(g => GetPointBalance(uid, g.Key, component) >= g.Sum(x => x.Amount));
    }

    private bool TryConsumePoints(EntityUid uid, IEnumerable<ResearchPointAmount> costs, ResearchServerComponent? component = null)
    {
        var list = costs.GroupBy(x => x.Type).Select(g => new ResearchPointAmount { Type = g.Key, Amount = g.Sum(x => x.Amount) }).ToList();
        if (!HasSufficientPoints(uid, list, component))
            return false;
        foreach (var cost in list)
            ModifyServerPoints(uid, cost.Type, -cost.Amount, component);
        return true;
    }

    private IEnumerable<EntityUid> GetNetworkServers(EntityUid uid, ResearchServerComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return [uid];
        var result = new List<EntityUid>();
        var query = EntityQueryEnumerator<ResearchServerComponent>();
        while (query.MoveNext(out var serverUid, out var server))
            if (server.NetworkId == component.NetworkId)
                result.Add(serverUid);
        return result;
    }

    private EntityUid GetNetworkAuthority(EntityUid uid, ResearchServerComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return uid;
        return GetNetworkServers(uid, component).OrderBy(GetServerSortId).FirstOrDefault(uid);
    }

    public string GetResearchLogUserName(EntityUid? user)
    {
        if (user is not { } uid)
            return Loc.GetString("research-netlog-user-system");
        return TryComp(uid, out MetaDataComponent? meta) ? meta.EntityName : ToPrettyString(uid);
    }

    public void LogNetworkEvent(EntityUid uid, string category, string message, EntityUid? actor = null, ResearchServerComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return;
        foreach (var serverUid in GetNetworkServers(uid, component))
        {
            if (!TryComp<ResearchServerComponent>(serverUid, out var server))
                continue;
            server.Logs.Add(new ResearchLogEntry
            {
                Timestamp = _timing.CurTime,
                Category = category,
                Message = message,
                Actor = actor.HasValue ? GetNetEntity(actor.Value) : null,
            });
            if (server.Logs.Count > 100)
                server.Logs.RemoveAt(0);
            Dirty(serverUid, server);
        }
    }

    private static void EnsurePointBalance(ResearchServerComponent component, string type)
    {
        if (!component.PointBalances.Any(x => x.Type == type))
            component.PointBalances.Add(new ResearchPointAmount { Type = type, Amount = 0 });
    }

    private void OnServerExamined(EntityUid uid, ResearchServerComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;
        var generation = GetPointGenerationPerSecond(uid, component);
        args.PushMarkup(Loc.GetString("research-server-examine", ("name", component.ServerName), ("points", generation.Sum(x => x.Amount))));
    }
}

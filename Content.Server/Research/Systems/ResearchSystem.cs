// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Radio.EntitySystems;
using Content.Shared.Access.Systems;
using Content.Shared.Popups;
using Content.Shared.Research.Components;
using Content.Shared.Research.Systems;
using JetBrains.Annotations;
using Robust.Server.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Research.Systems;

[UsedImplicitly]
public sealed partial class ResearchSystem : SharedResearchSystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        InitializeClient();
        InitializeConsole();
        InitializeSource();
        InitializeServer();
        InitializeExperiments();
        InitializeDiscovery();
        SubscribeLocalEvent<TechnologyDatabaseComponent, ResearchRegistrationChangedEvent>(OnDatabaseRegistrationChanged);
    }

    public bool TryGetServerById(EntityUid client, int id, [NotNullWhen(true)] out EntityUid? serverUid, [NotNullWhen(true)] out ResearchServerComponent? serverComponent)
    {
        serverUid = null;
        serverComponent = null;
        foreach (var (uid, server) in GetServers(client))
        {
            if (server.Id != id)
                continue;
            serverUid = uid;
            serverComponent = server;
            return true;
        }
        return false;
    }

    public string[] GetServerNames(EntityUid client) => GetServers(client).Select(x => x.Comp.ServerName).ToArray();
    public int[] GetServerIds(EntityUid client) => GetServers(client).Select(x => x.Comp.Id).ToArray();

    public List<Entity<ResearchServerComponent>> GetServers(EntityUid client)
    {
        var xform = Transform(client);
        if (xform.GridUid is not { } grid)
            return new();
        var servers = new HashSet<Entity<ResearchServerComponent>>();
        _lookup.GetGridEntities(grid, servers);
        return servers.OrderBy(ent => ent.Comp.Id).ToList();
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<ResearchServerComponent>();
        while (query.MoveNext(out var uid, out var server))
        {
            if (server.NextUpdateTime > _timing.CurTime)
                continue;
            server.NextUpdateTime = _timing.CurTime + server.ResearchConsoleUpdateTime;
            UpdateServer(uid, (int) server.ResearchConsoleUpdateTime.TotalSeconds, server);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;

using DotRecast.Core.Numerics;
using DotRecast.Detour;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Pathfinding;

namespace Sanctuary.Game.Tests;

/// <summary>
/// DotRecast never returns from a navmesh query for a point far enough out: its loop over the tile columns runs to
/// int.MaxValue. One client path request with such a point could freeze the whole Gateway, so
/// <see cref="NavMeshPathfinder"/> refuses points off the navmesh's tiles before asking DotRecast.
/// </summary>
[TestClass]
public sealed class NavMeshPathfinderTests
{
    private const float TileSize = 64f;
    private const int MaxVertsPerPoly = 6;

    // The test navmesh has one flat tile, in grid cell (1, 2): X from 64 to 128, Z from 128 to 192, at Y 0.
    private const int TileX = 1;
    private const int TileZ = 2;

    private static readonly Vector3 OnTheTile = new(70f, 0f, 140f);

    private static readonly Vector3 FabledRealmsSpawn = new(-1904.883f, -39.7098f, 412.6024f);

    private static readonly Vector3[] FarAway =
    [
        new(1.4e11f, 0f, 150f),
        new(70f, 0f, 1e20f),
        new(float.MaxValue, 0f, 150f),
        new(-1e20f, 0f, 150f),
        new(70f, 1e20f, 150f),
        new(float.NaN, 0f, 150f),
        new(float.PositiveInfinity, 0f, 150f),
        new(70f, float.NaN, 150f)
    ];

    [TestMethod]
    public void PointsOnTheTile_StillGetAPath()
    {
        var pathfinder = new NavMeshPathfinder(CreateOneTileNavMesh());
        var waypoints = new Queue<Vector3>();

        Assert.IsTrue(pathfinder.TryFindPath(OnTheTile, new Vector3(120f, 0f, 180f), waypoints));
        Assert.AreEqual(new Vector3(120f, 0f, 180f), waypoints.Last());

        // A point just past the tile's edge is still within reach of the polygon search, and gets the edge.
        Assert.IsTrue(pathfinder.TryFindPath(OnTheTile, new Vector3(129f, 0f, 150f), waypoints));
        Assert.AreEqual(new Vector3(128f, 0f, 150f), waypoints.Last());
    }

    [TestMethod]
    public void PointsFarOffTheNavMesh_GetNoPath_AtOnce()
    {
        AssertNoPathToFarAwayPoints(new NavMeshPathfinder(CreateOneTileNavMesh()), OnTheTile);
    }

    [TestMethod]
    public void NavMeshWithoutTiles_GivesNoPath()
    {
        var navMesh = new DtNavMesh();
        var navMeshParams = new DtNavMeshParams { orig = new RcVec3f(0f, 0f, 0f), tileWidth = TileSize, tileHeight = TileSize, maxTiles = 4, maxPolys = 4 };
        Assert.IsTrue(navMesh.Init(in navMeshParams, MaxVertsPerPoly).Succeeded());

        AssertNoPathToFarAwayPoints(new NavMeshPathfinder(navMesh), OnTheTile);
    }

    [TestMethod]
    public void FabledRealmsNavMesh_FarAwayPoints_GetNoPath_AtOnce()
    {
        // The deployed navmesh isn't in the repository: tools/deploy/fetch-zone-geometry.sh fetches it into
        // src/Resources. Where a developer has fetched it, this runs the check against it.
        var navMeshPath = FindFetchedNavMesh("FabledRealms");
        if (navMeshPath is null)
        {
            Assert.Inconclusive("FabledRealms.navmesh isn't fetched; run tools/deploy/fetch-zone-geometry.sh to test against it.");
            return;
        }

        Assert.IsTrue(NavMeshLoader.TryLoad(navMeshPath, NullLogger.Instance, out var navMesh));

        AssertNoPathToFarAwayPoints(new NavMeshPathfinder(navMesh!), FabledRealmsSpawn);
    }

    private static void AssertNoPathToFarAwayPoints(NavMeshPathfinder pathfinder, Vector3 start)
    {
        var paths = new List<bool>();

        // On a thread of its own: without the check, DotRecast never returns from the first of these.
        var thread = new Thread(() =>
        {
            foreach (var point in FarAway)
            {
                paths.Add(pathfinder.TryFindPath(start, point, new Queue<Vector3>()));
                paths.Add(pathfinder.TryFindPath(point, start, new Queue<Vector3>()));
            }
        }) { IsBackground = true };

        thread.Start();

        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(10)), "A navmesh query for a far-away point never returned.");
        Assert.AreEqual(FarAway.Length * 2, paths.Count);
        CollectionAssert.DoesNotContain(paths, true);
    }

    private static DtNavMesh CreateOneTileNavMesh()
    {
        var navMesh = new DtNavMesh();
        var navMeshParams = new DtNavMeshParams { orig = new RcVec3f(0f, 0f, 0f), tileWidth = TileSize, tileHeight = TileSize, maxTiles = 4, maxPolys = 4 };
        Assert.IsTrue(navMesh.Init(in navMeshParams, MaxVertsPerPoly).Succeeded());

        const float cellSize = 0.5f;
        const int cells = (int)(TileSize / cellSize);
        const int none = 0xffff;

        var bmin = new RcVec3f(TileX * TileSize, 0f, TileZ * TileSize);

        var data = DtNavMeshBuilder.CreateNavMeshData(new DtNavMeshCreateParams
        {
            // One square polygon over the whole tile. Vertices count cells from bmin; unused slots and edges without
            // a neighbour are "none".
            verts = [0, 0, 0, 0, 0, cells, cells, 0, cells, cells, 0, 0],
            vertCount = 4,
            polys = [0, 1, 2, 3, none, none, none, none, none, none, none, none],
            polyFlags = [1],
            polyAreas = [0],
            polyCount = 1,
            nvp = MaxVertsPerPoly,
            tileX = TileX,
            tileZ = TileZ,
            bmin = bmin,
            bmax = new RcVec3f(bmin.X + TileSize, 1f, bmin.Z + TileSize),
            walkableHeight = 2f,
            walkableRadius = 0.5f,
            walkableClimb = 1f,
            cs = cellSize,
            ch = cellSize,
            buildBvTree = true
        });

        Assert.IsTrue(navMesh.AddTile(data, 0, 0, out _).Succeeded());

        return navMesh;
    }

    private static string? FindFetchedNavMesh(string zoneName)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Resources", "NavMeshes", zoneName + ".navmesh");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}

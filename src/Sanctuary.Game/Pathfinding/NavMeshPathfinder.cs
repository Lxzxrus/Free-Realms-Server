using System;
using System.Collections.Generic;
using System.Numerics;

using DotRecast.Core.Numerics;
using DotRecast.Detour;

namespace Sanctuary.Game.Pathfinding;

public sealed class NavMeshPathfinder
{
    private const int MaxPathPolys = 256;
    private const int MaxSearchIterations = 2048;
    private const int MaxStraightPathPoints = 64;

    private static readonly RcVec3f NearestPolyExtents = new(2f, 4f, 2f);

    // Evergrove: how far outside the navmesh's tiles a point may lie and still be looked up. The polygon search only
    // reaches NearestPolyExtents from a point, so a point further out finds nothing anyway.
    private const float TileBoundsMargin = 8f;

    private readonly object _lock = new();
    private readonly DtNavMeshQuery _query;
    private readonly DtQueryDefaultFilter _filter = new();
    private readonly long[] _pathPolys = new long[MaxPathPolys];
    private readonly DtStraightPath[] _straightPath = new DtStraightPath[MaxStraightPathPoints];

    private readonly float _minX = float.PositiveInfinity;
    private readonly float _maxX = float.NegativeInfinity;
    private readonly float _minZ = float.PositiveInfinity;
    private readonly float _maxZ = float.NegativeInfinity;

    public NavMeshPathfinder(DtNavMesh navMesh)
    {
        _query = new DtNavMeshQuery(navMesh);

        // Evergrove: the area the tiles cover, from the navmesh's origin, tile size and each tile's place in its grid.
        // A navmesh without tiles covers nothing, so every point is refused.
        var navMeshParams = navMesh.GetParams();

        for (var i = 0; i < navMesh.GetMaxTiles(); i++)
        {
            if (navMesh.GetTile(i)?.data?.header is not { } header)
                continue;

            var tileX = navMeshParams.orig.X + header.x * navMeshParams.tileWidth;
            var tileZ = navMeshParams.orig.Z + header.y * navMeshParams.tileHeight;

            _minX = MathF.Min(_minX, tileX - TileBoundsMargin);
            _maxX = MathF.Max(_maxX, tileX + navMeshParams.tileWidth + TileBoundsMargin);
            _minZ = MathF.Min(_minZ, tileZ - TileBoundsMargin);
            _maxZ = MathF.Max(_maxZ, tileZ + navMeshParams.tileHeight + TileBoundsMargin);
        }
    }

    public bool TryFindPath(Vector3 start, Vector3 goal, Queue<Vector3> waypoints)
    {
        // Evergrove: DotRecast looks a point up by looping over the tile columns its search box spans, and for an X or Z
        // far enough out (about 1.4e11 with 64-unit tiles) the last column saturates at int.MaxValue, so that loop never
        // ends. One client path request could freeze the Gateway that way, and a pet following its owner out there the
        // zone's tick. A point that isn't finite or lies off the tiles has no polygon near it anyway, so it gets no path
        // without asking DotRecast.
        if (!IsOnTiles(start) || !IsOnTiles(goal))
            return false;

        var startPosition = new RcVec3f(start.X, start.Y, start.Z);
        var goalPosition = new RcVec3f(goal.X, goal.Y, goal.Z);

        lock (_lock)
        {
            _query.FindNearestPoly(startPosition, NearestPolyExtents, _filter, out var startRef, out var startOnMesh, out _);
            _query.FindNearestPoly(goalPosition, NearestPolyExtents, _filter, out var goalRef, out var goalOnMesh, out _);

            if (startRef == 0 || goalRef == 0)
                return false;

            var status = _query.InitSlicedFindPath(startRef, goalRef, startOnMesh, goalOnMesh, _filter, 0);
            if (status.Failed())
                return false;

            _query.UpdateSlicedFindPath(MaxSearchIterations, out _);

            status = _query.FinalizeSlicedFindPath(_pathPolys, out var pathCount, MaxPathPolys);
            if (status.Failed() || pathCount == 0)
                return false;

            var endPosition = goalOnMesh;
            if (_pathPolys[pathCount - 1] != goalRef)
                _query.ClosestPointOnPoly(_pathPolys[pathCount - 1], goalOnMesh, out endPosition, out _);

            status = _query.FindStraightPath(startOnMesh, endPosition, _pathPolys, pathCount, _straightPath, out var straightPathCount,
                MaxStraightPathPoints, 0);
            if (status.Failed() || straightPathCount == 0)
                return false;

            waypoints.Clear();

            for (var point = 1; point < straightPathCount; point++)
            {
                var position = _straightPath[point].pos;
                waypoints.Enqueue(new Vector3(position.X, position.Y, position.Z));
            }

            return waypoints.Count > 0;
        }
    }

    // NaN and the infinities fail every comparison here, so they are refused too.
    private bool IsOnTiles(Vector3 point) =>
        point.X >= _minX && point.X <= _maxX && point.Z >= _minZ && point.Z <= _maxZ && float.IsFinite(point.Y);
}

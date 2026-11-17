using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Logical grid cell data snapshot and non-allocating match/deadlock/shuffle utilities.
/// </summary>
public struct BoardCellData
{
    public bool IsEmpty;
    public TileKind Kind;
    public BlockColor Color;
    public int HitPoints;
    public int LockHitPoints;
    public bool IsLocked;
    public BoosterType Booster;
    public bool HorizontalRocket;
    public TileView View;

    public bool IsPlayableColorBlock => !IsEmpty && Kind == TileKind.ColorBlock && !IsLocked;
    public bool IsPinnedObstacle => !IsEmpty && (Kind == TileKind.WoodCrate || IsLocked);
}

public static class MatchFinder
{
    private static readonly int[] Dr = { -1, 1, 0, 0 };
    private static readonly int[] Dc = { 0, 0, -1, 1 };
    private static readonly Queue<Vector2Int> BfsQueue = new Queue<Vector2Int>(128);

    /// <summary>
    /// Finds the connected group of unlocked same-color blocks starting at (startRow, startCol).
    /// </summary>
    public static void FindConnectedColorGroup(
        BoardCellData[,] grid,
        int rows,
        int cols,
        int startRow,
        int startCol,
        List<Vector2Int> results,
        bool[,] visited)
    {
        results.Clear();
        if (startRow < 0 || startRow >= rows || startCol < 0 || startCol >= cols)
        {
            return;
        }

        BoardCellData origin = grid[startRow, startCol];
        if (!origin.IsPlayableColorBlock)
        {
            return;
        }

        BlockColor targetColor = origin.Color;
        BfsQueue.Clear();
        visited[startRow, startCol] = true;
        var originPos = new Vector2Int(startRow, startCol);
        BfsQueue.Enqueue(originPos);
        results.Add(originPos);

        while (BfsQueue.Count > 0)
        {
            Vector2Int curr = BfsQueue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                int nr = curr.x + Dr[d];
                int nc = curr.y + Dc[d];
                if (nr < 0 || nr >= rows || nc < 0 || nc >= cols || visited[nr, nc])
                {
                    continue;
                }

                BoardCellData neighbor = grid[nr, nc];
                if (neighbor.IsPlayableColorBlock && neighbor.Color == targetColor)
                {
                    visited[nr, nc] = true;
                    var pos = new Vector2Int(nr, nc);
                    BfsQueue.Enqueue(pos);
                    results.Add(pos);
                }
            }
        }
    }

    /// <summary>
    /// Populates groupSizes[r, c] for every unlocked ColorBlock on the board without GC churn.
    /// </summary>
    public static void ComputeAllGroupSizes(
        BoardCellData[,] grid,
        int rows,
        int cols,
        int[,] groupSizes,
        bool[,] visited,
        List<Vector2Int> scratchGroup)
    {
        Array.Clear(visited, 0, visited.Length);
        Array.Clear(groupSizes, 0, groupSizes.Length);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (visited[r, c] || !grid[r, c].IsPlayableColorBlock)
                {
                    continue;
                }

                FindConnectedColorGroup(grid, rows, cols, r, c, scratchGroup, visited);
                int count = scratchGroup.Count;
                for (int i = 0; i < count; i++)
                {
                    Vector2Int cell = scratchGroup[i];
                    groupSizes[cell.x, cell.y] = count;
                }
            }
        }
    }

    /// <summary>
    /// Returns true if at least one clickable >=2 match or active booster exists.
    /// </summary>
    public static bool HasAnyValidMove(BoardCellData[,] grid, int rows, int cols)
    {
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                BoardCellData cell = grid[r, c];
                if (cell.IsEmpty || cell.IsLocked)
                {
                    continue;
                }

                if (cell.Kind == TileKind.Rocket || cell.Kind == TileKind.Bomb || cell.Kind == TileKind.Disco)
                {
                    return true;
                }

                if (cell.Kind != TileKind.ColorBlock)
                {
                    continue;
                }

                if (c + 1 < cols && grid[r, c + 1].IsPlayableColorBlock && grid[r, c + 1].Color == cell.Color)
                {
                    return true;
                }

                if (r + 1 < rows && grid[r + 1, c].IsPlayableColorBlock && grid[r + 1, c].Color == cell.Color)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the highest-priority playable group for SolvePartial power-up:
    /// prefers groups matching active goals, adjacent to obstacles, and larger sizes.
    /// </summary>
    public static bool FindBestSolvePartialGroup(
        BoardCellData[,] grid,
        int rows,
        int cols,
        bool[,] visited,
        List<Vector2Int> scratchGroup,
        List<Vector2Int> bestGroup,
        Func<BlockColor, bool> isActiveGoalColor)
    {
        bestGroup.Clear();
        Array.Clear(visited, 0, visited.Length);
        int bestScore = int.MinValue;

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (visited[r, c] || !grid[r, c].IsPlayableColorBlock)
                {
                    continue;
                }

                FindConnectedColorGroup(grid, rows, cols, r, c, scratchGroup, visited);
                if (scratchGroup.Count < 2)
                {
                    continue;
                }

                int score = scratchGroup.Count * 10;
                BlockColor col = grid[r, c].Color;
                if (isActiveGoalColor != null && isActiveGoalColor(col))
                {
                    score += 40;
                }

                if (TouchesAnyObstacle(grid, rows, cols, scratchGroup))
                {
                    score += 25;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestGroup.Clear();
                    bestGroup.AddRange(scratchGroup);
                }
            }
        }

        return bestGroup.Count >= 2;
    }

    /// <summary>
    /// Shuffles unlocked ColorBlocks in place (keeping crates and locked cells pinned)
    /// and guarantees a valid adjacent pair when >= 2 matching blocks exist.
    /// </summary>
    public static void ShufflePlayableColorBlocks(BoardCellData[,] grid, int rows, int cols, System.Random rng)
    {
        var positions = new List<Vector2Int>(rows * cols);
        var colors = new List<BlockColor>(rows * cols);
        var views = new List<TileView>(rows * cols);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (grid[r, c].IsPlayableColorBlock)
                {
                    positions.Add(new Vector2Int(r, c));
                    colors.Add(grid[r, c].Color);
                    views.Add(grid[r, c].View);
                }
            }
        }

        if (positions.Count < 2)
        {
            return;
        }

        const int maxAttempts = 8;
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            for (int i = positions.Count - 1; i > 0; i--)
            {
                int swap = rng.Next(i + 1);
                (colors[i], colors[swap]) = (colors[swap], colors[i]);
                (views[i], views[swap]) = (views[swap], views[i]);
            }

            ApplyShuffledLists(grid, positions, colors, views);
            if (HasAnyValidMove(grid, rows, cols))
            {
                return;
            }
        }

        ForceAdjacentPair(grid, rows, cols, positions);
    }

    private static bool TouchesAnyObstacle(BoardCellData[,] grid, int rows, int cols, List<Vector2Int> group)
    {
        for (int i = 0; i < group.Count; i++)
        {
            Vector2Int p = group[i];
            for (int d = 0; d < 4; d++)
            {
                int nr = p.x + Dr[d];
                int nc = p.y + Dc[d];
                if (nr >= 0 && nr < rows && nc >= 0 && nc < cols && grid[nr, nc].IsPinnedObstacle)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void ApplyShuffledLists(
        BoardCellData[,] grid,
        List<Vector2Int> positions,
        List<BlockColor> colors,
        List<TileView> views)
    {
        for (int i = 0; i < positions.Count; i++)
        {
            Vector2Int pos = positions[i];
            BoardCellData cell = grid[pos.x, pos.y];
            cell.Color = colors[i];
            cell.View = views[i];
            grid[pos.x, pos.y] = cell;
        }
    }

    private static void ForceAdjacentPair(BoardCellData[,] grid, int rows, int cols, List<Vector2Int> playablePositions)
    {
        for (int i = 0; i < playablePositions.Count; i++)
        {
            Vector2Int anchor = playablePositions[i];
            Vector2Int? adj = FindPlayableNeighbor(grid, rows, cols, anchor);
            if (!adj.HasValue)
            {
                continue;
            }

            BlockColor neededColor = grid[anchor.x, anchor.y].Color;
            for (int j = 0; j < playablePositions.Count; j++)
            {
                Vector2Int candidate = playablePositions[j];
                if (candidate == anchor || candidate == adj.Value)
                {
                    continue;
                }

                if (grid[candidate.x, candidate.y].Color == neededColor)
                {
                    SwapCells(grid, adj.Value, candidate);
                    return;
                }
            }
        }
    }

    private static Vector2Int? FindPlayableNeighbor(BoardCellData[,] grid, int rows, int cols, Vector2Int p)
    {
        for (int d = 0; d < 4; d++)
        {
            int nr = p.x + Dr[d];
            int nc = p.y + Dc[d];
            if (nr >= 0 && nr < rows && nc >= 0 && nc < cols && grid[nr, nc].IsPlayableColorBlock)
            {
                return new Vector2Int(nr, nc);
            }
        }

        return null;
    }

    private static void SwapCells(BoardCellData[,] grid, Vector2Int a, Vector2Int b)
    {
        BoardCellData ca = grid[a.x, a.y];
        BoardCellData cb = grid[b.x, b.y];
        (ca.Color, cb.Color) = (cb.Color, ca.Color);
        (ca.View, cb.View) = (cb.View, ca.View);
        grid[a.x, a.y] = ca;
        grid[b.x, b.y] = cb;
    }
}

using System.Collections.Generic;
using UnityEngine;
using Internal.Core;

namespace Internal.Logic
{
    public static class MatchFinder
    {
        // Reusable generic collections to avoid GC (Single-threaded only)
        private static readonly Queue<Block> _reuseQueue = new Queue<Block>(100);

        public static void FindConnectedGroup(Block startBlock, Block[,] grid, int rows, int cols, List<Block> resultBuffer)
        {
            resultBuffer.Clear();
            if (startBlock == null) return;

            int targetColor = startBlock.ColorId;
            bool[,] visited = new bool[rows, cols]; // Allocating bool array is small but still alloc. 
            // Optimization: Pass visited array if we want 0 alloc, but handling reset is tricky.
            // For now, eliminating List and Queue alloc is huge win.
            
            _reuseQueue.Clear();
            _reuseQueue.Enqueue(startBlock);
            
            visited[startBlock.Row, startBlock.Col] = true;
            resultBuffer.Add(startBlock);

            int[] dx = { 0, 0, 1, -1 };
            int[] dy = { 1, -1, 0, 0 };

            while (_reuseQueue.Count > 0)
            {
                Block current = _reuseQueue.Dequeue();

                for (int i = 0; i < 4; i++)
                {
                    int nx = current.Col + dx[i];
                    int ny = current.Row + dy[i];

                    if (nx >= 0 && nx < cols && ny >= 0 && ny < rows)
                    {
                        if (!visited[ny, nx])
                        {
                            Block neighbor = grid[ny, nx];
                            if (neighbor != null && neighbor.ColorId == targetColor)
                            {
                                visited[ny, nx] = true;
                                resultBuffer.Add(neighbor);
                                _reuseQueue.Enqueue(neighbor);
                            }
                        }
                    }
                }
            }
        }
    }
}

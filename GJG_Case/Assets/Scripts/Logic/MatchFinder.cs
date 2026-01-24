using System.Collections.Generic;
using UnityEngine;
using Internal.Core;

namespace Internal.Logic
{
    public static class MatchFinder
    {
        // Reusable generic collections to avoid GC (Single-threaded only)
        private static readonly Queue<Block> _reuseQueue = new Queue<Block>(100);

        public static void FindConnectedGroup(Block startBlock, Block[,] grid, int rows, int cols, List<Block> resultBuffer, bool[,] visitedBuffer)
        {
            resultBuffer.Clear();
            if (startBlock == null) return;
            
            // System.Array.Clear(visitedBuffer, 0, visitedBuffer.Length); 
            // Caller must ensure visitedBuffer is clear or handled. 
            // To be safe and efficient, we can clear only used parts? 
            // No, for "Fill" style algorithms, we need a clean slate usually.
            // But if we are iterating whole board, we might want to keep visited state.
            // Let's assume caller manages the "reset" of visitedBuffer if they want a fresh search.
            // For a single group find, we usually need a fresh visited.
            
            // However, to strictly follow "No GC", we delegate clearing to caller or do it here.
            // Doing it here loop-free is Array.Clear, which is fast.
            // But if caller calls this in a loop (UpdateAllIcons), they might share visited!
            // So we DO NOT clear here. We use what is passed.
            // If this is a standalone check, caller must clear before passing.

            int targetColor = startBlock.ColorId;
            
            _reuseQueue.Clear();
            _reuseQueue.Enqueue(startBlock);
            
            visitedBuffer[startBlock.Row, startBlock.Col] = true;
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
                        if (!visitedBuffer[ny, nx])
                        {
                            Block neighbor = grid[ny, nx];
                            if (neighbor != null && neighbor.ColorId == targetColor)
                            {
                                visitedBuffer[ny, nx] = true;
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

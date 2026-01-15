using System.Collections.Generic;
using UnityEngine;
using Internal.Core;

namespace Internal.Logic
{
    public static class ShuffleLogic
    {
        public static bool IsDeadlocked(Block[,] grid, int rows, int cols)
        {
            // Check if any block has a neighbor of the same color
            // This is O(N), much faster than finding full groups for every block
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    Block b = grid[y, x];
                    if (b == null) continue;

                    // Check right
                    if (x + 1 < cols)
                    {
                        Block right = grid[y, x + 1];
                        if (right != null && right.ColorId == b.ColorId) return false;
                    }

                    // Check up
                    if (y + 1 < rows)
                    {
                        Block up = grid[y + 1, x];
                        if (up != null && up.ColorId == b.ColorId) return false;
                    }
                }
            }
            return true;
        }

        public static void ShuffleBoard(Block[,] grid, int rows, int cols, List<Block> allBlocks)
        {
            // Simple Fisher-Yates shuffle of positions
            // 1. Extract all blocks into a list (passed in to avoid allocs if possible, or just create new)
            // 2. Shuffle list
            // 3. Reassign to grid slots
            
            // To guarantee solvable:
            // We can retry shuffle until !IsDeadlocked
            // Or force a pair.
            
            // Let's implement TryShuffle first.
            int maxAttempts = 10;
            for (int i = 0; i < maxAttempts; i++)
            {
                ApplyRandomShuffle(grid, rows, cols, allBlocks);
                if (!IsDeadlocked(grid, rows, cols))
                {
                    return;
                }
            }
            
            // If still deadlocked, force a match
            ApplyRandomShuffle(grid, rows, cols, allBlocks); // Shuffle one last time
            ForceCreateMatch(grid, rows, cols);
        }

        private static void ApplyRandomShuffle(Block[,] grid, int rows, int cols, List<Block> blocks)
        {
            // Shuffle list
            for (int i = 0; i < blocks.Count; i++)
            {
                Block temp = blocks[i];
                int randomIndex = Random.Range(i, blocks.Count);
                blocks[i] = blocks[randomIndex];
                blocks[randomIndex] = temp;
            }

            // Reassign to grid
            int idx = 0;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    if (idx < blocks.Count)
                    {
                        grid[y, x] = blocks[idx];
                        // Don't update visual position yet, caller handles it to animate
                        blocks[idx].Init(y, x, blocks[idx].ColorId, blocks[idx].GetComponent<SpriteRenderer>().sprite); 
                        // Note: Init re-sets name and row/col. Visual update should follow.
                        idx++;
                    }
                }
            }
        }

        private static void ForceCreateMatch(Block[,] grid, int rows, int cols)
        {
            // Find two blocks of same color and put them next to each other
            // 0,0 and 0,1
            Block b1 = grid[0, 0];
            int targetId = b1.ColorId;
            
            // Find another block with same Id
            Block b2 = null;
            int b2X = -1, b2Y = -1;

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    if (x == 0 && y == 0) continue;
                    if (grid[y, x].ColorId == targetId)
                    {
                        b2 = grid[y, x];
                        b2X = x;
                        b2Y = y;
                        goto Found;
                    }
                }
            }

            Found:
            if (b2 != null)
            {
                // Swap b2 with 0,1
                Block targetPosBlock = grid[0, 1];
                
                // Swap in grid (0,1 is targetPosBlock, b2Y,b2X is b2)
                grid[0, 1] = b2;
                grid[b2Y, b2X] = targetPosBlock;
                
                // Update internal indices
                b2.Init(0, 1, b2.ColorId, b2.GetComponent<SpriteRenderer>().sprite);
                targetPosBlock.Init(b2Y, b2X, targetPosBlock.ColorId, targetPosBlock.GetComponent<SpriteRenderer>().sprite);
            }
        }
    }
}

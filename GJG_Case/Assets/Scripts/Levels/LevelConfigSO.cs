using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data-driven ScriptableObject defining one playable puzzle level.
/// Stored in Assets/Data/Levels so designers can tweak or add levels directly in the Inspector.
/// </summary>
[CreateAssetMenu(menuName = "GJG Case/Level Config", fileName = "Level_New")]
public class LevelConfigSO : ScriptableObject
{
    [Serializable]
    public struct LevelGoalEntry
    {
        public GoalTargetType TargetType;
        [Min(1)]
        public int TargetAmount;
    }

    [Header("Level Identity")]
    [Min(1)]
    public int LevelNumber = 1;
    public string LevelTitle = "Stage 1";

    [Header("Board Dimensions & Colors")]
    [Range(4, 12)]
    public int Rows = 6;
    [Range(4, 9)]
    public int Cols = 5;
    [Range(3, 6)]
    public int ColorCount = 4;
    [Tooltip("0 generates a fresh board every attempt; non-zero gives deterministic layout.")]
    public int RandomSeed = 0;

    [Header("Prototype Obstacles")]
    [Min(0)]
    public int WoodCrateCount = 0;
    [Range(1, 5)]
    public int WoodCrateHitPoints = 2;
    [Min(0)]
    public int LockedBlockCount = 0;
    [Range(1, 5)]
    public int LockedBlockHitPoints = 3;

    [Header("Limit Mode (Time or Moves)")]
    public LevelLimitType LimitType = LevelLimitType.Time;
    [Min(10f)]
    public float TimeLimitSeconds = 60f;
    [Min(5)]
    public int MoveLimit = 20;

    [Header("Booster Creation Group Thresholds")]
    [Min(3)]
    public int RocketThreshold = 5;
    [Min(4)]
    public int BombThreshold = 7;
    [Min(5)]
    public int DiscoThreshold = 9;

    [Header("Level Goals (Up to 4 in TopBar)")]
    public List<LevelGoalEntry> Goals = new List<LevelGoalEntry>();

    [Header("Starting Power-Up Charges (-1 keeps Slot Default)")]
    public int ExtraLimitCharges = -1;
    public int SolvePartialCharges = -1;
    public int UndoCharges = -1;
    public int BreakLockCharges = -1;

    private void OnValidate()
    {
        Rows = Mathf.Clamp(Rows, 4, 12);
        Cols = Mathf.Clamp(Cols, 4, 9);
        ColorCount = Mathf.Clamp(ColorCount, 3, 6);
        WoodCrateCount = Mathf.Max(0, WoodCrateCount);
        WoodCrateHitPoints = Mathf.Clamp(WoodCrateHitPoints, 1, 5);
        LockedBlockCount = Mathf.Max(0, LockedBlockCount);
        LockedBlockHitPoints = Mathf.Clamp(LockedBlockHitPoints, 1, 5);
        RocketThreshold = Mathf.Max(3, RocketThreshold);
        BombThreshold = Mathf.Max(RocketThreshold + 1, BombThreshold);
        DiscoThreshold = Mathf.Max(BombThreshold + 1, DiscoThreshold);
        TimeLimitSeconds = Mathf.Max(10f, TimeLimitSeconds);
        MoveLimit = Mathf.Max(5, MoveLimit);

        if (Goals != null)
        {
            if (Goals.Count > 4)
            {
                Goals.RemoveRange(4, Goals.Count - 4);
            }

            for (int i = 0; i < Goals.Count; i++)
            {
                LevelGoalEntry entry = Goals[i];
                if (entry.TargetAmount < 1)
                {
                    entry.TargetAmount = 1;
                    Goals[i] = entry;
                }
            }
        }
    }
}

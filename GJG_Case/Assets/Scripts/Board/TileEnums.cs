using System;

/// <summary>
/// Six playable block colors matching Assets/Match_Items_internship sprites.
/// </summary>
public enum BlockColor
{
    Blue = 0,
    Green = 1,
    Pink = 2,
    Purple = 3,
    Red = 4,
    Yellow = 5
}

/// <summary>
/// Kind of occupant residing in a board grid cell.
/// </summary>
public enum TileKind
{
    ColorBlock = 0,
    WoodCrate = 1,
    Rocket = 2,
    Bomb = 3,
    Disco = 4
}

/// <summary>
/// Booster tier determined by connected group size (or spawned directly).
/// </summary>
public enum BoosterType
{
    None = 0,
    Rocket = 1,
    Bomb = 2,
    Disco = 3
}

/// <summary>
/// Per-level end condition mode (countdown timer or move limit).
/// </summary>
public enum LevelLimitType
{
    Time = 0,
    Moves = 1
}

/// <summary>
/// Trackable objective types displayed in TopBar GOALS.
/// </summary>
public enum GoalTargetType
{
    Blue = 0,
    Green = 1,
    Pink = 2,
    Purple = 3,
    Red = 4,
    Yellow = 5,
    WoodCrate = 6,
    LockedBlock = 7
}

using System;

/// <summary>
/// Optional interface for gameplay/goal controllers to feed normalized level completion
/// progress (0..1) into UI reactions such as the TopBar mascot expressions.
/// </summary>
public interface ILevelProgressProvider
{
    /// <summary>
    /// Normalized completion ratio in [0, 1].
    /// </summary>
    float NormalizedProgress { get; }

    /// <summary>
    /// True once all level objectives/goals have been completed.
    /// </summary>
    bool IsLevelCompleted { get; }

    /// <summary>
    /// Invoked whenever normalized level progress updates.
    /// </summary>
    event Action<float> OnProgressChanged;
}

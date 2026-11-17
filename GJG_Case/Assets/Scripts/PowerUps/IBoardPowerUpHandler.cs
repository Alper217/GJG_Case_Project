/// <summary>
/// Gameplay contract implemented by the board controller to execute board-targeted
/// power-ups (partial solve, undo last move, and breaking locked blocks).
/// </summary>
public interface IBoardPowerUpHandler
{
    /// <summary>
    /// Returns true if the power-up can currently be used given the board state.
    /// </summary>
    bool CanUsePowerUp(PowerUpType type);

    /// <summary>
    /// Executes the board-side effect of the specified power-up and returns true if consumed.
    /// </summary>
    bool TryExecutePowerUp(PowerUpType type);
}

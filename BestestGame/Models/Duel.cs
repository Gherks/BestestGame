namespace BestestGame.Models;

public class Duel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Game1Id { get; set; }
    public Guid Game2Id { get; set; }
    public bool IsCompleted { get; set; } = false;
    public Guid? WinnerId { get; set; }
}

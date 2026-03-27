namespace BestestGame.Models;

public class Tournament
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<Game> Games { get; set; } = new();
    public List<Duel> Duels { get; set; } = new();
}

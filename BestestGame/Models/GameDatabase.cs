namespace BestestGame.Models;

public class GameDatabase
{
    public List<Game> Games { get; set; } = new();
    public List<Duel> Duels { get; set; } = new();
}

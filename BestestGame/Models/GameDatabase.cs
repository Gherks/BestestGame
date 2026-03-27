namespace BestestGame.Models;

public class GameDatabase
{
    public List<Tournament> Tournaments { get; set; } = new();
    public Guid? CurrentTournamentId { get; set; }
}

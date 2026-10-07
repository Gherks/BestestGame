namespace BestestGame.Components;

// Circuit-scoped UI notification; tournament selection itself remains in GameService.
public sealed class TournamentSelectionNotifications
{
    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();
}

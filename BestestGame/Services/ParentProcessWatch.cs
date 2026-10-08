using System.Runtime.InteropServices;

namespace BestestGame.Services;

// The debugger and dotnet watch can exit without stopping the app. Unix then hands
// the app to another parent, where it would keep holding the development port.
public static class ParentProcessWatch
{
    [DllImport("libc")]
    private static extern int getppid();

    public static void StopWhenOrphaned(IHostApplicationLifetime lifetime)
    {
        var parent = getppid();
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(lifetime.ApplicationStopping))
                {
                    if (getppid() == parent) continue;
                    lifetime.StopApplication();
                    return;
                }
            }
            catch (OperationCanceledException) { }
        });
    }
}

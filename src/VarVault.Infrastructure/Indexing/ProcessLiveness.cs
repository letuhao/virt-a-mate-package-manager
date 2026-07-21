using System.Diagnostics;

namespace VarVault.Infrastructure.Indexing;

/// <summary>Cross-process liveness probe: is a given process id still running? (A12 liveness.)</summary>
public static class ProcessLiveness
{
    public static bool IsAlive(int processId)
    {
        if (processId <= 0)
            return false;
        try
        {
            using var p = Process.GetProcessById(processId);
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // no such process
        }
        catch (InvalidOperationException)
        {
            return false; // process already exited
        }
    }
}

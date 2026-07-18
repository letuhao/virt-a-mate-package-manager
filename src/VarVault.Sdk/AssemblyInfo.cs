using System.Runtime.CompilerServices;

// The job-queue implementation (Infrastructure) mutates SDK job-handle state through
// internal setters, keeping the public surface read-only for consumers.
[assembly: InternalsVisibleTo("VarVault.Infrastructure")]

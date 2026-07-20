namespace VarVault.App.Services;

/// <summary>
/// Reveals a file in the OS file browser (on Windows: Explorer with the file selected). A seam so the
/// library detail "Locate" action is wired to real behavior yet unit-testable with a fake. (doc 26 · G-1.2)
/// </summary>
public interface IFileReveal
{
    void Reveal(string path);
}

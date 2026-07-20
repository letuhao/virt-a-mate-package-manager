using System.Diagnostics;
using System.IO;
using System.Text;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using VarVault.Domain.Content;
using VarVault.Sdk.Import;
using VarVault.Sdk.Settings;

namespace VarVault.Infrastructure.Import;

/// <summary>
/// Extracts an archive's <c>.var</c> entries with SharpCompress, decoding entry names through VarVault's own CJK
/// detection (<see cref="ArchiveEncoding.CustomDecoder"/>) so legacy-encoded Chinese/Japanese names survive intact —
/// the load-bearing reason we don't trust a lib's codepage guess. Reports password/corrupt/low-space as a status,
/// never throwing. When SharpCompress can't read an archive at all, falls back to an <b>optional external 7-Zip</b>
/// (the <c>import.sevenzip_path</c> setting, else an auto-detected system install — never a bundled binary).
/// (doc 30 §6 amendment A1; doc 31 Phase 3.2–3.4.)
/// </summary>
public sealed class ArchiveExtractor(ISettingsService? settings = null) : IArchiveExtractor
{
    private const string SevenZipKey = "import.sevenzip_path";

    public async Task<ArchiveExtractResult> ExtractAsync(string archivePath, string destDir, CancellationToken cancellationToken = default)
    {
        var result = await Task.Run(() => Extract(archivePath, destDir, cancellationToken), cancellationToken).ConfigureAwait(false);
        // Only retry archives SharpCompress genuinely couldn't read; a password/space failure stays as-is (§6 A1).
        if (result.Success || result.Status is ImportSourceStatus.PasswordProtected or ImportSourceStatus.NotEnoughSpace)
            return result;

        var sevenZip = await ResolveSevenZipAsync(cancellationToken).ConfigureAwait(false);
        if (sevenZip is null)
            return result; // no external 7-Zip available → keep the original failure
        return await Task.Run(() => ExtractWithSevenZip(sevenZip, archivePath, destDir, result, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The 7-Zip exe to use: the <c>import.sevenzip_path</c> setting if valid, else an auto-detected install.</summary>
    private async Task<string?> ResolveSevenZipAsync(CancellationToken ct)
    {
        var configured = settings is null ? null : await settings.GetAsync(SevenZipKey, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;
        foreach (var candidate in DefaultSevenZipPaths())
            if (File.Exists(candidate))
                return candidate;
        return null;
    }

    private static IEnumerable<string> DefaultSevenZipPaths()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(pf)) yield return Path.Combine(pf, "7-Zip", "7z.exe");
        if (!string.IsNullOrEmpty(pfx86)) yield return Path.Combine(pfx86, "7-Zip", "7z.exe");
    }

    /// <summary>Extract only <c>*.var</c> via an external 7-Zip, non-interactively (empty password). (§6 A1)</summary>
    private static ArchiveExtractResult ExtractWithSevenZip(string exe, string archivePath, string destDir,
        ArchiveExtractResult originalFailure, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(destDir);
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            };
            foreach (var arg in new[] { "x", "-y", "-p", "-r", $"-o{destDir}", archivePath, "*.var" })
                psi.ArgumentList.Add(arg);

            using var proc = Process.Start(psi);
            if (proc is null)
                return originalFailure;
            proc.WaitForExit();
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException(ct);
            if (proc.ExitCode != 0)
                return originalFailure; // 7-Zip also couldn't read it (e.g. AES header w/o password)

            var count = Directory.EnumerateFiles(destDir, "*.var", SearchOption.AllDirectories).Count();
            return count > 0
                ? new ArchiveExtractResult(true, count, ImportSourceStatus.Ok, null)
                : originalFailure;
        }
        catch (OperationCanceledException) { throw; }
        catch { return originalFailure; }
    }

    private static ArchiveExtractResult Extract(string archivePath, string destDir, CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);
        var options = new ReaderOptions { ArchiveEncoding = new ArchiveEncoding { CustomDecoder = CjkDecode } };
        try
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath, options);

            // Free-space guard: a multi-GB rar mustn't fill the temp drive. (§6)
            var needed = archive.Entries.Where(e => !e.IsDirectory).Sum(e => Math.Max(0, e.Size));
            if (!HasFreeSpace(destDir, needed))
                return Fail(ImportSourceStatus.NotEnoughSpace, "notEnoughSpace");

            var extraction = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };
            var count = 0;
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.IsDirectory)
                    continue;
                if (entry.IsEncrypted)
                    return Fail(ImportSourceStatus.PasswordProtected, "password");
                if (entry.Key is not { } key || !key.EndsWith(".var", StringComparison.OrdinalIgnoreCase))
                    continue;
                entry.WriteToDirectory(destDir, extraction);
                count++;
            }
            return new ArchiveExtractResult(true, count, ImportSourceStatus.Ok, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            var m = (ex.Message ?? string.Empty).ToLowerInvariant();
            return m.Contains("encrypt") || m.Contains("password")
                ? Fail(ImportSourceStatus.PasswordProtected, "password")
                : Fail(ImportSourceStatus.CorruptArchive, "corruptArchive");
        }
    }

    private static ArchiveExtractResult Fail(ImportSourceStatus status, string reason) => new(false, 0, status, reason);

    private static bool HasFreeSpace(string destDir, long needed)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(destDir));
            if (string.IsNullOrEmpty(root))
                return true;
            return new DriveInfo(root).AvailableFreeSpace >= needed;
        }
        catch
        {
            return true; // if we can't tell, don't block
        }
    }

    /// <summary>Decode a raw entry-name byte range: strict UTF-8 → VarVault's CJK detection → lenient UTF-8. (§6)</summary>
    private static string CjkDecode(byte[] data, int index, int count, EncodingType encodingType)
    {
        var raw = new byte[count];
        Array.Copy(data, index, raw, 0, count);

        try { return new UTF8Encoding(false, true).GetString(raw); }
        catch (DecoderFallbackException) { /* not valid UTF-8 → try legacy codepages */ }

        var codepage = EncodingHealthEngine.DetectCodepage(raw);
        if (codepage is not null && EncodingHealthEngine.CodePageFor(codepage) is int page)
        {
            try { return Encoding.GetEncoding(page).GetString(raw); }
            catch { /* fall through */ }
        }
        return Encoding.UTF8.GetString(raw); // lenient last resort
    }
}

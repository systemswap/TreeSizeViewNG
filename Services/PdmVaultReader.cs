using System.Reflection;
using System.Runtime.Versioning;
using Microsoft.Win32;
using TreeSizeViewNG.Models;

namespace TreeSizeViewNG.Services;

/// <summary>Lokale Ansicht eines SOLIDWORKS-PDM-Tresors: Tresorname und Ordner der Ansicht.</summary>
public sealed record PdmVault(string Name, string RootPath);

/// <summary>Eine Datei laut Tresor: lokaler Pfad in der Tresoransicht und Größe der aktuellen Version.</summary>
public readonly record struct PdmFile(string Path, long Size);

public sealed record PdmMergeResult(int VaultFiles, int VaultOnlyFiles, long VaultOnlyBytes, int Mismatches);

/// <summary>
/// Liest über den installierten SOLIDWORKS-PDM-Client, welche Dateien ein Tresor enthält – auch solche,
/// die (noch) nicht auf der Platte liegen. Solche Dateien zeigt der Explorer nur über die
/// PDM-Erweiterung; im Dateisystem existieren sie nicht, ein normaler Scan findet sie daher nicht.
///
/// Zugriff: COM-Objekt "ConisioLib.EdmVault" des PDM-Clients, Anmeldung mit LoginAuto (nutzt die
/// bestehende Anmeldung bzw. zeigt das Anmeldefenster von PDM; die App kennt keine Zugangsdaten).
/// Gelesen wird über die Suchfunktion (IEdmSearch5): Sie liefert Pfad und Größe aller Dateien eines
/// Ordners samt Unterordnern in einer Abfrage. Es wird nur gelesen – nichts abgerufen, ausgecheckt
/// oder geändert.
///
/// Aufrufe gehen spät gebunden über IDispatch (keine Abhängigkeit von der PDM-Interop-Bibliothek, die es
/// nur auf Rechnern mit PDM gibt). Die Ordner-Aufzählung (IEdmPos5) funktioniert so nicht, die Suche schon.
/// Nur unter Windows; ohne PDM-Client gibt es schlicht keine Tresore.
/// </summary>
public static class PdmVaultReader
{
    private const string RegistryKey = @"SOFTWARE\SolidWorks\Applications\PDMWorks Enterprise\Databases";
    private const string ProgId = "ConisioLib.EdmVault";
    private const int LoginCancelled = unchecked((int)0x80040226); // "Operation wurde vom Benutzer abgebrochen"

    /// <summary>
    /// Tresoransichten, die mit dem zu scannenden Ordner zu tun haben, jeweils mit dem Ordner, ab dem
    /// der Tresor abzufragen ist (der tiefere von Scan-Ordner und Tresor-Wurzel).
    /// </summary>
    public static List<(PdmVault Vault, string StartPath)> FindFor(string scanPath)
    {
        var result = new List<(PdmVault, string)>();
        if (!OperatingSystem.IsWindows())
            return result;

        try
        {
            if (Type.GetTypeFromProgID(ProgId) is null)
                return result; // kein PDM-Client installiert

            var scan = Normalize(scanPath);
            foreach (var vault in ReadVaultsFromRegistry())
            {
                var root = Normalize(vault.RootPath);
                if (IsSameOrInside(scan, root))
                    result.Add((vault, scan));       // Scan liegt im Tresor
                else if (IsSameOrInside(root, scan))
                    result.Add((vault, root));       // Tresor liegt im gescannten Ordner
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("PDM-Tresore ermitteln", ex);
        }

        return result;
    }

    [SupportedOSPlatform("windows")]
    private static List<PdmVault> ReadVaultsFromRegistry()
    {
        var vaults = new List<PdmVault>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var hive = RegistryKey_Open(view);
            using var key = hive?.OpenSubKey(RegistryKey);
            if (key is null)
                continue;

            foreach (var name in key.GetSubKeyNames())
            {
                using var vaultKey = key.OpenSubKey(name);
                if (vaultKey?.GetValue("ShellRoot") is string root && root.Length > 0 &&
                    !vaults.Exists(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
                    vaults.Add(new PdmVault(name, root));
            }
        }

        return vaults;
    }

    [SupportedOSPlatform("windows")]
    private static RegistryKey? RegistryKey_Open(RegistryView view)
    {
        try
        {
            return Microsoft.Win32.RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Liest alle Dateien des Tresors ab <paramref name="startPath"/> (samt Unterordnern).
    /// Läuft auf einem eigenen STA-Thread, wie COM es für den PDM-Client verlangt.
    /// </summary>
    public static Task<List<PdmFile>> ReadAsync(PdmVault vault, string startPath, Action<int>? progress, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<List<PdmFile>>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!OperatingSystem.IsWindows())
        {
            completion.SetResult([]);
            return completion.Task;
        }

        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(OperatingSystem.IsWindows() ? Read(vault, startPath, progress, cancellationToken) : []);
            }
            catch (OperationCanceledException)
            {
                completion.SetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                completion.SetException(ex is TargetInvocationException { InnerException: { } inner } ? inner : ex);
            }
        })
        {
            IsBackground = true,
            Name = "PDM " + vault.Name
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    [SupportedOSPlatform("windows")]
    private static List<PdmFile> Read(PdmVault vault, string startPath, Action<int>? progress, CancellationToken cancellationToken)
    {
        var type = Type.GetTypeFromProgID(ProgId) ?? throw new InvalidOperationException("SOLIDWORKS PDM ist nicht installiert.");
        var edmVault = Activator.CreateInstance(type) ?? throw new InvalidOperationException("PDM-Objekt konnte nicht erzeugt werden.");

        try
        {
            Call(edmVault, "LoginAuto", vault.Name, 0);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is System.Runtime.InteropServices.COMException { HResult: LoginCancelled })
        {
            // Anmeldefenster von PDM geschlossen bzw. Anmeldung für diese Sitzung abgelehnt
            throw new InvalidOperationException(Localization.Strings.PdmNotLoggedIn);
        }

        if (Get(edmVault, "IsLoggedIn") is not true)
            throw new InvalidOperationException(Localization.Strings.PdmNotLoggedIn);

        var folder = Call(edmVault, "GetFolderFromPath", startPath)
                     ?? throw new InvalidOperationException($"Ordner ist dem Tresor nicht bekannt: {startPath}");

        var search = Call(edmVault, "CreateSearch") ?? throw new InvalidOperationException("PDM-Suche nicht verfügbar.");
        Set(search, "FindFiles", true);
        Set(search, "FindFolders", false);
        Set(search, "Recursive", true);
        Set(search, "StartFolderID", Get(folder, "ID")!);
        Set(search, "FileName", "%");

        var files = new List<PdmFile>();
        var hit = Call(search, "GetFirstResult");
        while (hit is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Get(hit, "Path") is string path)
                files.Add(new PdmFile(path, Convert.ToInt64(Get(hit, "FileSize") ?? 0)));

            if (files.Count % 250 == 0)
                progress?.Invoke(files.Count);
            hit = Call(search, "GetNextResult");
        }

        progress?.Invoke(files.Count);
        return files;
    }

    /// <summary>
    /// Trägt die Tresor-Dateien in den gescannten Baum ein. Jede Datei zählt genau einmal:
    ///  - lokal vorhanden: sie bleibt unter "Lokal"; ihre Tresor-Größe wird zusätzlich vermerkt
    ///    (Spalte "In Cloud") und mit der lokalen Größe verglichen;
    ///  - nur im Tresor: neuer Eintrag, der wie eine Nur-online-Datei geführt wird.
    /// Darf nur laufen, solange der Baum weder angezeigt noch anderweitig gelesen wird.
    /// </summary>
    public static PdmMergeResult Merge(FolderNode root, string rootPath, IReadOnlyList<PdmFile> files, long minimumFileSize)
    {
        var prefix = Normalize(rootPath) + Path.DirectorySeparatorChar;
        var comparison = StringComparison.OrdinalIgnoreCase;
        var lookups = new Dictionary<FolderNode, Dictionary<string, FolderNode>>(ReferenceEqualityComparer.Instance);
        int vaultOnly = 0, mismatches = 0, counted = 0;
        long vaultOnlyBytes = 0;

        Dictionary<string, FolderNode> Lookup(FolderNode folder)
        {
            if (!lookups.TryGetValue(folder, out var map))
            {
                map = new Dictionary<string, FolderNode>(folder.Children.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var child in folder.Children)
                    map.TryAdd(child.Name, child);
                lookups[folder] = map;
            }
            return map;
        }

        foreach (var file in files)
        {
            var filePath = file.Path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (!filePath.StartsWith(prefix, comparison))
                continue;

            var parts = filePath[prefix.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            // Ordnerkette: fehlende Ordner gibt es nur im Tresor
            var folder = root;
            var blocked = false;
            for (var i = 0; i < parts.Length - 1 && !blocked; i++)
            {
                var map = Lookup(folder);
                if (!map.TryGetValue(parts[i], out var next))
                {
                    next = new FolderNode { Name = parts[i], Parent = folder, Depth = folder.Depth + 1, IsVaultOnly = true };
                    folder.Children.Add(next);
                    map[parts[i]] = next;
                    for (var p = folder; p is not null; p = p.Parent)
                        p.FolderCount++;
                }
                else if (!next.IsFolder)
                {
                    blocked = true; // lokal liegt dort eine Datei gleichen Namens
                }

                folder = next;
            }

            if (blocked)
                continue;

            counted++;
            var name = parts[^1];
            var siblings = Lookup(folder);
            if (siblings.TryGetValue(name, out var existing))
            {
                if (existing.IsFolder || existing.IsOnlineOnly)
                    continue;

                // lokal vorhanden: zählt weiter nur unter "Lokal", Tresor-Größe zusätzlich vermerken
                var differs = existing.SizeBytes != file.Size;
                existing.VaultLocalBytes = file.Size;
                existing.MismatchCount = differs ? 1 : 0;
                if (differs)
                    mismatches++;
                for (var p = folder; p is not null; p = p.Parent)
                {
                    p.VaultLocalBytes += file.Size;
                    if (differs)
                        p.MismatchCount++;
                }
                continue;
            }

            // Nicht im Baum: entweder nur im Tresor – oder lokal vorhanden, aber vom Mindestgrößen-Filter ausgeblendet
            if (file.Size < minimumFileSize || File.Exists(filePath))
                continue;

            var node = new FolderNode
            {
                Name = name,
                Parent = folder,
                Depth = folder.Depth + 1,
                IsFolder = false,
                IsOnlineOnly = true,
                IsVaultOnly = true,
                SizeBytes = file.Size
            };
            folder.Children.Add(node);
            siblings[name] = node;
            vaultOnly++;
            vaultOnlyBytes += file.Size;
            for (var p = folder; p is not null; p = p.Parent)
            {
                p.OnlineOnlyBytes += file.Size;
                p.OnlineOnlyFileCount++;
                p.HasVaultOnlyContent = true;
            }
        }

        return new PdmMergeResult(counted, vaultOnly, vaultOnlyBytes, mismatches);
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>true, wenn <paramref name="path"/> gleich <paramref name="candidate"/> ist oder darin liegt.</summary>
    private static bool IsSameOrInside(string path, string candidate) =>
        string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(candidate + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    // Spät gebundene COM-Aufrufe über IDispatch
    private static object? Get(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);

    private static void Set(object target, string name, object value) =>
        target.GetType().InvokeMember(name, BindingFlags.SetProperty, null, target, [value]);

    private static object? Call(object target, string name, params object[] arguments) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, arguments);
}

# Prüft, was die SOLIDWORKS-PDM-Schnittstelle auf diesem Rechner liefert – als Grundlage dafür, in
# TreeSizeViewNG Tresor-Dateien ohne lokale Kopie anzuzeigen (Spalte "In Cloud").
#
# Das Skript LIEST nur: Registry-Einträge von PDM, die Anmeldung am Tresor über die PDM-Schnittstelle
# und die Dateiliste EINES Ordners. Es ändert nichts, ruft keine Dateien ab und checkt nichts aus.
#
# Aufruf (PowerShell), mit einem Ordner innerhalb der lokalen Tresoransicht:
#   powershell -ExecutionPolicy Bypass -File tools\pdm-probe.ps1 -Path "C:\MeinTresor\Projekte\Beispiel"
#
# Ergebnis: pdm-probe-ergebnis.txt neben diesem Skript. Die Datei enthält Datei- und Ordnernamen aus dem
# angegebenen Ordner – bitte vor dem Weitergeben ansehen.
param(
    [Parameter(Mandatory = $true)][string]$Path,
    [int]$MaxFiles = 15
)

$out = New-Object System.Collections.Generic.List[string]
function Say([string]$text) { $out.Add($text); Write-Host $text }
function Section([string]$title) { Say ''; Say "===== $title" }
function Try-Get([scriptblock]$code) { try { & $code } catch { "FEHLER: $($_.Exception.Message)" } }

Section 'Umgebung'
Say "Datum: $(Get-Date -Format s) | PowerShell $($PSVersionTable.PSVersion) | 64-Bit-Prozess: $([Environment]::Is64BitProcess)"
Say "Pfad: $Path | vorhanden: $(Test-Path -LiteralPath $Path)"

Section 'Registry (PDM-Tresore)'
$names = New-Object System.Collections.Generic.List[string]
foreach ($base in 'HKLM:\SOFTWARE\SolidWorks\Applications\PDMWorks Enterprise',
                  'HKLM:\SOFTWARE\WOW6432Node\SolidWorks\Applications\PDMWorks Enterprise',
                  'HKCU:\SOFTWARE\SolidWorks\Applications\PDMWorks Enterprise') {
    if (-not (Test-Path $base)) { Say "$base : nicht vorhanden"; continue }
    Say "$base : vorhanden"
    Get-ChildItem $base -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
        $key = $_
        $values = foreach ($v in $key.GetValueNames()) {
            if ($v -match '(?i)pass|pwd|secret|token') { "$v=<ausgeblendet>" } else { "$v=$($key.GetValue($v))" }
        }
        Say ("  " + $key.Name.Replace('HKEY_LOCAL_MACHINE', 'HKLM').Replace('HKEY_CURRENT_USER', 'HKCU') + "  [" + ($values -join '; ') + "]")
        if ($key.Name -match '\\Databases\\([^\\]+)$' -or $key.Name -match '\\Vaults\\([^\\]+)$') { if (-not $names.Contains($Matches[1])) { $names.Add($Matches[1]) } }
    }
}
Say "Tresornamen aus der Registry: $($names -join ', ')"

Section 'Ordnerkette nach oben (desktop.ini der Tresoransicht)'
$dir = $Path
while ($dir) {
    $ini = Join-Path $dir 'desktop.ini'
    $attr = Try-Get { (Get-Item -LiteralPath $dir -Force).Attributes }
    $iniText = if (Test-Path -LiteralPath $ini) { ((Get-Content -LiteralPath $ini -ErrorAction SilentlyContinue) -join ' | ') } else { '-' }
    Say "  $dir  [Attribute: $attr]  desktop.ini: $iniText"
    $parent = [IO.Path]::GetDirectoryName($dir)
    if (-not $parent -or $parent -eq $dir) { break }
    $dir = $parent
}

Section 'PDM-Schnittstelle (COM)'
$vault = $null
foreach ($progId in 'ConisioLib.EdmVault', 'ConisioLib.EdmVault.1') {
    try { $vault = New-Object -ComObject $progId; Say "$progId : erzeugt"; break } catch { Say "$progId : $($_.Exception.Message)" }
}
if (-not $vault) { Say 'Kein PDM-Objekt verfügbar – ist der PDM-Client installiert?'; }
else {
    Say 'Mitglieder des Tresor-Objekts:'
    Try-Get { $vault | Get-Member | ForEach-Object { "  $($_.MemberType) $($_.Name)  $($_.Definition)" } } | ForEach-Object { Say $_ }

    # Tresor finden: Namen aus der Registry und Ordnernamen der Kette ausprobieren
    $candidates = New-Object System.Collections.Generic.List[string]
    $names | ForEach-Object { $candidates.Add($_) }
    $d = $Path; while ($d) { $leaf = [IO.Path]::GetFileName($d); if ($leaf -and -not $candidates.Contains($leaf)) { $candidates.Add($leaf) }; $p = [IO.Path]::GetDirectoryName($d); if (-not $p -or $p -eq $d) { break }; $d = $p }

    $loggedIn = $false
    foreach ($name in $candidates) {
        $result = Try-Get { $vault.LoginAuto($name, 0); "angemeldet=$($vault.IsLoggedIn) Wurzel=$($vault.RootFolderPath)" }
        Say "LoginAuto('$name'): $result"
        if ($result -like 'angemeldet=True*') {
            $rootPath = "$($vault.RootFolderPath)"
            if ($Path.StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)) { $loggedIn = $true; Say "-> Tresor '$name' passt zum Pfad"; break }
            Say '-> anderer Tresor (Wurzel passt nicht zum Pfad)'
        }
    }

    if ($loggedIn) {
        Section 'Ordner über die Schnittstelle'
        $folder = $null
        $r = Try-Get { $script:folder = $vault.GetFolderFromPath($Path); "ok: Name=$($script:folder.Name) ID=$($script:folder.ID) LocalPath=$($script:folder.LocalPath)" }
        Say "GetFolderFromPath: $r"
        if ($folder) {
            Say 'Mitglieder des Ordner-Objekts:'
            Try-Get { $folder | Get-Member | ForEach-Object { "  $($_.MemberType) $($_.Name)  $($_.Definition)" } } | ForEach-Object { Say $_ }

            Section "Dateien (höchstens $MaxFiles)"
            $count = 0; $missing = 0; $first = $true
            $r = Try-Get {
                $pos = $folder.GetFirstFilePosition()
                while (-not $pos.IsNull) {
                    $file = $folder.GetNextFile($pos)
                    $script:count++
                    $local = Join-Path $Path $file.Name
                    $exists = Test-Path -LiteralPath $local
                    if (-not $exists) { $script:missing++ }
                    if ($script:count -le $MaxFiles) {
                        if ($script:first) {
                            $script:first = $false
                            Say 'Mitglieder des Datei-Objekts:'
                            Try-Get { $file | Get-Member | ForEach-Object { "  $($_.MemberType) $($_.Name)  $($_.Definition)" } } | ForEach-Object { Say $_ }
                        }
                        $info = @("Name=$($file.Name)", "ID=$($file.ID)", "lokal vorhanden=$exists")
                        if ($exists) { $info += "Größe lokal=$((Get-Item -LiteralPath $local -Force).Length)" }
                        # Kandidaten für die Größe im Tresor (welche es gibt, zeigt die Ausgabe)
                        foreach ($member in 'GetLocalFileSize2', 'GetLocalFileSize') {
                            $info += "$member=" + (Try-Get { $file.$member($folder.ID) })
                        }
                        foreach ($prop in 'CurrentVersion', 'CurrentRevision', 'IsLocked', 'FileSize', 'Size') {
                            $info += "$prop=" + (Try-Get { $file.$prop })
                        }
                        Say ("  " + ($info -join ' | '))
                    }
                }
                'fertig'
            }
            Say "Auflistung: $r | Dateien laut Tresor: $count | davon ohne lokale Kopie: $missing"
            $onDisk = @(Get-ChildItem -LiteralPath $Path -File -Force -ErrorAction SilentlyContinue).Count
            Say "Dateien laut Dateisystem in diesem Ordner: $onDisk"

            Section 'Unterordner'
            $r = Try-Get {
                $pos = $folder.GetFirstSubFolderPosition(); $n = 0
                while (-not $pos.IsNull) {
                    $sub = $folder.GetNextSubFolder($pos); $n++
                    if ($n -le $MaxFiles) { Say "  $($sub.Name) | lokal vorhanden=$(Test-Path -LiteralPath (Join-Path $Path $sub.Name))" }
                }
                "Unterordner laut Tresor: $n"
            }
            Say $r
        }
    }
}

$target = Join-Path $PSScriptRoot 'pdm-probe-ergebnis.txt'
[IO.File]::WriteAllLines($target, $out, (New-Object Text.UTF8Encoding $true))
Write-Host ''
Write-Host "Ergebnis gespeichert: $target"

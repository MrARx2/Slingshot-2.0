param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputPath = (
        Join-Path $PSScriptRoot "Complete_Project_Folder_Structure.md"
    )
)

$ErrorActionPreference = "Stop"
$resolvedRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$comparison = [System.StringComparison]::OrdinalIgnoreCase
$inventoryErrors = [System.Collections.Generic.List[string]]::new()

function Get-RelativeInventoryPath {
    param([string]$FullPath)

    if ($FullPath.Equals($resolvedRoot, $comparison)) {
        return "."
    }

    return $FullPath.Substring($resolvedRoot.Length + 1).Replace("\", "/")
}

function Get-AccessibleChildren {
    param([string]$Directory)

    try {
        return @(
            Get-ChildItem -LiteralPath $Directory -Force -ErrorAction Stop |
                Where-Object {
                    -not $_.FullName.Equals($resolvedOutput, $comparison)
                } |
                Sort-Object `
                    @{ Expression = { -not $_.PSIsContainer } },
                    @{ Expression = { $_.Name } }
        )
    }
    catch {
        $relative = Get-RelativeInventoryPath -FullPath $Directory
        $inventoryErrors.Add(
            "$relative - $($_.Exception.GetType().Name): $($_.Exception.Message)"
        )
        return @()
    }
}

$directoryCount = 1
$fileCount = 0
$totalBytes = [int64]0
$topLevelRows = [System.Collections.Generic.List[object]]::new()

foreach ($item in (Get-AccessibleChildren -Directory $resolvedRoot)) {
    if ($item.PSIsContainer) {
        try {
            $nestedDirectories = @(
                Get-ChildItem -LiteralPath $item.FullName -Force -Directory `
                    -Recurse -ErrorAction Stop
            )
            $nestedFiles = @(
                Get-ChildItem -LiteralPath $item.FullName -Force -File `
                    -Recurse -ErrorAction Stop |
                    Where-Object {
                        -not $_.FullName.Equals($resolvedOutput, $comparison)
                    }
            )
            $directoryCount += 1 + $nestedDirectories.Count
            $fileCount += $nestedFiles.Count
            $bytes = [int64](
                $nestedFiles |
                    Measure-Object -Property Length -Sum
            ).Sum
            $totalBytes += $bytes
            $topLevelRows.Add([pscustomobject]@{
                Name = $item.Name + "/"
                Directories = 1 + $nestedDirectories.Count
                Files = $nestedFiles.Count
                Bytes = $bytes
            })
        }
        catch {
            $inventoryErrors.Add(
                "$($item.Name)/ summary - $($_.Exception.Message)"
            )
        }
    }
    else {
        $fileCount++
        $totalBytes += [int64]$item.Length
        $topLevelRows.Add([pscustomobject]@{
            Name = $item.Name
            Directories = 0
            Files = 1
            Bytes = [int64]$item.Length
        })
    }
}

# Account for the generated document itself. Its final byte size is reported
# as "generated output" inside the tree because including its own exact size
# would make the document recursively unstable.
$fileCount++

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$writer = [System.IO.StreamWriter]::new(
    $resolvedOutput,
    $false,
    $utf8NoBom,
    65536
)

function Write-InventoryTree {
    param(
        [string]$Directory,
        [int]$Depth
    )

    foreach ($item in (Get-AccessibleChildren -Directory $Directory)) {
        $indent = "  " * $Depth
        if ($item.PSIsContainer) {
            $writer.WriteLine(
                "$indent[D] $($item.Name)/"
            )
            Write-InventoryTree `
                -Directory $item.FullName `
                -Depth ($Depth + 1)
        }
        else {
            $writer.WriteLine(
                "$indent[F] $($item.Name) [$($item.Length) bytes]"
            )
        }
    }
}

try {
    $generatedAt = Get-Date -Format "yyyy-MM-dd HH:mm:ss zzz"
    $writer.WriteLine("# Complete Project Folder Structure")
    $writer.WriteLine("")
    $writer.WriteLine("**Project root:** ``$resolvedRoot``  ")
    $writer.WriteLine("**Generated:** $generatedAt  ")
    $writer.WriteLine("**Directories:** $directoryCount  ")
    $writer.WriteLine("**Files:** $fileCount  ")
    $writer.WriteLine(
        "**Known file bytes before this generated document:** " +
        "$totalBytes"
    )
    $writer.WriteLine("")
    $writer.WriteLine(
        "This is a machine-generated inventory of every accessible folder " +
        "and file under the Unity project root. It includes authored assets, " +
        "source code, documentation, tests, Unity metadata, imported package " +
        "cache files, build caches, logs, test results, IDE-generated files, " +
        "and user settings."
    )
    $writer.WriteLine("")
    $writer.WriteLine(
        "Unity-generated areas such as ``Library/``, ``Temp/``, ``Logs/``, " +
        "and ``obj/`` are intentionally included because this document is an " +
        "exact filesystem inventory, not only a source-control manifest."
    )
    $writer.WriteLine("")
    $writer.WriteLine("Legend: ``[D]`` directory, ``[F]`` file.")
    $writer.WriteLine("")
    $writer.WriteLine("## Project area guide")
    $writer.WriteLine("")
    $writer.WriteLine(
        "- ``Assets/`` - Unity-authored runtime code, editor tooling, tests, " +
        "scenes, prefabs, materials, definitions, generated prototype assets, " +
        "and every matching Unity ``.meta`` identity file."
    )
    $writer.WriteLine(
        "- ``CraftConfigs/`` - external hovercraft configuration data."
    )
    $writer.WriteLine(
        "- ``Docs/`` - design documents, implementation reports, audits, " +
        "handoffs, schemas, and this inventory generator."
    )
    $writer.WriteLine(
        "- ``LegacyHovercraft/`` - preserved legacy hovercraft source and " +
        "supporting material kept outside the active Unity ``Assets`` tree."
    )
    $writer.WriteLine(
        "- ``Packages/`` - Unity package manifest and resolved package lock."
    )
    $writer.WriteLine(
        "- ``ProjectSettings/`` - serialized Unity project-wide settings."
    )
    $writer.WriteLine(
        "- ``TestDriveReports/`` - captured test-drive outputs and reports."
    )
    $writer.WriteLine(
        "- ``UserSettings/`` - local Unity editor/user preferences."
    )
    $writer.WriteLine(
        "- ``Library/`` - Unity import database, compiled assemblies, package " +
        "cache, shader cache, and other reproducible generated state."
    )
    $writer.WriteLine(
        "- ``Logs/`` - Unity editor, generator, compiler, test, parity, and " +
        "audit logs."
    )
    $writer.WriteLine(
        "- ``Temp/`` and ``obj/`` - temporary Unity/MSBuild intermediate output."
    )
    $writer.WriteLine(
        "- Root ``.sln``/``.csproj`` files - Unity-generated IDE project model."
    )
    $writer.WriteLine(
        "- Root test-result XML files - machine-readable V3 verification runs."
    )
    $writer.WriteLine("")
    $writer.WriteLine("## Top-level summary")
    $writer.WriteLine("")
    $writer.WriteLine("| Entry | Directories | Files | Bytes |")
    $writer.WriteLine("|---|---:|---:|---:|")
    foreach ($row in $topLevelRows) {
        $escapedName = $row.Name.Replace("|", "\|")
        $writer.WriteLine(
            "| ``$escapedName`` | $($row.Directories) | " +
            "$($row.Files) | $($row.Bytes) |"
        )
    }
    $writer.WriteLine(
        "| ``Docs/Complete_Project_Folder_Structure.md`` | 0 | 1 | " +
        "generated output |"
    )
    $writer.WriteLine("")
    $writer.WriteLine("## Complete tree")
    $writer.WriteLine("")
    $writer.WriteLine('```text')
    $writer.WriteLine("[D] ./")
    Write-InventoryTree -Directory $resolvedRoot -Depth 1
    $writer.WriteLine(
        "  [F] Docs/Complete_Project_Folder_Structure.md " +
        "[generated output]"
    )
    $writer.WriteLine('```')
    $writer.WriteLine("")
    $writer.WriteLine("## Inventory access errors")
    $writer.WriteLine("")
    if ($inventoryErrors.Count -eq 0) {
        $writer.WriteLine("None.")
    }
    else {
        foreach ($message in $inventoryErrors) {
            $writer.WriteLine("- $message")
        }
    }
}
finally {
    $writer.Dispose()
}

$outputInfo = Get-Item -LiteralPath $resolvedOutput
Write-Output (
    "Generated $($outputInfo.FullName) " +
    "($($outputInfo.Length) bytes; " +
    "$directoryCount directories; $fileCount files)."
)

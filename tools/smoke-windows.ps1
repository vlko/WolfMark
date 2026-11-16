#Requires -Version 7
<#
.SYNOPSIS
  Smoke test of the published Windows app (Native AOT): runs the real WolfMark.exe on isolated demo data.

.DESCRIPTION
  Checks what otherwise needs a manual check on Windows:
    deps        M0-02  every DLL the binaries import is in the output or part of Windows (no .NET, no VC++ redist)
    screenshots M10-04 --screenshots renders the README images and exits (they are kept in <WorkDir>\screenshots)
    instance    M0-05  a second launch on the same data hands over to the first and exits; other data = own instance

  Runs in CI after the AOT publish and locally:
    dotnet publish src/WolfMark.App -c Release -r win-x64 -p:Platform=x64 -o publish/win-x64
    pwsh tools/smoke-windows.ps1 -PublishDir publish/win-x64

  Never touches the real notes: every launch gets its own --demo folder under -WorkDir.
  In GitHub Actions failures become annotations and a step summary. Exit code 1 when a check failed.
#>
param(
    [Parameter(Mandatory)] [string] $PublishDir,
    [string] $WorkDir = (Join-Path ([IO.Path]::GetTempPath()) "wolfmark-smoke"),
    [ValidateSet('deps', 'screenshots', 'instance')] [string[]] $Checks = @('deps', 'screenshots', 'instance')
)

$ErrorActionPreference = 'Stop'
$PublishDir = (Resolve-Path $PublishDir).Path
# Absolute: the app runs with WorkDir as its current folder and would resolve a relative --demo path against it.
$WorkDir = [IO.Path]::GetFullPath($WorkDir, (Get-Location).Path)
$exe = Join-Path $PublishDir 'WolfMark.exe'
if (-not (Test-Path $exe)) { throw "WolfMark.exe not found in $PublishDir" }
if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
New-Item -ItemType Directory $WorkDir | Out-Null

$script:results = [System.Collections.Generic.List[object]]::new()
$inCi = [bool]$env:GITHUB_ACTIONS

function Report([string] $check, [bool] $ok, [string] $detail) {
    $script:results.Add([pscustomobject]@{ Check = $check; Ok = $ok; Detail = $detail })
    if ($ok) {
        Write-Host "PASS $check – $detail" -ForegroundColor Green
    }
    else {
        Write-Host "FAIL $check – $detail" -ForegroundColor Red
        if ($inCi) {
            $msg = $detail.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A')
            $title = "Smoke test – $check".Replace('%', '%25').Replace(':', '%3A').Replace(',', '%2C')
            Write-Host "::error title=${title}::${msg}"
        }
    }
}

function Get-Log([string] $demoDir) {
    $logs = Join-Path $demoDir 'local\logs'
    if (-not (Test-Path $logs)) { return '' }
    (Get-ChildItem $logs -Filter 'wolfmark-*.log' | Get-Content -Raw) -join "`n"
}

function Get-Problems([string] $log) {
    # Critical = unhandled exceptions (App.LogFatal); errors are worth seeing too.
    ($log -split "`n" | Where-Object { $_ -match '\[(CRT|ERR)\]' }) -join "`n"
}

function Start-WolfMark([string[]] $arguments) {
    # Start-Process joins the arguments with spaces as they are: quote them (paths with spaces).
    $quoted = $arguments | ForEach-Object { '"' + $_ + '"' }
    Start-Process -FilePath $exe -ArgumentList $quoted -PassThru -WorkingDirectory $WorkDir
}

function Wait-Window([System.Diagnostics.Process] $process, [int] $seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) { return $false }
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { return $true }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

function Stop-WolfMark([System.Diagnostics.Process[]] $processes) {
    foreach ($p in $processes) {
        if ($p -and -not $p.HasExited) {
            $p.Kill($true)
            $p.WaitForExit(10000) | Out-Null
        }
    }
}

# ---------------------------------------------------------------------------------------------------------------
# deps (M0-02): a clean Windows has no .NET and no VC++ redistributable. The runner has both, so instead of
# hoping the launch fails, the imports of every binary are checked against the output and Windows itself.
# ---------------------------------------------------------------------------------------------------------------
function Test-Dependencies {
    $forbidden = 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'clrjit.dll'
    $present = Get-ChildItem $PublishDir -Recurse -File | Where-Object { $forbidden -contains $_.Name.ToLowerInvariant() }
    Report 'deps: no .NET runtime' ($present.Count -eq 0) $(if ($present) { "runtime files in output: $($present.Name -join ', ')" } else { 'self-contained Native AOT, no runtime files' })

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $dumpbin = if (Test-Path $vswhere) {
        # The same query the Native AOT build uses to find the linker (Microsoft.NETCore.Native.Windows.targets):
        # without -prerelease/-requires a newer instance without C++ (or an Insiders one with it) is missed.
        & $vswhere -latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
            -find 'VC\Tools\MSVC\**\bin\Hostx64\x64\dumpbin.exe' | Select-Object -First 1
    }
    if (-not $dumpbin) {
        Report 'deps: imports' $true 'skipped – dumpbin (Visual Studio C++ tools) not found'
        return
    }

    $binaries = Get-ChildItem $PublishDir -Recurse -File -Include '*.exe', '*.dll'
    $shipped = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $binaries | ForEach-Object { [void]$shipped.Add($_.Name) }
    $system32 = Join-Path $env:windir 'System32'
    # Present on the runner and on developer machines, but not part of a clean Windows.
    $redistributable = '^(vcruntime\d+.*|msvcp\d+.*|concrt\d+|vccorlib\d+|vcomp\d+|mfc\d+.*|ucrtbased|msvcr\d+.*)\.dll$'

    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($binary in $binaries) {
        $output = & $dumpbin /nologo /dependents $binary.FullName 2>$null
        if ($LASTEXITCODE -ne 0) { continue } # not a PE image (e.g. a resource-only .dll is still PE; others skip)
        foreach ($line in $output) {
            $name = $line.Trim()
            if ($name -notmatch '^[\w.\-]+\.dll$') { continue }
            if ($shipped.Contains($name)) { continue }
            if ($name -match '^(api|ext)-ms-') { continue }
            if ($name -notmatch $redistributable -and (Test-Path (Join-Path $system32 $name))) { continue }
            $missing.Add("$($binary.Name) → $name")
        }
    }

    $missing = $missing | Sort-Object -Unique
    Report 'deps: imports' ($missing.Count -eq 0) $(if ($missing) { "not in the output and not part of Windows:`n$($missing -join "`n")" } else { "$($binaries.Count) binaries, every import resolved" })
}

# ---------------------------------------------------------------------------------------------------------------
# screenshots (M10-04): also proves the AOT binary starts, shows and renders the main window on demo data.
# ---------------------------------------------------------------------------------------------------------------
function Test-Screenshots {
    $demo = Join-Path $WorkDir 'demo-screenshots'
    $shots = Join-Path $WorkDir 'screenshots'
    $p = Start-WolfMark @('--demo', $demo, '--screenshots', $shots)
    if (-not $p.WaitForExit(240000)) {
        Stop-WolfMark $p
        Report 'screenshots' $false "did not finish within 4 minutes`n$(Get-Problems (Get-Log $demo))"
        return
    }

    $expected = 'main-light.png', 'raw-mode.png', 'calendar-month.png', 'search.png', 'full-view.png'
    $files = @(Get-ChildItem $shots -Filter '*.png' -ErrorAction SilentlyContinue)
    $absent = $expected | Where-Object { $files.Name -notcontains $_ }
    # A blank (single-colour) 1440×960 PNG compresses to a few kB; a real window is far bigger.
    $blank = $files | Where-Object { $_.Length -lt 20KB } | ForEach-Object { "$($_.Name) ($([int]($_.Length / 1KB)) kB)" }
    $problems = Get-Problems (Get-Log $demo)

    $ok = (-not $absent) -and (-not $blank) -and (-not $problems)
    $detail = if ($ok) { "$($files.Count) images: $($files.Name -join ', ')" } else {
        @(
            $(if ($absent) { "missing: $($absent -join ', ')" })
            $(if ($blank) { "suspiciously small: $($blank -join ', ')" })
            $(if ($problems) { "log:`n$problems" })
        ) -join "`n"
    }
    Report 'screenshots' $ok $detail
}

# ---------------------------------------------------------------------------------------------------------------
# instance (M0-05): one process per data folder; a later launch redirects its activation and exits.
# ---------------------------------------------------------------------------------------------------------------
function Test-SingleInstance {
    $demo = Join-Path $WorkDir 'demo-instance'
    $other = Join-Path $WorkDir 'demo-instance-other'
    $first = $null; $second = $null; $third = $null
    try {
        $first = Start-WolfMark @('--demo', $demo)
        if (-not (Wait-Window $first 90)) {
            Report 'instance: first launch shows a window' $false "no main window within 90 s (exited: $($first.HasExited))`n$(Get-Problems (Get-Log $demo))"
            return
        }
        Report 'instance: first launch shows a window' $true "pid $($first.Id)"

        $second = Start-WolfMark @('--demo', $demo)
        $exited = $second.WaitForExit(30000)
        Start-Sleep -Seconds 1 # the redirected activation is logged by the first process
        $redirected = (Get-Log $demo) -match 'Activation redirected from another launch'
        $ok = $exited -and $second.ExitCode -eq 0 -and -not $first.HasExited -and $redirected
        Report 'instance: second launch hands over and exits' $ok `
            "second exited: $exited$(if ($exited) { " (code $($second.ExitCode))" }), first still running: $(-not $first.HasExited), redirect logged by the first: $redirected"

        # Other data folder (e.g. demo next to the real notes) = its own instance.
        $third = Start-WolfMark @('--demo', $other)
        $ok = Wait-Window $third 90
        Report 'instance: other data folder runs separately' $ok $(if ($ok) { "pid $($third.Id) next to pid $($first.Id)" } else { "no own window (exited: $($third.HasExited))" })

        $problems = (Get-Problems (Get-Log $demo)), (Get-Problems (Get-Log $other)) | Where-Object { $_ }
        Report 'instance: no errors logged' (-not $problems) $(if ($problems) { $problems -join "`n" } else { 'clean logs' })
    }
    finally {
        Stop-WolfMark @($first, $second, $third)
    }
}

if ($Checks -contains 'deps') { Test-Dependencies }
if ($Checks -contains 'screenshots') { Test-Screenshots }
if ($Checks -contains 'instance') { Test-SingleInstance }

$failed = @($script:results | Where-Object { -not $_.Ok })
if ($env:GITHUB_STEP_SUMMARY) {
    $lines = @('### Windows smoke test', '', '| | Check | Detail |', '|---|---|---|')
    foreach ($r in $script:results) {
        $lines += "| $(if ($r.Ok) { '✅' } else { '❌' }) | $($r.Check) | $($r.Detail.Replace("`n", '<br>').Replace('|', '\|')) |"
    }
    $lines -join "`n" | Out-File $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8
}

Write-Host ''
Write-Host "$($script:results.Count - $failed.Count)/$($script:results.Count) checks passed; work folder: $WorkDir"
exit $(if ($failed.Count) { 1 } else { 0 })

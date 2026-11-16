<#
.SYNOPSIS
  Frosty Neon VR Studio: run the avatar toolkit from one command.

.DESCRIPTION
  Commands (run from the repository folder):
    audit    <file.blend>      Check a Blender avatar file and write <name>_audit_report.json beside it. The .blend is not changed.
    export   <file.blend>      Export it to FBX and copy it into the Unity project. Never overwrites without -Force.
    import   <file.unitypackage>  Bring an avatar package (most Booth avatars) into the Unity project, then check
                               the avatar(s) it contains. Lists the contents first; a package with scripts is
                               only imported with -AllowScripts. -DryRun lists without importing.
    vroid    <file.vrm>        Bring in a VRoid Studio file (needs UniVRM in the project) and set it up as a VRChat
                               avatar (<name>_Avatar.prefab): eyes placed, lip sync wired. Shows the creator's terms
                               stored in the file; if they limit who may use or change it, add -AcceptTerms only
                               when you are the author or have permission. VRM 1.0 files are not read (export 0.0).
    model    <file.fbx>        Bring in a plain model file (Mixamo, Blender, a 3D store): copies it into the Unity
                               project (with a <name>.fbm or Textures folder beside it), then runs prepare on the
                               copy. Your file is not changed. -Height 1.6 scales it; -Force replaces a different
                               model of the same name.
    fix      <Assets/...prefab>   Guided Fix for an avatar prefab (for example a Booth avatar): turns on the import
                               settings VRChat's upload requires, without editing the avatar's files. -DryRun lists
                               the changes; -Undo puts the settings back.
    quest    <Assets/...prefab>   Make a Quest version of an avatar prefab whose materials Quest refuses: a copy with
                               Quest materials and smaller Quest textures, sharing the avatar ID. -Remove deletes it.
    list                       List the avatars in the Unity project. Changes nothing.
    inspect  <Assets/...prefab>   Everything the Avatar Guide shows for one avatar (check in plain words, Fix plan,
                               Quest version, height, terms, preview pictures). Changes nothing.
    check    <Assets/...fbx>   Dry run of the Unity pipeline on an imported model. Changes nothing.
    prepare  <Assets/...fbx>   Run the Unity pipeline: import settings, materials, avatar prefab, previews.
                               Add -Height 1.6 to scale the avatar to that many metres tall.
    preview  <Assets/...>      Render preview images of a model or prefab.
    upload   <Assets/...>      Upload a prepared avatar to your VRChat account as a PRIVATE avatar: give its model
                               (or its _Avatar prefab). Add -Quest for the Quest version, -Name to name a new avatar,
                               -DryRun to check without uploading. It switches PC/Quest, checks the avatar, opens
                               Unity and waits; you only confirm the upload in the box Unity shows.
    all      <file.blend>      audit, export, then prepare.
    doctor                     Check this PC has what the toolkit needs (Creator Companion, the right Unity,
                               Quest support, the VRChat SDK, Blender, disk space) and say what to install.
                               Installs and changes nothing.
    selftest                   Run every Blender and Unity self-test, and the Frosty Neon Studio self-test.

  Unity commands need the Unity Editor window closed. upload then opens Unity itself and uses the VRChat
  sign-in saved in the VRChat SDK window; see docs/AVATAR_UPLOAD.md.

  Blender and Unity are found automatically. To use other installs, set FROSTY_BLENDER or FROSTY_UNITY
  to the full path of blender.exe or Unity.exe.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\frosty.ps1 all C:\Art\MyAvatar.blend
#>
param(
    [Parameter(Position = 0)] [string] $Command = "help",
    [Parameter(Position = 1)] [string] $Target,
    [switch] $Force,
    [double] $Height = 0,
    [switch] $Quest,
    [string] $Name,
    [switch] $DryRun,
    [switch] $AllowScripts,
    [switch] $Undo,
    [switch] $Remove,
    [switch] $AcceptTerms
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Project = Join-Path $Root "Unity\FrostyAvatarProject"
$Logs = Join-Path $Project "Logs\FrostyNeon"
$ImportFolder = "Assets/FrostyNeon/Avatars"
$ModelFolder = "Assets/FrostyNeon/Models"    # model files people bring in with 'model' (theirs, git-ignored)

function Say([string] $text, [string] $colour = "Gray") { Write-Host $text -ForegroundColor $colour }
function Fail([string] $text) { Write-Host "ERROR: $text" -ForegroundColor Red; exit 1 }

function Find-Blender {
    if ($env:FROSTY_BLENDER) { if (Test-Path -LiteralPath $env:FROSTY_BLENDER -PathType Leaf) { return $env:FROSTY_BLENDER } else { Fail "FROSTY_BLENDER points to a missing file: $env:FROSTY_BLENDER" } }
    $found = Get-ChildItem "C:\Program Files\Blender Foundation" -Filter blender.exe -Recurse -Depth 1 -ErrorAction SilentlyContinue |
             Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $found) { Fail "Blender was not found. Install Blender, or set FROSTY_BLENDER to the full path of blender.exe." }
    return $found.FullName
}

function Get-UnityVersion {
    return (Select-String -LiteralPath (Join-Path $Project "ProjectSettings\ProjectVersion.txt") -Pattern "^m_EditorVersion: (.+)$").Matches[0].Groups[1].Value.Trim()
}

# Where Unity Hub keeps editors: its default folder, and the folder chosen in Hub > Preferences, if any.
function Get-UnityFolders {
    $folders = @("C:\Program Files\Unity\Hub\Editor")
    $setting = Join-Path $env:APPDATA "UnityHub\secondaryInstallPath.json"
    if (Test-Path -LiteralPath $setting) {
        try { $custom = Get-Content -LiteralPath $setting -Raw | ConvertFrom-Json; if ($custom -is [string] -and $custom.Trim()) { $folders += $custom.Trim() } } catch { }
    }
    return $folders
}

# The Unity.exe this project needs (FROSTY_UNITY, or the right version in a Unity Hub folder), or $null.
function Get-UnityPath {
    if ($env:FROSTY_UNITY) { return $env:FROSTY_UNITY }
    $version = Get-UnityVersion
    foreach ($folder in Get-UnityFolders) {
        $path = Join-Path $folder "$version\Editor\Unity.exe"
        if (Test-Path -LiteralPath $path -PathType Leaf) { return $path }
    }
    return $null
}

function Find-Unity {
    $path = Get-UnityPath
    if ($env:FROSTY_UNITY -and -not (Test-Path -LiteralPath $path -PathType Leaf)) { Fail "FROSTY_UNITY points to a missing file: $env:FROSTY_UNITY" }
    if (-not $path) {
        Fail ("Unity " + (Get-UnityVersion) + " (the version this project uses) was not found. Install it through VRChat Creator Companion or " +
              "Unity Hub, or, if it is installed somewhere else, set FROSTY_UNITY to the full path of its Unity.exe. '.\frosty.ps1 doctor' says more.")
    }
    return $path
}

function Test-UnityOpen {
    $lock = Join-Path $Project "Temp\UnityLockfile"
    if (-not (Test-Path -LiteralPath $lock)) { return $false }
    try { [IO.File]::Open($lock, "Open", "ReadWrite", "None").Close(); return $false } catch { return $true }
}

function Assert-UnityClosed {
    if (Test-UnityOpen) { Fail "The Unity Editor has this project open. Close the Unity window, then run this again." }
}

# Blender's output goes to a log in $Logs. --python-exit-code 1: a Python error in a tool fails the run (Blender's own
# default is exit code 0). Blender writes warnings to stderr; with "Stop", Windows PowerShell would turn the first one
# into an error that ends this script, so native output is read with "Continue".
function Invoke-Blender([string[]] $arguments, [string] $logName) {
    $blender = Find-Blender
    New-Item -ItemType Directory -Force -Path $Logs | Out-Null
    $ErrorActionPreference = "Continue"
    & $blender --background --python-exit-code 1 @arguments 2>&1 | ForEach-Object { "$_" } |
        Set-Content -LiteralPath (Join-Path $Logs $logName) -Encoding UTF8
    return $LASTEXITCODE
}

# One command-line argument for Start-Process, which joins its list with spaces: quote anything with a space (file
# names like "My Avatar.fbx"), and double the backslashes before the closing quote, which would otherwise escape it.
function ConvertTo-Argument([string] $value) {
    if ($value.Contains('"')) { Fail "This path or name contains a double quote (`"), which cannot be passed to Unity safely: $value" }
    if ($value -ne "" -and $value -notmatch '\s') { return $value }
    return '"' + ($value -replace '(\\+)$', '$1$1') + '"'
}

function Invoke-Unity([string] $method, [string[]] $extra, [string] $logName) {
    Assert-UnityClosed
    $unity = Find-Unity
    New-Item -ItemType Directory -Force -Path $Logs | Out-Null
    $log = Join-Path $Logs $logName
    if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log }
    Say "Running Unity ($method)... this takes about half a minute."
    $all = @("-batchmode", "-projectPath", $Project, "-executeMethod", $method) + $extra + @("-logFile", $log)
    $process = Start-Process -FilePath $unity -ArgumentList ($all | ForEach-Object { ConvertTo-Argument $_ }) -Wait -PassThru -NoNewWindow
    if (-not (Test-Path -LiteralPath $log)) { Fail "Unity did not start (exit code $($process.ExitCode)); it wrote no log. Run '.\frosty.ps1 doctor' to check the Unity install." }
    if (Select-String -LiteralPath $log -Pattern "error CS" -Quiet) { Fail "Unity reported script compile errors. See $log" }
    return $process.ExitCode
}

# A file the person gave. -LiteralPath, so names with [ ] (common in shop downloads) are not read as wildcards.
function Resolve-Input([string] $what) {
    Require-Target $what
    if (-not (Test-Path -LiteralPath $Target -PathType Leaf)) { Fail "File not found: $Target" }
    return (Resolve-Path -LiteralPath $Target).Path
}

function Show-Pipeline([string] $assetPath) {
    $report = Join-Path $Logs ("pipeline_" + [IO.Path]::GetFileNameWithoutExtension($assetPath) + ".json")
    if (-not (Test-Path -LiteralPath $report)) { Fail "No pipeline report was written. See the Unity log: $Logs\pipeline.log" }
    $data = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    Say ""
    $data.summary -split "`n" | ForEach-Object { Say "  $_" }
    $colour = switch ($data.status) { "pass" { "Green" } "warn" { "Yellow" } default { "Red" } }
    Say ""
    Say ("Status: " + $data.status) $colour
    Say ("Next: " + $data.next)
    Say ("Full report: " + $report)
}

function Require-Target([string] $what) { if (-not $Target) { Fail "Give $what. Run '.\frosty.ps1 help' for usage." } }

function Assert-Blend([string] $file) {
    if ([IO.Path]::GetExtension($file) -ne ".blend") { Fail "This command takes a Blender file (.blend). For an .fbx use 'model', for a .unitypackage 'import', for a .vrm 'vroid'." }
}

function Do-Audit {
    $source = Resolve-Input "a .blend file"
    Assert-Blend $source
    # The report goes beside the .blend (the toolkit's own robot keeps its report in Git that way); an old one is
    # removed first, so a failed run cannot show the last run's result.
    $out = [IO.Path]::ChangeExtension($source, $null).TrimEnd(".") + "_audit_report.json"
    if (Test-Path -LiteralPath $out) { Remove-Item -LiteralPath $out }
    $code = Invoke-Blender @($source, "--python", (Join-Path $Root "tools\blender\audit_avatar_scene.py"), "--", "--json-out", $out) "blender_audit.log"
    if (-not (Test-Path -LiteralPath $out)) { Fail "The audit did not finish (Blender exit code $code). See $Logs\blender_audit.log" }
    $data = Get-Content -LiteralPath $out -Raw | ConvertFrom-Json
    $humanoid = $data.humanoid_bones | Select-Object -First 1
    Say ("Audit: " + $data.status + " | triangles " + $data.performance.measured.triangles +
         " | PC " + $data.performance.platforms.pc.estimated_rank + ", Quest " + $data.performance.platforms.quest.estimated_rank +
         $(if ($humanoid) { " | humanoid slots matched " + ($humanoid.matched.PSObject.Properties | Measure-Object).Count } else { "" }))
    foreach ($issue in $data.issues) { Say ("  " + $issue.severity + " " + $issue.code + ": " + $issue.message) $(if ($issue.severity -eq "error") { "Red" } elseif ($issue.severity -eq "warning") { "Yellow" } else { "Gray" }) }
    Say "Report: $out"
    if ($code -ne 0) { Fail "The audit found errors (exit code $code)." }
}

function Do-Export {
    $source = Resolve-Input "a .blend file"
    Assert-Blend $source
    $name = [IO.Path]::GetFileNameWithoutExtension($source)
    $exportDir = Join-Path $Root "Assets\Avatars\Exports\PC"
    $fbx = Join-Path $exportDir "$name.fbx"
    $report = Join-Path $exportDir "${name}_export_report.json"
    $unityFbx = Join-Path $Project ($ImportFolder.Replace("/", "\") + "\$name.fbx")
    if (((Test-Path -LiteralPath $fbx) -or (Test-Path -LiteralPath $unityFbx)) -and -not $Force) {
        Fail "$name.fbx already exists (in the exports or the Unity project). Add -Force to replace it; Unity keeps its import settings."
    }
    New-Item -ItemType Directory -Force -Path $exportDir | Out-Null
    if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
    $blenderArgs = @($source, "--python", (Join-Path $Root "tools\blender\export_avatar_fbx.py"), "--", "--out", $fbx, "--roundtrip", "--json-out", $report)
    if ($Force) { $blenderArgs += "--overwrite" }
    $code = Invoke-Blender $blenderArgs "blender_export.log"
    if ($code -ne 0) {
        $details = if (Test-Path -LiteralPath $report) { $report } else { "$Logs\blender_export.log" }
        Fail "The export failed or its checks did not pass, so nothing was copied into Unity. See $details"
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $unityFbx) | Out-Null
    Copy-Item -LiteralPath $fbx -Destination $unityFbx -Force
    Say "Exported $fbx and copied it into the Unity project as $ImportFolder/$name.fbx" "Green"
    return "$ImportFolder/$name.fbx"
}

function Do-Import {
    $file = Resolve-Input "the avatar package (.unitypackage)"
    if ([IO.Path]::GetExtension($file) -ne ".unitypackage") { Fail "import takes a Unity package (.unitypackage). If it came in a .zip, unzip it first; for an .fbx use 'model', for a .vrm 'vroid'." }
    $reportFile = Join-Path $Logs "package_import.json"
    if (Test-Path -LiteralPath $reportFile) { Remove-Item -LiteralPath $reportFile }
    $extra = @("-packageFile", $file)
    if ($DryRun) { $extra += "-packageInspectOnly" }
    if ($AllowScripts) { $extra += "-packageAllowScripts" }
    $code = Invoke-Unity "FrostyNeon.Editor.AvatarPackage.Run" $extra "package_import.log"
    if (-not (Test-Path -LiteralPath $reportFile)) { Fail "No import report was written. See $Logs\package_import.log" }
    $report = Get-Content -LiteralPath $reportFile -Raw | ConvertFrom-Json
    $info = $report.inspected
    if ($info) {
        Say ("Package: " + $info.file_count + " files, " + @($info.prefabs).Count + " prefab(s), " + @($info.models).Count + " model(s), " + @($info.shaders).Count + " shader file(s).")
        if (@($info.scripts).Count -gt 0) {
            Say ("It contains " + @($info.scripts).Count + " script file(s); Unity runs these when they are imported:") "Yellow"
            @($info.scripts) | Select-Object -First 12 | ForEach-Object { Say "  $_" "Yellow" }
        }
    }
    Say ""
    Say $report.message $(if ($code -eq 0) { "Green" } else { "Red" })
    foreach ($avatar in @($report.avatars)) {
        if (-not $avatar) { continue }
        Say ("  " + $avatar.prefab + "   PC: " + $avatar.pc_status + "   Quest: " + $avatar.quest_status)
        foreach ($m in @($avatar.missing_shaders)) { if ($m) { Say "    missing shader: $m" "Yellow" } }
    }
    if ($report.mode -eq "imported" -and @($report.avatar_prefabs).Count -gt 0) {
        Say ""
        Say "Next: open Unity and choose Frosty Neon > Avatar Guide, or upload with: frosty.ps1 upload <the prefab above>" "Green"
    }
    if ($code -ne 0) { exit 1 }
}

function Do-Model {
    # A plain model file (.fbx, for example from Mixamo, Blender or a 3D store): copy it into the project, with its
    # texture folder, and run the pipeline on the copy. The person's own file is not changed.
    $file = Resolve-Input "the model file (.fbx)"
    if ([IO.Path]::GetExtension($file) -ne ".fbx") { Fail "model takes an .fbx file. For a .unitypackage use 'import', for a .vrm use 'vroid', for a .blend use 'all'." }
    $name = [IO.Path]::GetFileNameWithoutExtension($file)
    $folder = "$ModelFolder/$name"
    $dest = Join-Path $Project ($folder.Replace("/", "\"))
    $copy = Join-Path $dest "$name.fbx"
    $same = (Test-Path -LiteralPath $copy) -and (Get-FileHash -LiteralPath $copy).Hash -eq (Get-FileHash -LiteralPath $file).Hash
    if ((Test-Path -LiteralPath $copy) -and -not $same -and -not $Force) {
        Fail "A different $name.fbx is already in the project ($folder). Rename your file, or add -Force to replace it (Unity keeps its import settings)."
    }
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    if (-not $same) { Copy-Item -LiteralPath $file -Destination $copy -Force; Say "Copied $name.fbx into the project: $folder" }
    else { Say "$name.fbx is already in the project ($folder); using it." }
    # Textures that live in separate image files: the FBX's own <name>.fbm folder, or a Textures folder beside it.
    $source = Split-Path $file
    foreach ($sub in "$name.fbm", "Textures", "textures", "Texture", "texture", "tex") {
        $dir = Join-Path $source $sub
        if ((Test-Path -LiteralPath $dir -PathType Container) -and -not (Test-Path -LiteralPath (Join-Path $dest $sub))) {
            $folderPath = (Get-Item -LiteralPath $dir).FullName.TrimEnd('\')
            $images = @(Get-ChildItem -LiteralPath $folderPath -File -Recurse | Where-Object { $_.Extension -match '^\.(png|jpe?g|tga|psd|bmp|tiff?|exr)$' })
            foreach ($image in $images) {
                $to = Join-Path (Join-Path $dest $sub) $image.FullName.Substring($folderPath.Length).TrimStart('\')
                New-Item -ItemType Directory -Force -Path (Split-Path $to) | Out-Null
                Copy-Item -LiteralPath $image.FullName -Destination $to
            }
            if ($images.Count) { Say "Copied $($images.Count) texture image(s) from $sub" }
            break
        }
    }
    Do-Pipeline "$folder/$name.fbx" $true
}

function Show-Result([string] $file, [int] $code) {
    if (-not (Test-Path -LiteralPath $file)) { Fail "No result was written. See the Unity log in $Logs." }
    $r = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    foreach ($name in "vrm_terms", "vrm_warnings", "planned", "converted", "see_through", "textures_capped", "failed") {
        $items = @($r.$name) | Where-Object { $_ }
        if ($items.Count) { Say "  ${name}:"; $items | Select-Object -First 15 | ForEach-Object { Say "    $_" } }
    }
    foreach ($a in @($r.avatars) | Where-Object { $_ }) {
        Say ("  " + "$($a.name)".PadRight(32) + " " + "$($a.kind)".PadRight(22) + " " + $a.prefab + $(if ($a.quest_version) { "   (has a Quest version)" } else { "" }))
    }
    foreach ($g in @($r.groups) | Where-Object { $_ }) {
        Say ("  " + $g.group + ":")
        foreach ($i in @($g.items)) { Say ("    " + $i.title + $(if ($i.platform) { " [" + $i.platform + " only]" } else { "" }) + " - " + $i.meaning) }
    }
    if ($null -ne $r.height_m) { Say ("  Height " + $r.height_m + " m, eyes at " + $r.eyes_m + " m") }
    foreach ($n in @($r.floor_notes) | Where-Object { $_ }) { Say "  note: $n" "Yellow" }
    if ($r.quest_version) { Say ("  Quest version: " + $r.quest_version) }
    foreach ($n in @($r.quest_version_notes) | Where-Object { $_ }) { Say "    Quest: $n" "Yellow" }
    $pictures = @($r.pictures) | Where-Object { $_ }
    if ($pictures) { Say ("  Pictures: " + (Split-Path @($pictures)[0])) }
    if ($r.pc_status) { Say ("  PC: " + $r.pc_status + "   Quest: " + $r.quest_status) }
    foreach ($e in @($r.errors) | Where-Object { $_ }) { Say "    error: $e" "Yellow" }
    foreach ($e in @($r.quest_findings) | Where-Object { $_ }) { Say "    Quest: $e" "Yellow" }
    if ($r.avatar_id_note) { Say ("  " + $r.avatar_id_note) "Yellow" }
    Say ""
    Say $r.message $(if ($code -eq 0) { "Green" } else { "Red" })
    if ($code -ne 0) { exit 1 }
}

function Do-Command([string] $method, [string[]] $extra, [string] $name) {
    $result = Join-Path $Logs "${name}_result.json"
    if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
    $code = Invoke-Unity "FrostyNeon.Editor.FrostyNeonCommands.$method" $extra "$name.log"
    Show-Result $result $code
}

function Do-Upload {
    Require-Target "the avatar to upload: its model (Assets/...fbx) or its avatar prefab (Assets/..._Avatar.prefab)"
    $asset = $Target.Replace('\', '/')
    if (-not $asset.StartsWith("Assets/") -or -not [IO.Path]::GetExtension($asset)) { Fail "Give the model or prefab's path inside the Unity project, starting with Assets/ (for example Assets/FrostyNeon/Models/Me/Me.fbx)." }
    $prefab = if ($asset -like "*.prefab") { $asset } else { $asset.Substring(0, $asset.LastIndexOf('.')) + "_Avatar.prefab" }
    if (-not (Test-Path -LiteralPath (Join-Path $Project $prefab))) { Fail "No avatar prefab at $prefab. Run 'frosty.ps1 prepare <model>' first." }
    $platform = if ($Quest) { "quest" } else { "pc" }
    $label = if ($Quest) { "Quest" } else { "PC" }
    $wanted = if ($Quest) { "Android" } else { "StandaloneWindows64" }
    $avatarName = if ($Name) { $Name } else { [IO.Path]::GetFileNameWithoutExtension($prefab) -replace '_Avatar$', '' }
    # A Quest upload uses the avatar's Quest version (made in the Avatar Guide) when there is one.
    $questVersion = ($prefab.Substring(0, $prefab.LastIndexOf('/')) + "/FrostyNeon_Quest/" + [IO.Path]::GetFileNameWithoutExtension($prefab) + "_Quest.prefab")
    if ($Quest -and (Test-Path -LiteralPath (Join-Path $Project $questVersion))) { $prefab = $questVersion; Say "Using the Quest version: $prefab" }

    # 1. With Unity closed: switch the build target and check the avatar (batch mode, uploads nothing).
    $readyFile = Join-Path $Logs "upload_readiness.json"
    if (Test-Path -LiteralPath $readyFile) { Remove-Item -LiteralPath $readyFile }
    Invoke-Unity "FrostyNeon.Editor.AvatarUpload.CheckReady" @("-buildTarget", $(if ($Quest) { "Android" } else { "Win64" }), "-uploadPrefab", $prefab) "upload_check.log" | Out-Null
    if (-not (Test-Path -LiteralPath $readyFile)) { Fail "The avatar check did not run. See $Logs\upload_check.log" }
    $ready = Get-Content -LiteralPath $readyFile -Raw | ConvertFrom-Json
    if ($ready.prefab -ne $prefab) { Fail "The avatar check looked at $($ready.prefab), not $prefab. See $Logs\upload_check.log" }
    if (-not $ready.prefab_ok) { Fail "$prefab is not an avatar prefab (no VRChat avatar descriptor). Run 'frosty.ps1 prepare <model>' first." }
    if ($ready.active_build_target -ne $wanted) {
        Fail "Unity could not switch to $wanted (it is on $($ready.active_build_target)). For Quest, Unity's Android Build Support must be installed (Unity Hub > Installs > Add modules)."
    }
    $status = if ($Quest) { $ready.quest_status } else { $ready.pc_status }
    $relevant = @($ready.preflight_findings | Where-Object { -not $_.platform -or $_.platform -eq $platform })
    foreach ($f in $relevant) { Say ("  " + $f.severity + ": " + $f.message) $(if ($f.severity -eq "error") { "Red" } else { "Yellow" }) }
    if ($status -eq "fail") { Fail "The $label check fails, so nothing was uploaded. Fix the errors above (usually: frosty.ps1 prepare <model>), then try again." }
    $kind = if ($ready.avatar_id_already_set) { "update the existing avatar" } else { "upload a NEW avatar" }
    Say "Check for ${label}: $status. This will $kind '$avatarName' ($label version) as a PRIVATE avatar." "Green"
    if ($DryRun) { Say "Dry run: Unity was not opened and nothing was uploaded." "Green"; return }

    # 2. Ask Unity to start the upload once it has loaded and VRChat has signed in (it still asks you to confirm).
    $requestFile = Join-Path $Logs "upload_request.json"
    $resultFile = Join-Path $Logs "upload_result.json"
    $request = [ordered]@{ prefab = $prefab; name = $avatarName; platform = $platform; requested_at = (Get-Date).ToUniversalTime().ToString("o") }
    [IO.File]::WriteAllText($requestFile, ($request | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
    $since = Get-Date
    $editor = Start-Process -FilePath (Find-Unity) -ArgumentList @("-projectPath", (ConvertTo-Argument $Project)) -PassThru
    Say ""
    Say "Unity is opening. When it has loaded and VRChat has signed you in, a 'Frosty Neon upload' box asks you to"
    Say "confirm: click Update (or Upload). If the VRChat SDK window asks you to sign in, do that first."
    Say "If Unity asks to save a scene, save it in Assets under any name."
    Say "Waiting for the result (up to 30 minutes)..."

    # 3. Wait for the uploader's result file (or for Unity to be closed without one).
    $deadline = (Get-Date).AddMinutes(30)
    $closedAt = $null
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 5
        if ((Test-Path -LiteralPath $resultFile) -and (Get-Item -LiteralPath $resultFile).LastWriteTime -gt $since) {
            Start-Sleep -Seconds 1
            try { $result = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json } catch { continue }    # still being written
            Say ""
            Say $result.message
            if ($result.uploaded) {
                Say "Done: the $label version is on your VRChat account. Unity stays open; close it when you are finished." "Green"
                return
            }
            Fail "Not uploaded. Details: $resultFile"
        }
        # Unity closed before it answered: give it a moment (the lock file shows an Editor that is still running).
        if ($editor.HasExited -and -not (Test-UnityOpen)) {
            if (-not $closedAt) { $closedAt = Get-Date }
            elseif (((Get-Date) - $closedAt).TotalSeconds -gt 15) {
                if (Test-Path -LiteralPath $requestFile) { Remove-Item -LiteralPath $requestFile }
                Fail "Unity was closed before the upload finished, so nothing was uploaded. Run the upload again when you are ready."
            }
        } else { $closedAt = $null }
    }
    if (Test-Path -LiteralPath $requestFile) { Remove-Item -LiteralPath $requestFile }
    Fail "No result after 30 minutes, so the request was cancelled. Nothing was uploaded unless Unity says otherwise; see $resultFile."
}

function Do-Doctor {
    # Read-only: looks at what is installed and says, in order, what to do. Installs and changes nothing.
    $script:needed = 0
    $script:advised = 0
    $script:rows = @()
    function Row([string] $state, [string] $what, [string] $detail, [string] $todo) {
        $script:rows += [ordered]@{ state = $state; what = $what; detail = $detail; todo = $todo }
        $colour = switch ($state) { "OK" { "Green" } "MISSING" { "Red" } "OPTIONAL" { "Yellow" } default { "Gray" } }
        Say ("  [" + $state.PadRight(8) + "] " + $what + $(if ($detail) { ": " + $detail } else { "" })) $colour
        if ($todo) { Say ("             -> " + $todo) $colour }
        if ($state -eq "MISSING") { $script:needed++ }
        if ($state -eq "OPTIONAL") { $script:advised++ }
    }
    Say "Frosty Neon doctor: checking this PC (nothing is installed or changed)."
    Say ""

    # 1. VRChat Creator Companion: installs Unity and the VRChat SDK, and adds shader packages.
    $vccPlaces = @((Join-Path $env:LOCALAPPDATA "Programs\VRChat Creator Companion\CreatorCompanion.exe"),
                   (Join-Path $env:ProgramFiles "VRChat Creator Companion\CreatorCompanion.exe"))
    $vcc = $vccPlaces | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($vcc) { Row "OK" "VRChat Creator Companion" $vcc }
    else { Row "MISSING" "VRChat Creator Companion" "" "Install it from https://vrchat.com/home/download (Creator Companion). It also installs Unity for you." }

    # 2. The Unity version this project uses.
    $version = Get-UnityVersion
    $unity = Get-UnityPath
    $unityOk = $unity -and (Test-Path -LiteralPath $unity -PathType Leaf)
    if ($unityOk) { Row "OK" "Unity $version" $unity }
    elseif ($env:FROSTY_UNITY) { Row "MISSING" "Unity $version" "FROSTY_UNITY points to a missing file: $unity" "Fix FROSTY_UNITY or remove it." }
    else {
        $others = @(Get-UnityFolders | ForEach-Object { Get-ChildItem -LiteralPath $_ -Directory -ErrorAction SilentlyContinue } | ForEach-Object { $_.Name } | Sort-Object -Unique)
        $hint = if ($others.Count) { "Found other versions (" + ($others -join ", ") + "), but VRChat needs exactly $version." } else { "" }
        Row "MISSING" "Unity $version" $hint "Open Creator Companion, add this project (Projects > Add > $Project) and let it install Unity $version."
    }

    # 3. Quest support (Unity's Android module, with its SDK, NDK and Java).
    if ($unityOk) {
        $android = Join-Path (Split-Path $unity) "Data\PlaybackEngines\AndroidPlayer"
        $parts = @("SDK", "NDK", "OpenJDK") | Where-Object { -not (Test-Path (Join-Path $android $_)) }
        if (-not (Test-Path $android)) { Row "OPTIONAL" "Quest uploads (Android Build Support)" "not installed" "Only needed for Quest. In Unity Hub (install it from unity.com if you do not have it): Installs > $version > Add modules > Android Build Support (with OpenJDK and SDK & NDK Tools)." }
        elseif ($parts.Count) { Row "OPTIONAL" "Quest uploads (Android Build Support)" ("installed without " + ($parts -join ", ")) "Add the missing parts: Unity Hub > Installs > $version > Add modules (OpenJDK, Android SDK & NDK Tools)." }
        else { Row "OK" "Quest uploads (Android Build Support)" "Android module with SDK, NDK and Java" }
    }

    # 4. The VRChat SDK in this project.
    $sdkFile = Join-Path $Project "Packages\com.vrchat.avatars\package.json"
    if (Test-Path $sdkFile) {
        $sdk = (Get-Content $sdkFile -Raw | ConvertFrom-Json).version
        if ($sdk -eq "3.10.6") { Row "OK" "VRChat Avatars SDK" $sdk }
        else { Row "INFO" "VRChat Avatars SDK" "$sdk (the toolkit was tested with 3.10.6; it should still work, and the check will say if anything differs)" "" }
    } else { Row "MISSING" "VRChat Avatars SDK in this project" "" "Open the project in Creator Companion (Manage Project) and add 'VRChat SDK - Avatars'." }

    # 5. Shader packages that bought avatars usually need.
    $packages = Join-Path $Project "Packages"
    $shaders = [ordered]@{ "lilToon" = @("jp.lilxyzw.liltoon", "Assets\lilToon"); "Poiyomi Toon" = @("com.poiyomi.toon", "Assets\_PoiyomiShaders") }
    foreach ($name in $shaders.Keys) {
        $found = (Test-Path (Join-Path $packages $shaders[$name][0])) -or (Test-Path (Join-Path $Project $shaders[$name][1]))
        if ($found) { Row "OK" "$name shader" "in the project" }
        else { Row "INFO" "$name shader" "not in the project" "Only needed if your avatar uses it; the check names the shader an avatar needs. Add it with Creator Companion (Manage Project)." }
    }

    # 5b. UniVRM: only for VRoid Studio (.vrm) avatars.
    $uniVrm = @("com.vrmc.univrm", "com.vrmc.vrm", "com.vrmc.gltf") | Where-Object { Test-Path (Join-Path $packages $_) }
    if ($uniVrm -or (Test-Path (Join-Path $Project "Assets\VRM")) -or (Test-Path (Join-Path $Project "Assets\VRM10"))) { Row "OK" "UniVRM (for VRoid .vrm files)" "in the project" }
    else { Row "INFO" "UniVRM (for VRoid .vrm files)" "not in the project" "Only needed for VRoid avatars: download the UniVRM .unitypackage from https://github.com/vrm-c/UniVRM/releases and import it (frosty.ps1 import <file> -AllowScripts, or the Avatar Guide)." }

    # 6. Blender: only for avatars that come as .blend files.
    $blender = if ($env:FROSTY_BLENDER) { $env:FROSTY_BLENDER } else {
        (Get-ChildItem "C:\Program Files\Blender Foundation" -Filter blender.exe -Recurse -Depth 1 -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1).FullName }
    if ($blender -and (Test-Path $blender)) { Row "OK" "Blender" $blender }
    else { Row "INFO" "Blender" "not found" "Only needed for .blend files (audit/export). Booth packages, .fbx and VRoid files do not need it." }

    # 7. Disk space and whether Unity has the project open.
    $drive = Get-PSDrive -Name ($Project.Substring(0, 1)) -ErrorAction SilentlyContinue
    if ($drive) {
        $freeGb = [math]::Round($drive.Free / 1GB, 1)
        if ($freeGb -lt 10) { Row "OPTIONAL" "Free disk space" "$freeGb GB on $($drive.Name):" "Unity and uploads need room; free up at least 10 GB." }
        else { Row "OK" "Free disk space" "$freeGb GB on $($drive.Name):" }
    }
    $open = Test-UnityOpen
    Row "INFO" "Unity Editor" $(if ($open) { "has this project open (close it before running Unity commands such as check, prepare or upload)" } else { "closed" }) ""
    Row "INFO" "VRChat sign-in" "checked inside Unity when you upload (VRChat SDK window, Authentication tab)" ""

    # The same result as a file, for Frosty Neon Studio (tools/app).
    New-Item -ItemType Directory -Force -Path $Logs | Out-Null
    $doctorJson = [ordered]@{ tool = "frosty-neon-doctor"; needed = $script:needed; advised = $script:advised; rows = $script:rows }
    [IO.File]::WriteAllText((Join-Path $Logs "doctor.json"), ($doctorJson | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding $false))
    Say ""
    if ($script:needed) { Say "$($script:needed) thing(s) to install first (red, in the order above). Then run '.\frosty.ps1 doctor' again." "Red"; exit 1 }
    if ($script:advised) { Say "Ready for PC avatars. $($script:advised) suggestion(s) above (yellow)." "Yellow"; return }
    Say "All set! Open the project in Unity and choose Frosty Neon > Avatar Guide (start here)." "Green"
}

function Do-Pipeline([string] $assetPath, [bool] $apply) {
    $assetPath = $assetPath.Replace('\', '/')
    # An old report is removed first, so a run that fails early cannot show the last run's result.
    $report = Join-Path $Logs ("pipeline_" + [IO.Path]::GetFileNameWithoutExtension($assetPath) + ".json")
    if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report }
    $extra = @("-pipelineAsset", $assetPath)
    if ($apply) { $extra += "-pipelineApply" }
    if ($Height -gt 0) { $extra += @("-pipelineHeight", $Height.ToString([Globalization.CultureInfo]::InvariantCulture)) }
    $code = Invoke-Unity "FrostyNeon.Editor.AvatarPipeline.Run" $extra "pipeline.log"
    Show-Pipeline $assetPath
    if ($code -ne 0) { exit 1 }
}

switch ($Command.ToLowerInvariant()) {
    "audit"   { Do-Audit }
    "export"  { Do-Export | Out-Null }
    "check"   { Require-Target "an Assets/... model path"; Do-Pipeline $Target $false }
    "prepare" { Require-Target "an Assets/... model path"; Do-Pipeline $Target $true }
    "upload"  { Do-Upload }
    "import"  { Do-Import }
    "model"   { Do-Model }
    "doctor"  { Do-Doctor }
    "list"    { Do-Command "List" @() "list" }
    "inspect" {
        Require-Target "the avatar prefab (Assets/....prefab)"
        Do-Command "Inspect" @("-inspectPrefab", $Target.Replace('\', '/')) "inspect"
    }
    "fix" {
        Require-Target "the avatar prefab (Assets/....prefab)"
        $extra = @("-fixPrefab", $Target.Replace('\', '/'))
        if ($DryRun) { $extra += "-fixDryRun" }
        if ($Undo) { $extra += "-fixUndo" }
        Do-Command "Fix" $extra "fix"
    }
    "quest" {
        Require-Target "the avatar prefab (Assets/....prefab)"
        $extra = @("-questPrefab", $Target.Replace('\', '/'))
        if ($Remove) { $extra += "-questRemove" }
        Do-Command "Quest" $extra "quest"
    }
    "vroid" {
        $extra = @("-vroidFile", (Resolve-Input "the VRoid file (.vrm)"))
        if ($AcceptTerms) { $extra += "-vroidAcceptTerms" }
        Do-Command "Vroid" $extra "vroid"
    }
    "preview" {
        Require-Target "an Assets/... model or prefab path"
        $code = Invoke-Unity "FrostyNeon.Editor.AvatarPreviewRender.Run" @("-previewAsset", $Target.Replace('\', '/')) "preview.log"
        if ($code -ne 0) { Fail "Preview failed. See $Logs\preview.log" }
        Say "Previews are in $Logs\previews" "Green"
    }
    "all" {
        Do-Audit
        $asset = Do-Export
        Do-Pipeline $asset $true
    }
    "selftest" {
        $failed = 0
        foreach ($tool in "audit_avatar_scene.py", "export_avatar_fbx.py") {
            $code = Invoke-Blender @("--factory-startup", "--python", (Join-Path $Root "tools\blender\$tool"), "--", "--self-test") "selftest_$tool.log"
            $line = (Select-String -LiteralPath (Join-Path $Logs "selftest_$tool.log") -Pattern "SELF-TEST" | Select-Object -Last 1).Line
            if ($code -eq 0) { Say "PASS  Blender $tool  $line" "Green" } else { Say "FAIL  Blender $tool  $line" "Red"; $failed++ }
        }
        foreach ($test in "AvatarPreflightSelfTest", "AvatarSetupHelperSelfTest", "AvatarMaterialSetupSelfTest", "AvatarDescriptorSetupSelfTest", "AvatarUploadRequestSelfTest", "AvatarGuideSelfTest", "AvatarGuidedFixSelfTest", "AvatarQuestVersionSelfTest") {
            $code = Invoke-Unity "FrostyNeon.Editor.$test.Run" @() "selftest_$test.log"
            $line = (Select-String -LiteralPath (Join-Path $Logs "selftest_$test.log") -Pattern "self-test: " | Select-Object -Last 1).Line
            if ($code -eq 0) { Say "PASS  Unity $test  $line" "Green" } else { Say "FAIL  Unity $test  $line" "Red"; $failed++ }
        }
        # Frosty Neon Studio (tools/app): window, report display and buttons, from sample reports. No Unity.
        $ErrorActionPreference = "Continue"    # its stderr must not end this script (see Invoke-Blender)
        $studio = & powershell -NoProfile -ExecutionPolicy Bypass -STA -File (Join-Path $Root "tools\app\FrostyNeonStudio.ps1") -SelfTest 2>&1 | ForEach-Object { "$_" }
        $studioCode = $LASTEXITCODE
        $ErrorActionPreference = "Stop"
        $line = ($studio | Select-String "self-test: " | Select-Object -Last 1)
        if ($studioCode -eq 0) { Say "PASS  Frosty Neon Studio  $line" "Green" } else { Say "FAIL  Frosty Neon Studio  $line" "Red"; $studio | ForEach-Object { Say "    $_" }; $failed++ }
        if ($failed) { Fail "$failed self-test(s) failed." }
        Say "All self-tests passed." "Green"
    }
    default { Get-Help $PSCommandPath -Detailed }
}

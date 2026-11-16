<#
.SYNOPSIS
  Frosty Neon Studio: the avatar toolkit in one window, without opening Unity.

.DESCRIPTION
  A small Windows (WPF) window over frosty.ps1. It runs the toolkit's commands in the background (doctor, list,
  inspect, import, vroid, model, fix, quest, upload), reads the JSON reports they write to
  Unity\FrostyAvatarProject\Logs\FrostyNeon and shows them in plain words, with preview pictures. Unity runs hidden
  in batch mode; it only opens for an upload, where VRChat's sign-in and the upload confirmation happen.
  Nothing is uploaded without the person's confirmation, and uploads are private.

  Start it with "Frosty Neon Studio.cmd" in the repository folder, or:
    powershell -NoProfile -ExecutionPolicy Bypass -STA -File tools\app\FrostyNeonStudio.ps1

  Test options:
    -SelfTest                  load the window, fill it from sample reports, check it, exit 0 (pass) or 1. No Unity.
                               With -Screenshot <file.png> it also saves pictures of several states beside that file.
    -Screenshot <file.png>     start normally (doctor, list, and -Select <prefab> if given), save a picture of the
                               window when it is idle, then close.
    -Hide <name>,<name>        (with -Screenshot) leave those avatars out of the list, e.g. private ones, for
                               pictures that will be published.
    -Add <file> -Yes           (with -Screenshot) add that avatar file as if it was dropped on the window, answering
                               Yes to the app's own questions and writing its messages to the log. Uploads are
                               refused in this mode.

  This file is plain ASCII on purpose (Windows PowerShell 5.1 reads scripts without a byte-order mark as ANSI);
  write other characters as [char] codes. The XAML file is read as UTF-8.
#>
param(
    [string] $Screenshot,
    [string] $Select,
    [string] $Add,
    [string[]] $Hide,
    [switch] $Yes,
    [switch] $SelfTest
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

$AppDir = $PSScriptRoot
$Root = (Resolve-Path (Join-Path $AppDir "..\..")).Path
$Frosty = Join-Path $Root "frosty.ps1"
$Project = Join-Path $Root "Unity\FrostyAvatarProject"
$Logs = Join-Path $Project "Logs\FrostyNeon"
$IconPath = Join-Path $Root "brand\icon.ico"
$Version = $(if (Test-Path (Join-Path $Root "VERSION")) { (Get-Content (Join-Path $Root "VERSION") -TotalCount 1).Trim() } else { "dev" })
$StatePath = Join-Path $Logs "studio.json"    # what the app remembers between runs (last avatar, window place)
$Tick = [string][char]0x2713
$Dot = [string][char]0x2022
$Cross = [string][char]0x2715
$Quiet = $Yes -or $SelfTest                   # no message boxes: answers and messages go to the log

# Text colours used from code (the self-test checks each against its background for WCAG AA contrast).
$Colours = @{ Text = "#E8ECFF"; Dim = "#A8B0E0"; Good = "#6CFFB0"; Warn = "#FFD36E"; Bad = "#FF8FA6"; Ink = "#0D0F1E"
              Pass = "#4CFFA0"; Caution = "#FFC94A"; Fail = "#FF5A7A"; Cyan = "#3EF2FF"; Card = "#181B35" }

[xml] $xaml = Get-Content (Join-Path $AppDir "FrostyNeonStudio.xaml") -Raw -Encoding UTF8
$window = [Windows.Markup.XamlReader]::Load((New-Object System.Xml.XmlNodeReader $xaml))
$window.Title = "Frosty Neon Studio $Version"
$window.Content.Background = $window.Background
$ui = @{}
$names = "BusyText", "Busy", "MenuButton", "MenuShortcut", "MenuCopy", "MenuLogs", "MenuDoctor",
         "NoticeBox", "NoticeText", "NoticeButton", "NoticeClose", "SetupText", "SetupAgainButton",
         "AddButton", "RefreshButton", "AvatarList", "ListEmpty", "EmptyPanel", "EmptyTitle", "EmptyText", "EmptyDrop",
         "DetailScroll", "AvatarName", "Headline", "Pills", "TermsBox", "TermsText", "Pic1", "Pic2", "Pic3", "Facts",
         "FixButton", "UndoFixButton", "QuestButton", "QuestLookButton", "RemoveQuestButton", "UploadPcButton",
         "UploadQuestButton", "Findings", "DetailsToggle", "CopyButton", "DetailsFold", "DetailsText", "Log",
         "DropOverlay", "DropText", "DropHint"
foreach ($name in $names) { $ui[$name] = $window.FindName($name) }

$script:current = $null      # the inspect report of the chosen avatar
$script:doctor = $null       # the doctor report (what is installed)
$script:job = $null          # the command running now (one at a time)
$script:ctx = @{}            # what the running command is about (file, prefab, ...)
$script:retry = $null        # the last command started, for the notice's "Try again"
$script:noticeAction = $null
$script:lastError = $null    # the last "ERROR: ..." line frosty.ps1 printed
$script:lastStarted = $null  # when the last command started (older report files are left over from earlier runs)
$script:showQuest = $false
$script:shotAt = $null
$script:flashUntil = $null
$script:said = @()           # messages and questions in -Yes / -SelfTest mode
$script:selectAfterList = $Select
$script:firstList = $true
$script:quietSelect = $false
$script:listed = $false      # the avatar list has loaded at least once

function Brush([string] $colour) { return (New-Object Windows.Media.BrushConverter).ConvertFromString($colour) }

# ------------------------------------------------------------------ helpers
function Log([string] $text) {
    # Paths relative to the toolkit folder: shorter, and screenshots or pasted logs don't show the Windows user name.
    $text = Private $text
    $ui.Log.AppendText($text + "`r`n")
    $ui.Log.ScrollToEnd()
}

function Read-Json([string] $file) {
    if (-not (Test-Path $file)) { return $null }
    return Get-Content $file -Raw -Encoding UTF8 | ConvertFrom-Json
}

# A report written by the command that just ran: not one left over from an earlier run, and not half written.
function Fresh-Json([string] $file) {
    if (-not (Test-Path $file)) { return $null }
    if ($script:lastStarted -and (Get-Item $file).LastWriteTime -lt $script:lastStarted.AddSeconds(-2)) { return $null }
    try { return Read-Json $file } catch { Log ("  (could not read " + $file + ": " + $_.Exception.Message + ")"); return $null }
}

function Result([string] $name) { Fresh-Json (Join-Path $Logs "${name}_result.json") }

function Lines($items) { return @($items) | Where-Object { $_ } }

# The studio's own dialog: dark like the window, buttons that say what they do, Enter and Escape work.
$DialogXaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Width="500" SizeToContent="Height" ResizeMode="NoResize" WindowStartupLocation="CenterOwner" ShowInTaskbar="False"
        Background="#181B35" Foreground="#E8ECFF" FontFamily="Segoe UI" FontSize="13" UseLayoutRounding="True"
        TextOptions.TextFormattingMode="Display">
  <StackPanel Margin="24,20,24,18">
    <TextBlock x:Name="Heading" FontSize="17" FontWeight="SemiBold" Foreground="#3EF2FF" TextWrapping="Wrap"/>
    <ScrollViewer MaxHeight="400" VerticalScrollBarVisibility="Auto" Focusable="False" Margin="0,10,0,0">
      <TextBlock x:Name="Body" TextWrapping="Wrap" LineHeight="19"/>
    </ScrollViewer>
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,20,0,0">
      <Button x:Name="YesButton"/>
      <Button x:Name="NoButton"/>
    </StackPanel>
  </StackPanel>
</Window>
'@

function New-Dialog([string] $text, [string] $heading, [string] $yes, [string] $no, [bool] $safeDefault) {
    $dialog = [Windows.Markup.XamlReader]::Parse($DialogXaml)
    $dialog.Title = "Frosty Neon Studio"
    if ($window.IsVisible) { $dialog.Owner = $window } else { $dialog.WindowStartupLocation = "CenterScreen" }
    if ($window.Icon) { $dialog.Icon = $window.Icon }
    $dialog.FindName("Heading").Text = $heading
    $dialog.FindName("Body").Text = $text
    $yesButton = $dialog.FindName("YesButton")
    $noButton = $dialog.FindName("NoButton")
    $yesButton.Style = $window.FindResource("Neon"); $yesButton.Margin = "0"; $yesButton.MinWidth = 96
    $noButton.Style = $window.FindResource("Quiet"); $noButton.Margin = "8,0,0,0"; $noButton.MinWidth = 96
    $yesButton.Content = $yes
    $yesButton.Add_Click({ [Windows.Window]::GetWindow($this).DialogResult = $true })
    if ($no) {
        $noButton.Content = $no
        $noButton.IsCancel = $true                      # Escape (and the window's X) mean No
        if ($safeDefault) { $noButton.IsDefault = $true; $focus = $noButton } else { $yesButton.IsDefault = $true; $focus = $yesButton }
    } else {
        $noButton.Visibility = "Collapsed"
        $yesButton.IsDefault = $true; $yesButton.IsCancel = $true    # a message: Enter or Escape close it
        $focus = $yesButton
    }
    [Windows.Input.FocusManager]::SetFocusedElement($dialog, $focus)
    return $dialog
}

function Ask([string] $text, [string] $heading, [string] $yes = "Yes", [string] $no = "Cancel", [bool] $safeDefault = $false) {
    if ($Quiet) {
        $script:said += $heading + ": " + $text
        Log ("  [question: " + $heading + "] " + ($text -replace "`r?`n", " ") + $(if ($Yes) { " -> " + $yes } else { " -> " + $no }))
        return [bool]$Yes
    }
    return [bool](New-Dialog $text $heading $yes $no $safeDefault).ShowDialog()
}

function Tell([string] $text, [string] $heading = "Frosty Neon Studio") {
    if ($Quiet) { $script:said += $heading + ": " + $text; Log ("  [message: " + $heading + "] " + ($text -replace "`r?`n", " ")); return }
    [void](New-Dialog $text $heading "OK" $null $false).ShowDialog()
}

function Pick-File([string] $filter) {
    $dialog = New-Object Microsoft.Win32.OpenFileDialog
    $dialog.Filter = $filter
    $dialog.Title = "Add an avatar file"
    if ($dialog.ShowDialog($window)) { return $dialog.FileName }
    return $null
}

# A short note in the banner that clears itself ("Copied").
function Flash([string] $text) {
    if ($script:job) { Log ("  " + $text); return }
    $ui.BusyText.Text = $text
    $script:flashUntil = (Get-Date).AddSeconds(3)
}

# ------------------------------------------------------------------ remembering things between runs
function Load-State { try { return Read-Json $StatePath } catch { return $null } }

function Save-State([hashtable] $changes) {
    try {
        $state = [ordered]@{}
        $old = Load-State
        if ($old) { foreach ($p in $old.PSObject.Properties) { $state[$p.Name] = $p.Value } }
        foreach ($k in $changes.Keys) { $state[$k] = $changes[$k] }
        New-Item -ItemType Directory -Force -Path (Split-Path $StatePath) | Out-Null
        [IO.File]::WriteAllText($StatePath, ($state | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding $false))
    } catch { }
}

function Remember([string] $prefab) { Save-State @{ last_avatar = $prefab } }

function Recall { $state = Load-State; if ($state) { return [string]$state.last_avatar }; return $null }

function Window-Placement {
    $maximized = $window.WindowState -eq "Maximized"
    $b = $(if ($window.WindowState -eq "Normal") { New-Object Windows.Rect($window.Left, $window.Top, $window.Width, $window.Height) } else { $window.RestoreBounds })
    return [ordered]@{ left = [int]$b.Left; top = [int]$b.Top; width = [int]$b.Width; height = [int]$b.Height; maximized = $maximized }
}

# Where to put the window: the remembered place if it is still on a screen (screens change), sized to fit.
function Fit-Placement($saved, [Windows.Rect] $screens, [Windows.Rect] $work) {
    if (-not $saved -or -not $saved.width -or -not $saved.height) { return $null }
    $w = [math]::Max($window.MinWidth, [math]::Min([double]$saved.width, $work.Width))
    $h = [math]::Max($window.MinHeight, [math]::Min([double]$saved.height, $work.Height))
    $left = [double]$saved.left; $top = [double]$saved.top
    $onScreen = ($left + $w - 120 -gt $screens.Left) -and ($left + 120 -lt $screens.Right) -and ($top -gt $screens.Top - 8) -and ($top + 60 -lt $screens.Bottom)
    if (-not $onScreen) { $left = $work.Left + ($work.Width - $w) / 2; $top = $work.Top + ($work.Height - $h) / 2 }
    return @{ Left = $left; Top = $top; Width = $w; Height = $h; Maximized = [bool]$saved.maximized }
}

function Place-Window {
    $p = [Windows.SystemParameters]
    $screens = New-Object Windows.Rect($p::VirtualScreenLeft, $p::VirtualScreenTop, $p::VirtualScreenWidth, $p::VirtualScreenHeight)
    $work = $p::WorkArea
    $state = Load-State
    $fit = Fit-Placement $(if ($state) { $state.window } else { $null }) $screens $work
    if ($fit) {
        $window.WindowStartupLocation = "Manual"
        $window.Left = $fit.Left; $window.Top = $fit.Top; $window.Width = $fit.Width; $window.Height = $fit.Height
        if ($fit.Maximized) { $window.WindowState = "Maximized" }
    } else {
        # First run: the designed size, or the screen's size at high Windows scaling (125-150 % on a laptop).
        $window.Width = [math]::Max($window.MinWidth, [math]::Min($window.Width, $work.Width))
        $window.Height = [math]::Max($window.MinHeight, [math]::Min($window.Height, $work.Height))
    }
}

# The window and taskbar icon, from brand\icon.ico when there is one (the default icon otherwise).
function Set-AppIcon([string] $path) {
    if (-not $path -or -not (Test-Path $path)) { return $false }
    try { $window.Icon = [Windows.Media.Imaging.BitmapFrame]::Create((New-Object Uri($path))); return $true } catch { return $false }
}

# "Create a desktop shortcut": a .lnk that starts the studio without a console window, with the studio's icon.
function New-Shortcut([string] $folder) {
    $link = Join-Path $folder "Frosty Neon Studio.lnk"
    $shell = New-Object -ComObject WScript.Shell
    try {
        $s = $shell.CreateShortcut($link)
        $s.TargetPath = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
        $s.Arguments = '-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "' + (Join-Path $AppDir "FrostyNeonStudio.ps1") + '"'
        $s.WorkingDirectory = $Root
        $s.WindowStyle = 7    # minimised, so no console flashes up
        $s.Description = "Frosty Neon Studio: bring your avatar into VRChat"
        if (Test-Path $IconPath) { $s.IconLocation = $IconPath + ",0" }
        $s.Save()
    } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell) }
    return $link
}

# ------------------------------------------------------------------ what can go wrong, in plain words
function Test-UnityOpen([string] $projectDir = $Project) {
    # Unity (the window, or a hidden step still running) holds Temp\UnityLockfile while it has the project open.
    $lock = Join-Path $projectDir "Temp\UnityLockfile"
    if (-not (Test-Path $lock)) { return $false }
    try { [IO.File]::Open($lock, "Open", "ReadWrite", "None").Close(); return $false } catch { return $true }
}

$UnityOpenText = "Unity has this project open: either the Unity window is open, or a step started earlier is still finishing. " +
                 "Close Unity (save if it asks), wait a moment, then click Try again."

# frosty.ps1's last error line -> what it means and what to do. Action: "retry" (Try again) or "doctor" (Check this PC again).
function Explain-Error([string] $message) {
    $m = [string]$message
    $help = "If it keeps happening, choose More > Copy details for a helper and send that to whoever helps you."
    if ($m -match 'has this project open') { return @{ Text = $UnityOpenText; Action = "retry" } }
    if ($m -match 'Unity .*was not found|FROSTY_UNITY points') { return @{ Text = "Unity isn't installed yet, or not the exact version VRChat needs. The Your PC card on the left says what to install."; Action = "doctor" } }
    if ($m -match 'script compile errors') { return @{ Text = "Unity couldn't load the scripts in the project; usually a package brought in a broken script. $help"; Action = "retry" } }
    if ($m -match '^File not found') { return @{ Text = "The file isn't there any more. It may have been moved or renamed: add it again from where it is now."; Action = "" } }
    if ($m -match 'A different .* is already in the project') { return @{ Text = "Your project already has a different model with the same file name. Rename your file (for example, add a 2 to its name) and add it again."; Action = "" } }
    if ($m -match 'Android|could not switch to') { return @{ Text = "Unity can't build for Quest yet: its Android Build Support is missing. The Your PC card on the left says how to add it."; Action = "doctor" } }
    if ($m -match 'check fails, so nothing was uploaded') { return @{ Text = "The check found something to fix first; the list on the right says what."; Action = "" } }
    if ($m -match 'No result after 30 minutes') { return @{ Text = "Unity sent no answer within 30 minutes, so the studio stopped waiting. Look at the Unity window: nothing is uploaded unless it says so."; Action = "" } }
    if ($m -match 'No avatar prefab at|not an avatar prefab') { return @{ Text = "This avatar isn't set up as a VRChat avatar any more. Click Refresh and pick it again."; Action = "" } }
    if ($m -match 'No .*(report|result) was written|did not run') { $m = "" }
    if (-not $m) { return @{ Text = "Unity stopped before it finished, without saying why. Click Try again. $help"; Action = "retry" } }
    return @{ Text = ("It stopped with this message: " + $m.TrimEnd(".") + ". " + $help); Action = "retry" }
}

function Show-Notice([string] $text, [string] $action) {
    $ui.NoticeText.Text = $text
    $script:noticeAction = $action
    $ui.NoticeButton.Visibility = $(if ($action) { "Visible" } else { "Collapsed" })
    $ui.NoticeButton.Content = $(if ($action -eq "doctor") { "_Check this PC again" } else { "_Try again" })
    $ui.NoticeBox.Visibility = "Visible"
    Log ("! " + $text)
}

function Hide-Notice { $ui.NoticeBox.Visibility = "Collapsed"; $script:noticeAction = $null }

# A step ended without its report: say what did not happen, why, and what to do.
function Step-Failed([string] $what) {
    $why = Explain-Error $script:lastError
    Show-Notice ($what + " " + $why.Text) $why.Action
}

# Closing while a step runs: the hidden step is not stopped (stopping Unity halfway could break the project).
function Close-Warning([string] $label) {
    if ($label -match '^Uploading') {
        return "An upload is going on in the Unity window. Closing the studio doesn't stop it: finish or cancel it in Unity. " +
               "You just won't see the result here.`n`nClose the studio anyway?"
    }
    $doing = $label.TrimEnd(".")
    $doing = $doing.Substring(0, 1).ToLowerInvariant() + $doing.Substring(1)
    return "The studio is still $doing.`n`nIf you close it now, that step carries on hidden in the background and finishes by itself, " +
           "usually within a few minutes. Nothing is lost: you'll see the result the next time you open the studio " +
           "(if you open it sooner, it asks you to wait a moment).`n`nClose anyway?"
}

# ------------------------------------------------------------------ running frosty.ps1 in the background
# One command-line argument for Start-Process: quoted when it has spaces, with trailing backslashes doubled so they
# don't escape the closing quote. A double quote can't be passed safely (and Windows file names can't contain one).
function Quote-Argument([string] $text) {
    if ($text.Contains('"')) { throw "A double quote can't be passed to frosty.ps1: $text" }
    if ($text -eq "" -or $text -match '\s') { return '"' + ($text -replace '(\\+)$', '$1$1') + '"' }
    return $text
}

# The -Command text that runs frosty.ps1 with UTF-8 output (so file names in any language reach the log intact)
# and passes its exit code back. Arguments go in single quotes, where PowerShell takes $ and ` literally.
function Frosty-Command([string[]] $arguments) {
    $literal = @($Frosty) + $arguments | ForEach-Object { "'" + ([string]$_).Replace("'", "''") + "'" }
    return "[Console]::OutputEncoding = [Text.Encoding]::UTF8; & " + ($literal -join " ") + "; exit `$LASTEXITCODE"
}

function Run-Frosty([string[]] $arguments, [string] $label, [scriptblock] $done) {
    if ($script:job) { Tell "The studio is still working on the last step. Please wait for it to finish." "One moment"; return }
    Hide-Notice
    $script:retry = @{ Args = $arguments; Label = $label; Done = $done; Ctx = $script:ctx.Clone() }
    if ($arguments[0] -ne "doctor" -and (Test-UnityOpen)) {
        Show-Notice $UnityOpenText "retry"
        if (-not $script:current) { Show-Empty } else { Update-Buttons }
        return
    }
    $out = [IO.Path]::GetTempFileName()
    $err = [IO.Path]::GetTempFileName()
    Log ("> frosty.ps1 " + ($arguments -join " "))
    try {
        $argLine = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", (Quote-Argument (Frosty-Command $arguments)))
        $process = Start-Process powershell.exe -ArgumentList $argLine -WorkingDirectory $Project -WindowStyle Hidden `
                                 -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
        [void]$process.Handle    # keeps the exit code readable after the process ends
    } catch {
        Show-Notice ("The studio couldn't start its helper (Windows PowerShell): " + $_.Exception.Message) "retry"
        return
    }
    $script:lastError = $null
    $script:lastStarted = Get-Date
    $script:job = @{ Process = $process; Out = $out; Err = $err; Read = 0; Done = $done; Label = $label; Started = $script:lastStarted }
    Set-Busy $label
}

function Pump-Output($job, [string] $file, [bool] $all) {
    try {
        $stream = [IO.File]::Open($file, "Open", "Read", "ReadWrite")
        try {
            if (-not $all) { [void]$stream.Seek($job.Read, "Begin") }
            $reader = New-Object IO.StreamReader($stream, (New-Object Text.UTF8Encoding $false))
            $text = $reader.ReadToEnd()
            if (-not $all) { $job.Read = $stream.Position }
        } finally { $stream.Close() }
        foreach ($line in ($text -split "`r?`n")) {
            if (-not $line.Trim()) { continue }
            Log ("  " + $line.TrimEnd())
            if ($line -match '^ERROR: (.+)$') { $script:lastError = $Matches[1].Trim() }
            elseif ($all -and -not $script:lastError) { $script:lastError = $line.Trim() }    # PowerShell's own error text
        }
    } catch { }
}

$timer = New-Object Windows.Threading.DispatcherTimer
$timer.Interval = [TimeSpan]::FromMilliseconds(400)
$timer.Add_Tick({
    $job = $script:job
    if ($job) {
        Pump-Output $job $job.Out $false
        $ui.BusyText.Text = Busy-Label $job.Label ((Get-Date) - $job.Started)
        if ($job.Process.HasExited) {
            Pump-Output $job $job.Out $false
            Pump-Output $job $job.Err $true
            $code = $job.Process.ExitCode
            Remove-Item $job.Out, $job.Err -ErrorAction SilentlyContinue
            $script:job = $null
            Set-Busy $null
            try { & $job.Done $code } catch { Show-Notice ("Something went wrong in the studio itself: " + $_.Exception.Message) "" }
            if (-not $script:job -and $Screenshot) { $script:shotAt = (Get-Date).AddSeconds(1.5) }
        }
    }
    if ($script:flashUntil -and (Get-Date) -gt $script:flashUntil) {
        $script:flashUntil = $null
        if (-not $script:job) { $ui.BusyText.Text = "" }
    }
    if ($script:shotAt -and (Get-Date) -gt $script:shotAt -and -not $script:job) {
        $script:shotAt = $null
        Save-Screenshot $Screenshot
        $window.Close()
    }
})

# "Checking your avatar...  0:42", with a reassuring note once a step takes a while (Unity starts slowly).
function Busy-Label([string] $label, [TimeSpan] $elapsed) {
    $clock = "{0}:{1:00}" -f [int][math]::Floor($elapsed.TotalMinutes), $elapsed.Seconds
    $note = $(if ($elapsed.TotalSeconds -ge 300) { "`nStill going; the activity log shows each step." }
              elseif ($elapsed.TotalSeconds -ge 25) { "`nUnity is working; this can take a minute." } else { "" })
    return "$label  $clock$note"
}

function Set-Busy([string] $label) {
    $on = [bool]$label
    $ui.Busy.Visibility = $(if ($on) { "Visible" } else { "Hidden" })
    $ui.Busy.IsIndeterminate = $on
    $ui.BusyText.Text = $(if ($on) { $label } else { "" })
    $script:flashUntil = $null
    Update-Buttons
}

# Turn a button on or off; its tooltip says what it does, or why it is off.
function Enable($button, [bool] $on, [string] $why, [string] $what) {
    $button.IsEnabled = $on
    $button.ToolTip = $(if ($on) { $what } else { $why })
}

function Update-Buttons {
    $r = $script:current
    $idle = -not $script:job
    $wait = "Wait for the current step to finish."
    $setupNeeded = $script:doctor -and $script:doctor.needed
    $blocked = $(if (-not $idle) { $wait } elseif ($setupNeeded) { "First install what the Your PC card lists, then click Check again." } else { "" })
    Enable $ui.AddButton (-not $blocked) $blocked "A Booth package (.unitypackage), a VRoid file (.vrm) or a model (.fbx)"
    Enable $ui.RefreshButton (-not $blocked) $blocked "Look for avatars in the project again"
    $ui.AvatarList.IsEnabled = -not $blocked
    Enable $ui.SetupAgainButton $idle $wait "Check what is installed again, after installing something"
    $ui.MenuDoctor.IsEnabled = $idle

    $have = $r -and $r.name
    $base = $(if (-not $idle) { $wait } elseif (-not $have) { "Pick an avatar first." } else { "" })
    $plan = @(Lines $(if ($have) { $r.fix_plan } else { $null }))
    Enable $ui.FixButton (-not $base -and $plan.Count -gt 0) $(if ($base) { $base } else { "Nothing to fix: the check found nothing this button can change." }) `
        "Changes the import settings listed below. Your own files are not edited, and you can undo it."
    Enable $ui.UndoFixButton (-not $base -and [bool]$r.fix_can_undo) $(if ($base) { $base } else { "Nothing to undo: Fix it for me hasn't changed this avatar." }) `
        "Puts back the import settings that Fix it for me changed."

    $hasQuest = $have -and [bool]$r.quest_version
    $ui.QuestButton.Content = $(if ($hasQuest) { "_Make the Quest version again" } else { "_Make a Quest version" })
    $questWhy = $(if ($base) { $base } elseif ($r.quest_status -ne "fail") { "Not needed: this avatar already passes the Quest check." }
                  else { "A Quest version wouldn't help here; the list below says what Quest needs." })
    Enable $ui.QuestButton (-not $base -and ($hasQuest -or [bool]$r.quest_version_needed)) $questWhy `
        $(if ($hasQuest) { "Builds the Quest version again from your PC avatar, for example after a fix." } else { "Makes a separate Quest copy with Quest materials. Your PC avatar is not changed." })

    $ui.QuestLookButton.Content = $(if ($script:showQuest) { "Show _PC look" } else { "Show _Quest look" })
    Enable $ui.QuestLookButton ($have -and @(Lines $r.quest_pictures).Count -gt 0) $(if (-not $have) { "Pick an avatar first." } else { "Make a Quest version first to see how it looks on Quest." }) `
        "Switch the pictures between the PC and the Quest version"
    Enable $ui.RemoveQuestButton (-not $base -and $hasQuest) $(if ($base) { $base } else { "There is no Quest version to remove." }) `
        "Deletes the Quest copy and puts back the texture settings it changed"

    $uploadTip = "Uploads it as a private avatar. Unity opens and asks you to confirm before anything goes up."
    Enable $ui.UploadPcButton (-not $base -and $r.pc_status -ne "fail") $(if ($base) { $base } else { "Not yet: fix what the list below says first. The PC check has to pass before an upload." }) `
        $uploadTip
    $android = @($(if ($script:doctor) { $script:doctor.rows } else { $null }) | Where-Object { $_ -and $_.what -match '^Quest uploads' -and $_.state -ne "OK" })
    $questUploadWhy = $(if ($base) { $base } elseif ($r.quest_version_needed -and -not $hasQuest) { "Not yet: make a Quest version first." }
                        elseif ($r.quest_status -eq "fail") { "Not yet: the Quest check has to pass first; the list below says what is missing." }
                        else { "Unity's Android Build Support is missing; the Your PC card says how to add it." })
    Enable $ui.UploadQuestButton (-not $base -and $r.quest_status -ne "fail" -and $android.Count -eq 0) $questUploadWhy $uploadTip
}

# ------------------------------------------------------------------ showing reports
# Text with clickable web links (for the doctor's "download it from https://..." advice).
function Add-Text($block, [string] $text, [string] $colour = $null, [bool] $bold = $false) {
    $parts = [regex]::Split($text, '(https://[^\s)]+)')
    foreach ($part in $parts) {
        if (-not $part) { continue }
        if ($part -match '^https://') {
            $url = $part.TrimEnd(".", ",")
            $link = New-Object Windows.Documents.Hyperlink
            $link.Inlines.Add($url)
            $link.NavigateUri = New-Object Uri($url)
            $link.ToolTip = "Opens in your web browser"
            $link.Add_Click({ Start-Process $this.NavigateUri.AbsoluteUri })
            $block.Inlines.Add($link)
            if ($part.Length -gt $url.Length) { $block.Inlines.Add($part.Substring($url.Length)) }
        } else {
            $run = New-Object Windows.Documents.Run($part)
            if ($colour) { $run.Foreground = Brush $colour }
            if ($bold) { $run.FontWeight = "SemiBold" }
            $block.Inlines.Add($run)
        }
    }
}

function Show-Setup($doctor) {
    $tb = $ui.SetupText
    $tb.Inlines.Clear()
    $ui.SetupAgainButton.Visibility = "Collapsed"
    if (-not $doctor) {
        Add-Text $tb "The studio couldn't check this PC. Click Check again; the activity log at the bottom says what happened."
        $ui.SetupAgainButton.Visibility = "Visible"
        return
    }
    $rows = @($doctor.rows | Where-Object { $_ })
    $ok = @($rows | Where-Object { $_.state -eq "OK" } | ForEach-Object { ($_.what -replace ' \(.*\)$', '') })
    $unityOpen = @($rows | Where-Object { $_.what -eq "Unity Editor" -and $_.detail -match 'has this project open' }).Count -gt 0
    if ($doctor.needed) {
        Add-Text $tb "Install these first, in this order:" $Colours.Bad $true
        foreach ($row in @($rows | Where-Object { $_.state -eq "MISSING" })) {
            Add-Text $tb ("`n$Cross  " + $row.what) $Colours.Bad $true
            if ($row.todo) { Add-Text $tb ("`n" + $row.todo) }
            Log ("  To do: " + $row.what + " -> " + $row.todo)
        }
        Add-Text $tb "`nThen click Check again."
        $ui.SetupAgainButton.Visibility = "Visible"
    } else {
        Add-Text $tb $(if ($doctor.advised) { "Ready for PC avatars." } else { "All set!" }) $Colours.Good $true
        foreach ($row in @($rows | Where-Object { $_.state -eq "OPTIONAL" })) {
            Add-Text $tb ("`n!  " + $row.what) $Colours.Warn $true
            if ($row.todo) { Add-Text $tb ("`n" + $row.todo) $Colours.Dim }
        }
    }
    if ($unityOpen) { Add-Text $tb "`n!  Unity has this project open. Close it before checking avatars." $Colours.Warn $true }
    if ($ok.Count) { Add-Text $tb ("`n$Tick  " + ($ok -join ", ")) $Colours.Dim }
}

# The right side before an avatar is picked: what to do next, depending on where the person is.
function Show-Empty {
    $script:current = $null
    $ui.EmptyPanel.Visibility = "Visible"
    $ui.DetailScroll.Visibility = "Collapsed"
    $ui.EmptyDrop.Visibility = "Visible"
    if ($script:doctor -and $script:doctor.needed) {
        $ui.EmptyTitle.Text = "First, set up your PC"
        $ui.EmptyText.Text = "The Your PC card on the left lists what to install. It's free and takes a little while. When it's done, click Check again."
        $ui.EmptyDrop.Visibility = "Collapsed"
    } elseif (-not $script:listed) {
        $ui.EmptyTitle.Text = "Your avatars will show up here"
        $ui.EmptyText.Text = "The studio looks for them as soon as it can. If a message above asks you to do something, do that first. You can also drop an avatar file anywhere on this window."
    } elseif ($ui.AvatarList.Items.Count -eq 0) {
        $ui.EmptyTitle.Text = "Add your first avatar"
        $ui.EmptyText.Text = "Click Add an avatar file, or drop the file anywhere on this window: a Booth package (.unitypackage), a VRoid file (.vrm) or a model (.fbx)."
    } else {
        $ui.EmptyTitle.Text = "Pick an avatar on the left"
        $ui.EmptyText.Text = "Or add another one: drop its file anywhere on this window (.unitypackage, .vrm or .fbx)."
    }
    Update-ListEmpty
    Update-Buttons
}

function Update-ListEmpty {
    $none = $ui.AvatarList.Items.Count -eq 0 -and ($script:listed -or ($script:doctor -and $script:doctor.needed))
    $ui.ListEmpty.Visibility = $(if ($none) { "Visible" } else { "Collapsed" })
    $ui.ListEmpty.Text = $(if ($script:doctor -and $script:doctor.needed) { "Your avatars show up here once your PC is set up." }
                           else { "No avatars yet.`nClick Add an avatar file above, or drop one on this window." })
}

function Show-List($list) {
    $keep = $(if ($script:current) { $script:current.prefab } else { $null })
    $script:listed = $true
    $ui.AvatarList.Items.Clear()
    $dim = Brush $Colours.Dim
    foreach ($a in @($list.avatars) | Where-Object { $_ }) {
        $item = New-Object Windows.Controls.ListBoxItem
        $row = New-Object Windows.Controls.StackPanel
        $title = New-Object Windows.Controls.TextBlock
        $title.Text = [string]$a.name
        $title.FontWeight = "SemiBold"
        $title.TextWrapping = "NoWrap"; $title.TextTrimming = "CharacterEllipsis"
        $kind = New-Object Windows.Controls.TextBlock
        $kind.Text = [string]$a.kind + $(if ($a.quest_version) { "  " + $Dot + "  has a Quest version" } else { "" })
        $kind.Foreground = $dim; $kind.FontSize = 12
        $kind.TextWrapping = "NoWrap"; $kind.TextTrimming = "CharacterEllipsis"
        [void]$row.Children.Add($title); [void]$row.Children.Add($kind)
        $item.Content = $row
        $item.Tag = $a.prefab
        $item.ToolTip = [string]$a.name    # the whole name, when it is too long for the list
        [Windows.Automation.AutomationProperties]::SetName($item, ([string]$a.name + ", " + $kind.Text))
        [void]$ui.AvatarList.Items.Add($item)
        if ($keep -and $a.prefab -eq $keep) { $script:quietSelect = $true; $ui.AvatarList.SelectedItem = $item; $script:quietSelect = $false }
    }
    if ($ui.AvatarList.Items.Count -eq 0) { Log "  No avatars in the project yet." }
    Update-ListEmpty
    if (-not $script:current) { Show-Empty }
}

function Pill([string] $text, [string] $status) {
    $colour = switch ($status) { "pass" { $Colours.Pass } "warn" { $Colours.Caution } default { $Colours.Fail } }
    $border = New-Object Windows.Controls.Border
    $border.Background = Brush $colour
    $border.CornerRadius = 4
    $border.Padding = "9,3,9,3"
    $border.Margin = "0,0,6,0"
    $block = New-Object Windows.Controls.TextBlock
    $block.Text = $text
    $block.Foreground = Brush $Colours.Ink
    $block.FontWeight = "Bold"
    $border.Child = $block
    [Windows.Automation.AutomationProperties]::SetName($border, $text)
    [void]$ui.Pills.Children.Add($border)
}

function Load-Image($image, [string] $path) {
    if (-not $path -or -not (Test-Path $path)) { $image.Source = $null; return }
    try {
        $bitmap = New-Object Windows.Media.Imaging.BitmapImage
        $bitmap.BeginInit()
        $bitmap.CacheOption = "OnLoad"
        $bitmap.CreateOptions = "IgnoreImageCache"
        $bitmap.UriSource = New-Object Uri($path)
        $bitmap.EndInit()
        $image.Source = $bitmap
    } catch { $image.Source = $null }    # a damaged picture file: show none rather than stop
}

function Show-Pictures {
    $r = $script:current
    $pictures = @(Lines $(if ($script:showQuest) { $r.quest_pictures } else { $r.pictures }))
    $images = $ui.Pic1, $ui.Pic2, $ui.Pic3
    for ($i = 0; $i -lt 3; $i++) { Load-Image $images[$i] $(if ($i -lt $pictures.Count) { $pictures[$i] } else { $null }) }
}

# The technical side, for whoever helps (or for a bug report): codes, objects, files. No personal folder names.
function Private([string] $text) {
    $text = $text.Replace($Root, ".")
    if ($env:USERPROFILE) { $text = $text.Replace($env:USERPROFILE, "%USERPROFILE%") }
    return $text
}

function Details-Text($r) {
    $t = New-Object Text.StringBuilder
    [void]$t.AppendLine("Frosty Neon Studio " + $Version + ", Windows " + [Environment]::OSVersion.Version + ", PowerShell " + $PSVersionTable.PSVersion)
    if ($r) {
        [void]$t.AppendLine("prefab: " + $r.prefab)
        [void]$t.AppendLine("pc_status: " + $r.pc_status + "   quest_status: " + $r.quest_status)
        foreach ($group in @($r.groups) | Where-Object { $_ }) {
            foreach ($item in @($group.items)) {
                $objects = @(Lines $item.objects)
                [void]$t.AppendLine("[" + $item.code + "]" + $(if ($item.platform) { " (" + $item.platform + ")" } else { "" }) +
                                    $(if ($objects.Count) { " on: " + ($objects -join ", ") } else { "" }))
            }
        }
        foreach ($c in Lines $r.fix_plan) { [void]$t.AppendLine("fix: " + $c) }
        if ($r.quest_version) { [void]$t.AppendLine("quest_version: " + $r.quest_version) }
        if ($r.last_upload) { [void]$t.AppendLine("last_upload: " + $r.last_upload.platform + " " + $r.last_upload.message) }
        if ($r.message -and -not $r.name) { [void]$t.AppendLine("message: " + $r.message) }
        [void]$t.AppendLine("report: " + (Join-Path $Logs "inspect_result.json"))
    }
    if ($script:lastError) { [void]$t.AppendLine("last error: " + $script:lastError) }
    [void]$t.Append("logs: " + $Logs)
    return Private $t.ToString()
}

function Helper-Text {
    $lines = @($ui.Log.Text -split "`r?`n" | Where-Object { $_ } | Select-Object -Last 80)
    return (Details-Text $script:current) + "`r`n`r`n--- activity (last lines) ---`r`n" + (Private ($lines -join "`r`n"))
}

function Show-Avatar($r) {
    $script:current = $r
    $script:showQuest = $false
    $ui.Pills.Children.Clear()
    $ui.EmptyPanel.Visibility = "Collapsed"
    $ui.DetailScroll.Visibility = "Visible"
    $ui.DetailScroll.ScrollToTop()
    $ui.DetailsText.Text = Details-Text $r
    if (-not $r -or -not $r.name) {
        $picked = $ui.AvatarList.SelectedItem
        $ui.AvatarName.Text = $(if ($picked) { [string]$picked.ToolTip } else { "This avatar" })
        $ui.Headline.Text = $(if ($r -and $r.message) { "It couldn't be checked: " + $r.message } else { "It couldn't be checked. The message at the top says why and what to do." })
        $ui.Findings.Text = ""; $ui.Facts.Text = ""; $ui.TermsBox.Visibility = "Collapsed"
        foreach ($img in $ui.Pic1, $ui.Pic2, $ui.Pic3) { $img.Source = $null }
        Update-Buttons
        return
    }
    $ui.AvatarName.Text = $r.name
    $ui.Headline.Text = $r.headline
    Pill ("PC  " + $(if ($r.pc_status -eq "fail") { "NEEDS WORK" } else { "READY" })) $r.pc_status
    Pill ($(if ($r.quest_version) { "QUEST VERSION  " } else { "QUEST  " }) + $(if ($r.quest_status -eq "fail") { "NEEDS WORK" } else { "READY" })) $r.quest_status
    if ($r.only_author_may_use) {
        $ui.TermsText.Text = "Its creator's terms say only the author may use it as an avatar. Upload it only if that is you, or if you have their permission.`n" +
                             ((Lines $r.vrm_terms) -join "`n")
        $ui.TermsBox.Visibility = "Visible"
    } else { $ui.TermsBox.Visibility = "Collapsed" }
    Show-Pictures
    $facts = "$($r.height_m) m tall" + $(if ($null -ne $r.eyes_m) { ", eyes at $($r.eyes_m) m (the cyan mark in the pictures)" } else { "" })
    $notes = @(Lines $r.floor_notes)
    $ui.Facts.Text = $facts + $(if ($notes.Count) { ".`n" + ($notes -join "`n") } else { ". Feet on the floor and eyes in the head. Looking good!" })

    $text = New-Object Text.StringBuilder
    foreach ($group in @($r.groups) | Where-Object { $_ }) {
        [void]$text.AppendLine($group.group.ToUpperInvariant())
        foreach ($item in @($group.items)) {
            $where = $(if ($item.platform) { "  [" + $item.platform + " only]" } else { "" })
            [void]$text.AppendLine("  $Dot " + $item.title + $where)
            [void]$text.AppendLine("     " + $item.meaning)
        }
        [void]$text.AppendLine()
    }
    $plan = @(Lines $r.fix_plan)
    if ($plan.Count) {
        [void]$text.AppendLine("WHAT FIX IT FOR ME WOULD CHANGE (import settings only; you can undo it)")
        $plan | ForEach-Object { [void]$text.AppendLine("  $Dot $_") }
        [void]$text.AppendLine()
    }
    if ($r.quest_version) {
        [void]$text.AppendLine("QUEST VERSION: " + $r.quest_version)
        foreach ($n in Lines $r.quest_version_notes) { [void]$text.AppendLine("  $Dot $n") }
        [void]$text.AppendLine()
    } elseif ($r.quest_version_needed) {
        [void]$text.AppendLine("QUEST")
        [void]$text.AppendLine("  $Dot Its materials need a Quest version. Make a Quest version builds a copy with Quest materials; the PC avatar stays as it is.")
        [void]$text.AppendLine()
    }
    if ($r.last_upload) { [void]$text.AppendLine("LAST UPLOAD (" + $r.last_upload.platform + "): " + $r.last_upload.message) }
    $ui.Findings.Text = $text.ToString().TrimEnd()
    Update-Buttons
}

# A picture of a window's inside: its background, then its content at its place (inside the content's margin).
function Save-Screenshot([string] $file, $owner = $null) {
    if (-not $owner) { $owner = $window }
    $element = $owner.Content
    $owner.UpdateLayout()
    $m = $element.Margin
    $width = $element.ActualWidth + $m.Left + $m.Right
    $height = $element.ActualHeight + $m.Top + $m.Bottom
    $backdrop = New-Object Windows.Media.DrawingVisual
    $dc = $backdrop.RenderOpen()
    $dc.DrawRectangle($owner.Background, $null, (New-Object Windows.Rect(0, 0, $width, $height)))
    $dc.Close()
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap([int]$width, [int]$height, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($backdrop)
    $bitmap.Render($element)    # drawn at its own offset, inside its margin
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create($file)
    try { $encoder.Save($stream) } finally { $stream.Close() }
}

# ------------------------------------------------------------------ actions
function Start-Doctor {
    Run-Frosty @("doctor") "Checking this PC..." {
        param($code)
        $d = Fresh-Json (Join-Path $Logs "doctor.json")
        $script:doctor = $d
        Show-Setup $d
        if (-not $d) { Step-Failed "The studio couldn't check this PC."; Show-Empty; return }
        if ($d.needed) { $ui.AvatarList.Items.Clear(); Show-Empty; return }    # nothing more until it is installed
        Start-List
    }
}

function Start-List {
    Run-Frosty @("list") "Looking for avatars..." {
        param($code)
        $list = Result "list"
        if (-not $list) { Step-Failed "The studio couldn't look for avatars."; Show-Empty; return }
        if ($Screenshot -and $Hide) {
            $list = [pscustomobject]@{ avatars = @(@($list.avatars) | Where-Object { $_ -and $Hide -notcontains [string]$_.name }) }
        }
        Show-List $list
        if ($script:firstList) {
            # On start, come back to the avatar picked last time (unless -Select names one).
            $script:firstList = $false
            if ($Add) { Add-AvatarFile $Add; return }
            if (-not $script:selectAfterList) { $script:selectAfterList = Recall }
        }
        if ($script:selectAfterList) {
            $wanted = $script:selectAfterList
            $script:selectAfterList = $null
            foreach ($item in $ui.AvatarList.Items) { if ($item.Tag -eq $wanted) { $ui.AvatarList.SelectedItem = $item; $ui.AvatarList.ScrollIntoView($item) } }
        }
    }
}

function Start-Inspect([string] $prefab) {
    $script:ctx = @{ Prefab = $prefab }
    Run-Frosty @("inspect", $prefab) "Checking your avatar..." {
        param($code)
        $r = Result "inspect"
        if (-not $r) { Step-Failed "The avatar check didn't finish." }
        Show-Avatar $r
        if ($r -and $r.name) { Remember $r.prefab }
    }
}

function Refresh-Then-Select([string] $prefab) {
    $script:selectAfterList = $prefab
    Start-List
}

$ui.AvatarList.Add_SelectionChanged({
    $item = $ui.AvatarList.SelectedItem
    if ($item -and -not $script:job -and -not $script:quietSelect) { Start-Inspect $item.Tag }
})

$ui.RefreshButton.Add_Click({ Start-List })
$ui.SetupAgainButton.Add_Click({ Start-Doctor })

function After-Import($code) {
    $r = Fresh-Json (Join-Path $Logs "package_import.json")
    if (-not $r) { Step-Failed "The package wasn't imported."; return }
    if ($r.mode -eq "refused") {
        $scripts = @(Lines $r.inspected.scripts)
        $shown = (($scripts | Select-Object -First 10) -join "`n") + $(if ($scripts.Count -gt 10) { "`n... and " + ($scripts.Count - 10) + " more" } else { "" })
        $ask = "This package contains " + $scripts.Count + " script file(s). Unity runs scripts as soon as they are imported, so only import it " +
               "if you trust where it came from (for example, the creator's own Booth page):`n`n" + $shown + "`n`nImport it with its scripts?"
        if (Ask $ask "This package has scripts" "Import it" "Don't import" $true) {
            Run-Frosty @("import", $script:ctx.File, "-AllowScripts") "Importing the package..." { param($code) After-Import $code }
        } else { Log "  Not imported." }
        return
    }
    if ($r.mode -ne "imported") {
        Log ("  " + $r.message)
        $why = $(if ($r.message -match 'did not finish within|cancelled') { "Unity took too long. Click Try again." }
                 else { "The file may be damaged or only partly downloaded: download it again from the creator's page, then add it once more." })
        Show-Notice ("The package couldn't be imported. " + $why) $(if ($why -match 'Try again') { "retry" } else { "" })
        return
    }
    $found = @(Lines $r.avatar_prefabs)
    if ($found.Count) {
        Tell ($(if ($found.Count -eq 1) { "Your avatar is in. It's picked on the left and being checked now." }
                else { "It has " + $found.Count + " avatars. The first one is picked on the left and being checked now." })) "Package imported"
        Refresh-Then-Select $found[0]
    } else {
        Tell ("The package was imported, but there is no ready-to-wear avatar in it.`n`nBooth avatars often come as several packages " +
              "(the avatar, outfits, shaders). Look for the main one, usually named after the avatar, and add that. " +
              "If this package only has a model, add its .fbx file instead.") "No avatar in this package"
        Refresh-Then-Select $null
    }
}

function Start-Import([string] $file) {
    $script:ctx = @{ File = $file }
    Run-Frosty @("import", $file) "Importing the package..." { param($code) After-Import $code }
}

function After-Vroid($code) {
    $r = Result "vroid"
    if (-not $r) { Step-Failed "The VRoid file wasn't brought in."; return }
    switch ($r.mode) {
        "terms" {
            $terms = ((Lines $r.vrm_terms) + "" + (Lines $r.vrm_warnings)) -join "`n"
            if (Ask ($terms + "`n`nSetting it up for VRChat changes it. Are you its author, or do you have the creator's permission?") "Check the creator's terms" "Yes, I may use it" "Cancel" $true) {
                Run-Frosty @("vroid", $script:ctx.File, "-AcceptTerms") "Bringing in the VRoid file..." { param($code) After-Vroid $code }
            }
            return
        }
        "set_up" { }
        "needs_univrm" {
            Tell ("VRoid files need UniVRM, a free add-on, in the project.`n`n1. Download the UniVRM .unitypackage from https://github.com/vrm-c/UniVRM/releases`n" +
                  "2. Add it here like an avatar package (say Import it when it asks about scripts).`n3. Add your VRoid file again.") "One more thing for VRoid files"
            return
        }
        { $_ -in "vrm_1_0", "not_read" } {
            Tell "This VRoid file couldn't be read. It is probably a VRM 1.0 file: in VRoid Studio, export it again as VRM 0.0, then add the new file." "Export it as VRM 0.0"
            return
        }
        default { Show-Notice ("The VRoid file wasn't brought in: " + $r.message) "retry"; return }
    }
    $warnings = @(Lines $r.vrm_warnings)
    if ($warnings.Count) { Tell (((Lines $r.vrm_terms) -join "`n") + "`n`n" + ($warnings -join "`n")) "The creator's terms" }
    Refresh-Then-Select $r.avatar_prefab
}

function Start-Vroid([string] $file) {
    $script:ctx = @{ File = $file }
    Run-Frosty @("vroid", $file) "Bringing in the VRoid file..." { param($code) After-Vroid $code }
}

function Start-Model([string] $file) {
    $name = [IO.Path]::GetFileNameWithoutExtension($file)
    $text = "A copy of '" + [IO.Path]::GetFileName($file) + "' goes into the project (with its Textures folder, if it has one) and is set up as " +
            "a VRChat avatar: rig, materials, eye position and, where it can, lip sync.`n`nYour own file is not changed."
    if (-not (Ask $text "Set up this model?" "Set it up" "Cancel")) { return }
    $script:ctx = @{ File = $file; Name = $name }
    Run-Frosty @("model", $file) "Setting up your model..." {
        param($code)
        $p = Fresh-Json (Join-Path $Logs ("pipeline_" + $script:ctx.Name + ".json"))
        if (-not $p) { Step-Failed "The model wasn't set up."; return }
        $status = switch ($p.status) { "pass" { "It's set up and passes VRChat's checks." } "warn" { "It's set up. The check has a few notes for you on the right." }
                                       default { "It's set up, but the check found something to fix; the list on the right says what." } }
        Tell ($status + $(if ($p.summary -match 'lip sync: ([^\n]+)') { "`n`nLip sync: " + $Matches[1] } else { "" })) "Model set up"
        Refresh-Then-Select $p.avatar_prefab
    }
}

# Any avatar file, from the button or dropped on the window.
function Add-AvatarFile([string] $file) {
    if ($script:job) { Tell "The studio is still working on the last step. Please wait for it to finish, then add the file again." "One moment"; return }
    if ($script:doctor -and $script:doctor.needed) { Tell "First install what the Your PC card lists. Then click Check again, and add your avatar." "Set up your PC first"; return }
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { Tell "That file isn't there any more. It may have been moved or renamed: add it again from where it is now." "File not found"; return }
    if ((Get-Item -LiteralPath $file).Length -eq 0) { Tell "This file is empty (0 bytes). It probably didn't finish downloading: download it again, then add the new file." "Empty file"; return }
    switch (Kind-Of $file) {
        "package" { Start-Import $file }
        "vrm"     { Start-Vroid $file }
        "model"   { Start-Model $file }
        "zip" {
            $inside = @(); $broken = $false
            try { $inside = @(Avatar-Files-In-Zip $file) } catch { $broken = $true }
            if ($broken) { Tell "This zip file can't be opened. It may be damaged or still downloading: download it again." "Damaged zip file"; return }
            Tell ("Unzip it first: right-click the file, choose Extract All, then add the avatar file from the new folder." +
                  $(if ($inside.Count) { "`n`nAvatar files inside:`n" + (($inside | Select-Object -First 8) -join "`n") }
                    else { "`n`nNo .unitypackage, .vrm or .fbx file is directly inside; it may hold another zip. Unzip it and look in its folders." })) "This is a zip file"
        }
        "blend" { Tell "In Blender, choose File > Export > FBX (.fbx), then add the .fbx file here." "This is a Blender file" }
        default { Tell "Frosty Neon Studio takes a Booth package (.unitypackage), a VRoid file (.vrm) or a model (.fbx)." "Not an avatar file" }
    }
}

# What kind of avatar file this is, from its name: package, vrm, model, zip, blend or other.
function Kind-Of([string] $file) {
    switch ([IO.Path]::GetExtension($file).ToLowerInvariant()) {
        ".unitypackage" { return "package" }
        ".vrm"          { return "vrm" }
        ".fbx"          { return "model" }
        ".zip"          { return "zip" }
        ".blend"        { return "blend" }
        default         { return "other" }
    }
}

# The avatar files inside a zip (Booth downloads are often zipped), without unpacking it.
function Avatar-Files-In-Zip([string] $file) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($file)
    try { return @($zip.Entries | Where-Object { (Kind-Of $_.FullName) -in "package", "vrm", "model" } | ForEach-Object { $_.FullName }) }
    finally { $zip.Dispose() }
}

$ui.AddButton.Add_Click({
    $file = Pick-File "Avatar files (*.unitypackage, *.vrm, *.fbx, *.zip)|*.unitypackage;*.vrm;*.fbx;*.zip|All files (*.*)|*.*"
    if ($file) { Add-AvatarFile $file }
})

# Drag and drop: preview events, so the activity log's text box does not swallow the drop.
# While a file hovers over the window: a full-window highlight saying what dropping it will do.
function Drop-Hint([string] $file) {
    if ($script:job) { return "The studio is busy. Wait for the current step to finish, then drop it again." }
    if ($script:doctor -and $script:doctor.needed) { return "Set up your PC first: the Your PC card says what to install." }
    switch (Kind-Of $file) {
        "package" { return "Booth package: it is imported, then checked." }
        "vrm"     { return "VRoid file: you see the creator's terms first." }
        "model"   { return "Model (.fbx): a copy is set up as your avatar. Your file stays as it is." }
        "zip"     { return "Zip file: the studio tells you which avatar files are inside." }
        "blend"   { return "Blender file: export it as .fbx first, then drop that." }
        default   { return "Not an avatar file. The studio takes .unitypackage, .vrm and .fbx files." }
    }
}

function Show-DropOverlay($data) {
    $files = @($data.GetData([Windows.DataFormats]::FileDrop))
    if (-not $files.Count) { return }
    $kind = Kind-Of ([string]$files[0])
    $ready = -not $script:job -and -not ($script:doctor -and $script:doctor.needed)
    $ok = $ready -and $kind -in "package", "vrm", "model", "zip"
    $ui.DropText.Text = $(if ($ok) { "Drop to add " + [IO.Path]::GetFileName([string]$files[0]) } elseif ($ready) { "Can't add this one" } else { "Not right now" })
    $ui.DropHint.Text = Drop-Hint ([string]$files[0])
    $ui.DropOverlay.BorderBrush = Brush $(if ($ok) { $Colours.Cyan } else { $Colours.Fail })
    $ui.DropOverlay.Visibility = "Visible"
}

$window.Add_PreviewDragEnter({
    if ($_.Data.GetDataPresent([Windows.DataFormats]::FileDrop)) { Show-DropOverlay $_.Data }
})
$window.Add_PreviewDragOver({
    $has = $_.Data.GetDataPresent([Windows.DataFormats]::FileDrop)
    $_.Effects = $(if ($has) { "Copy" } else { "None" })
    if ($has -and $ui.DropOverlay.Visibility -ne "Visible") { Show-DropOverlay $_.Data }
    $_.Handled = $true
})
$window.Add_PreviewDragLeave({
    # Leave also fires when moving between parts of the window; only hide when the pointer left the window.
    $p = $_.GetPosition($window)
    if ($p.X -le 0 -or $p.Y -le 0 -or $p.X -ge $window.ActualWidth - 1 -or $p.Y -ge $window.ActualHeight - 1) {
        $ui.DropOverlay.Visibility = "Collapsed"
    }
})
$window.Add_PreviewDrop({
    $_.Handled = $true
    $ui.DropOverlay.Visibility = "Collapsed"
    $files = @($_.Data.GetData([Windows.DataFormats]::FileDrop))
    if ($files.Count -gt 1) { Log "  Several files were dropped; using the first one: $($files[0])" }
    if ($files.Count) {
        # Handle it after the drop has finished: a question box inside the drop would hold up Explorer until answered.
        $script:dropped = [string]$files[0]
        [void]$window.Dispatcher.BeginInvoke([Action]{ Add-AvatarFile $script:dropped }, [Windows.Threading.DispatcherPriority]::Background)
    }
})

function After-Change([string] $name, [string] $what) {
    if (-not (Result $name)) { Step-Failed $what; return }
    Start-Inspect $script:ctx.Prefab
}

$ui.FixButton.Add_Click({
    $r = $script:current
    $text = "These import settings change:`n" + ((Lines $r.fix_plan | ForEach-Object { "$Dot $_" }) -join "`n") +
            "`n`nThe avatar's own files are not edited, and Undo the fix puts everything back."
    if (-not (Ask $text "Fix it for me?" "Fix it" "Cancel")) { return }
    $script:ctx = @{ Prefab = $r.prefab }
    Run-Frosty @("fix", $r.prefab) "Fixing..." { param($code) After-Change "fix" "The fix didn't finish." }
})

$ui.UndoFixButton.Add_Click({
    $r = $script:current
    if (-not (Ask "This puts back the import settings that Fix it for me changed on this avatar." "Undo the fix?" "Undo the fix" "Cancel")) { return }
    $script:ctx = @{ Prefab = $r.prefab }
    Run-Frosty @("fix", $r.prefab, "-Undo") "Undoing the fix..." { param($code) After-Change "fix" "The undo didn't finish." }
})

$ui.QuestButton.Add_Click({
    $r = $script:current
    $text = "The Quest version is a separate copy beside your avatar, with Quest materials and smaller textures. Your PC avatar and its look " +
            "are not changed, and both versions go to the same avatar in VRChat."
    if (-not $r.quest_version -and -not (Ask $text "Make a Quest version?" "Make it" "Cancel")) { return }
    $script:ctx = @{ Prefab = $r.prefab }
    Run-Frosty @("quest", $r.prefab) "Making the Quest version..." {
        param($code)
        $q = Result "quest"
        if (-not $q) { Step-Failed "The Quest version wasn't made."; return }
        $extra = @()
        if (@(Lines $q.see_through).Count) { $extra += "$(@(Lines $q.see_through).Count) see-through part(s), like eyelashes or highlights, use Quest's see-through shaders." }
        if (@(Lines $q.cut_out).Count) { $extra += "Cut-out edges become solid on Quest. Click Show Quest look to see it." }
        Tell ((@($q.message) + $extra | Where-Object { $_ }) -join "`n`n") "Quest version"
        Start-Inspect $script:ctx.Prefab
    }
})

$ui.RemoveQuestButton.Add_Click({
    $r = $script:current
    $text = "This deletes the Quest copy and puts back the texture settings it changed. Your PC avatar stays as it is, and an avatar already on VRChat is not affected."
    if (-not (Ask $text "Remove the Quest version?" "Remove it" "Keep it" $true)) { return }
    $script:ctx = @{ Prefab = $r.prefab }
    Run-Frosty @("quest", $r.prefab, "-Remove") "Removing the Quest version..." { param($code) After-Change "quest" "The Quest version wasn't removed." }
})

$ui.QuestLookButton.Add_Click({
    $script:showQuest = -not $script:showQuest
    Show-Pictures
    Update-Buttons
})

$ui.DetailsToggle.Add_Click({
    $open = $ui.DetailsFold.Visibility -ne "Visible"
    $ui.DetailsFold.Visibility = $(if ($open) { "Visible" } else { "Collapsed" })
    $ui.DetailsToggle.Content = $(if ($open) { "Hide details for _helpers" } else { "Show details for _helpers" })
    if ($open) { $ui.DetailsText.BringIntoView() }
})

function Copy-Details {
    try { [Windows.Clipboard]::SetText((Helper-Text)); Flash "Copied: paste it into a message to your helper." }
    catch { Tell "The details couldn't be copied (another program may be using the clipboard). Try again in a moment." "Not copied" }
}
$ui.CopyButton.Add_Click({ Copy-Details })
$ui.MenuCopy.Add_Click({ Copy-Details })

$ui.MenuButton.Add_Click({
    $menu = $ui.MenuButton.ContextMenu
    $menu.PlacementTarget = $ui.MenuButton
    $menu.Placement = "Bottom"
    $menu.IsOpen = $true
})

$ui.MenuLogs.Add_Click({
    $folder = $(if (Test-Path $Logs) { $Logs } else { $Root })
    Start-Process explorer.exe -ArgumentList ('"' + $folder + '"')
})

$ui.MenuDoctor.Add_Click({ Start-Doctor })

$ui.MenuShortcut.Add_Click({
    $desktop = [Environment]::GetFolderPath("Desktop")
    if ((Test-Path (Join-Path $desktop "Frosty Neon Studio.lnk")) -and
        -not (Ask "There is already a Frosty Neon Studio shortcut on your desktop. Replace it with a new one?" "Replace the shortcut?" "Replace it" "Cancel")) { return }
    try { [void](New-Shortcut $desktop); Tell "Frosty Neon Studio is on your desktop now. Double-click it to start the studio." "Shortcut created" }
    catch { Tell ("The shortcut couldn't be created: " + $_.Exception.Message) "No shortcut" }
})

$ui.NoticeClose.Add_Click({ Hide-Notice })
$ui.NoticeButton.Add_Click({
    $action = $script:noticeAction
    Hide-Notice
    if ($action -eq "doctor") { Start-Doctor }
    elseif ($action -eq "recheck" -and $script:ctx.Prefab) { Start-Inspect $script:ctx.Prefab }
    elseif ($action -eq "retry" -and $script:retry) { $script:ctx = $script:retry.Ctx; Run-Frosty $script:retry.Args $script:retry.Label $script:retry.Done }
})

function Start-Upload([bool] $quest) {
    if ($Yes) { Log "  [test] Uploads are not started in -Yes mode."; return }
    $r = $script:current
    if (Test-UnityOpen) { Show-Notice $UnityOpenText ""; return }
    $platform = $(if ($quest) { "Quest" } else { "PC" })
    $text = "Upload '" + $r.name + "' for " + $platform + " as a private avatar?`n`nUnity opens by itself and signs in to VRChat with the account " +
            "saved in its VRChat SDK window (if it asks you to sign in, do that first). Before anything goes up, it asks you once more: " +
            "click Upload (or Update) there.`n`nA private avatar is only for you; nobody else can wear it."
    if ($r.only_author_may_use) { $text += "`n`nIts creator's terms say only the author may use it as an avatar; Unity asks you to confirm that is you." }
    if (-not (Ask $text ("Upload for " + $platform + "?") "Upload" "Cancel")) { return }
    $script:ctx = @{ Prefab = $r.prefab }
    $arguments = @("upload", $r.prefab)
    if ($quest) { $arguments += "-Quest" }
    Run-Frosty $arguments "Uploading: confirm in the Unity window..." {
        param($code)
        $u = Fresh-Json (Join-Path $Logs "upload_result.json")
        if ($code -eq 0 -and $u -and $u.uploaded) {
            Tell ($u.message + "`n`nIn VRChat: open your menu, go to Avatars > Uploaded, and put it on. Have fun out there!`n`nUnity stays open; close it when you're finished.") "It's on VRChat"
        } elseif ($u) { Tell ("Nothing was uploaded. " + $u.message) "Not uploaded" }
        else { Step-Failed "Nothing was uploaded." }
        if (Test-UnityOpen) { Show-Notice "Unity is still open from the upload. Close it when you're done, then click Try again to refresh this avatar's check." "recheck"; return }
        Start-Inspect $script:ctx.Prefab
    }
}

$ui.UploadPcButton.Add_Click({ Start-Upload $false })
$ui.UploadQuestButton.Add_Click({ Start-Upload $true })

$window.Add_Closing({
    $e = $_
    if ($script:job -and -not $Quiet -and -not $Screenshot) {
        if (-not (Ask (Close-Warning $script:job.Label) "Still working" "Close anyway" "Keep it open" $true)) { $e.Cancel = $true; return }
    }
    $timer.Stop()
    if (-not $SelfTest -and -not $Screenshot) { Save-State @{ window = Window-Placement } }
})

[void](Set-AppIcon $IconPath)

# ------------------------------------------------------------------ self-test (no Unity)
if ($SelfTest) {
    $failed = 0
    $checks = 0
    function Check([string] $name, [bool] $ok) {
        $script:checks++
        if ($ok) { Write-Host "  [ok] $name" } else { Write-Host "  [FAIL] $name"; $script:failed++ }
    }
    function Luminance([string] $hex) {
        $c = [Windows.Media.ColorConverter]::ConvertFromString($hex)
        $ch = @($c.R, $c.G, $c.B | ForEach-Object { $v = $_ / 255; if ($v -le 0.03928) { $v / 12.92 } else { [math]::Pow(($v + 0.055) / 1.055, 2.4) } })
        return 0.2126 * $ch[0] + 0.7152 * $ch[1] + 0.0722 * $ch[2]
    }
    function Contrast([string] $fg, [string] $bg) {
        $a = Luminance $fg; $b = Luminance $bg
        return ([math]::Max($a, $b) + 0.05) / ([math]::Min($a, $b) + 0.05)
    }
    function Shown-Text($block) { return (New-Object Windows.Documents.TextRange($block.ContentStart, $block.ContentEnd)).Text }
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ("frosty_studio_test_" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tmp -Force | Out-Null
    $StatePath = Join-Path $tmp "studio.json"    # never the real one

    Check "every named part of the window exists" (@($names | Where-Object { -not $ui[$_] }).Count -eq 0)
    $bytes = [IO.File]::ReadAllBytes((Join-Path $AppDir "FrostyNeonStudio.ps1"))
    Check "this script is plain ASCII (no bytes over 126, no control characters but tab, CR, LF)" (@($bytes | Where-Object { $_ -gt 126 -or ($_ -lt 32 -and $_ -notin 9, 10, 13) }).Count -eq 0)

    # Colour contrast of text (WCAG AA: 4.5:1), including the XAML's own colours.
    $pairs = @(@("#E8ECFF", "#181B35"), @("#A8B0E0", "#181B35"), @("#A8B0E0", "#11142A"), @("#A8B0E0", "#090B16"), @("#B49CFF", "#181B35"),
               @("#3EF2FF", "#181B35"), @("#FF4FD8", "#2A0F4A"), @("#3EF2FF", "#2A0F4A"), @("#3EF2FF", "#0F2A4A"), @("#0D0F1E", "#FF4FD8"), @("#0D0F1E", "#3EF2FF"),
               @("#E8ECFF", "#2A2F5A"), @("#E8ECFF", "#2E3470"), @("#FFD36E", "#3A2A10"), @("#FFE3EA", "#3A1428"), @($Colours.Good, $Colours.Card),
               @($Colours.Warn, $Colours.Card), @($Colours.Bad, $Colours.Card), @($Colours.Ink, $Colours.Pass), @($Colours.Ink, $Colours.Caution), @($Colours.Ink, $Colours.Fail))
    $low = @($pairs | Where-Object { (Contrast $_[0] $_[1]) -lt 4.5 } | ForEach-Object { $_[0] + " on " + $_[1] + " = " + [math]::Round((Contrast $_[0] $_[1]), 2) })
    Check ("text colours reach WCAG AA contrast (4.5:1)" + $(if ($low.Count) { ": " + ($low -join "; ") } else { "" })) ($low.Count -eq 0)

    # Setup card: all fine, then something missing.
    $script:doctor = [pscustomobject]@{ needed = 0; advised = 1; rows = @(
        [pscustomobject]@{ state = "OK"; what = "Unity 2022.3.22f1"; detail = "x"; todo = "" },
        [pscustomobject]@{ state = "OPTIONAL"; what = "Quest uploads (Android Build Support)"; detail = "x"; todo = "add it" },
        [pscustomobject]@{ state = "INFO"; what = "Unity Editor"; detail = "closed"; todo = "" }) }
    Show-Setup $script:doctor
    Check "setup card: ready line, what is optional, one line for what is fine" ((Shown-Text $ui.SetupText) -match "Ready for PC avatars" -and (Shown-Text $ui.SetupText) -match "Unity 2022" -and
        (Shown-Text $ui.SetupText) -match "!  Quest uploads" -and $ui.SetupAgainButton.Visibility -eq "Collapsed")
    $missingDoctor = [pscustomobject]@{ needed = 1; advised = 0; rows = @(
        [pscustomobject]@{ state = "MISSING"; what = "VRChat Creator Companion"; detail = ""; todo = "Install it from https://vrchat.com/home/download (Creator Companion). It also installs Unity for you." },
        [pscustomobject]@{ state = "INFO"; what = "Unity Editor"; detail = "has this project open (close it ...)"; todo = "" }) }
    $script:doctor = $missingDoctor
    Show-Setup $missingDoctor
    $links = @($ui.SetupText.Inlines | Where-Object { $_ -is [Windows.Documents.Hyperlink] })
    Check "setup card when something is missing: what to install, a clickable link, Check again, and that Unity is open" ((Shown-Text $ui.SetupText) -match "Install these first" -and
        (Shown-Text $ui.SetupText) -match "Creator Companion" -and $links.Count -eq 1 -and $links[0].NavigateUri.AbsoluteUri -eq "https://vrchat.com/home/download" -and
        $ui.SetupAgainButton.Visibility -eq "Visible" -and (Shown-Text $ui.SetupText) -match "Unity has this project open")
    Show-Empty
    Check "with setup missing: adding is off and says why; the right side says to set up first" (-not $ui.AddButton.IsEnabled -and [string]$ui.AddButton.ToolTip -match "Your PC card" -and
        $ui.EmptyTitle.Text -match "set up your PC" -and $ui.ListEmpty.Text -match "once your PC is set up")
    $script:said = @()
    Add-AvatarFile "C:\x\My Avatar.unitypackage"
    Check "with setup missing: a dropped avatar gets 'set up your PC first'" ($script:said.Count -eq 1 -and $script:said[0] -match "Set up your PC first")
    Show-Setup $null
    Check "setup card when the PC check itself failed: says so and offers Check again" ((Shown-Text $ui.SetupText) -match "couldn't check this PC" -and $ui.SetupAgainButton.Visibility -eq "Visible")
    $script:doctor = [pscustomobject]@{ needed = 0; advised = 0; rows = @([pscustomobject]@{ state = "OK"; what = "Quest uploads (Android Build Support)"; detail = "x"; todo = "" }) }
    Show-Setup $script:doctor

    $script:doctor = [pscustomobject]@{ needed = 0; advised = 0; rows = @() }
    Show-Empty
    Check "before the avatar list has loaded (Unity busy, for example), the right side doesn't say 'pick one on the left'" ($ui.EmptyTitle.Text -eq "Your avatars will show up here" -and
        $ui.ListEmpty.Visibility -eq "Collapsed")
    $script:doctor = $null

    # Empty project.
    Show-List ([pscustomobject]@{ avatars = @() })
    Check "empty project: the list says how to add one, the right side says 'Add your first avatar'" ($ui.ListEmpty.Visibility -eq "Visible" -and
        $ui.ListEmpty.Text -match "No avatars yet" -and $ui.EmptyTitle.Text -eq "Add your first avatar" -and $ui.AddButton.IsEnabled)
    $longName = "Super Long Avatar Name From Booth With Every Outfit Included Deluxe Edition Version 2.3 (Quest-ready)"
    Show-List ([pscustomobject]@{ avatars = @([pscustomobject]@{ name = "Bot"; kind = "ready-made"; prefab = "Assets/Bot_Avatar.prefab"; quest_version = $null },
                                              [pscustomobject]@{ name = $longName; kind = "Booth package"; prefab = "Assets/Long/Long_Avatar.prefab"; quest_version = "x" }) })
    Check "avatar list is filled, with the prefab as the item's tag; the empty note is gone" ($ui.AvatarList.Items.Count -eq 2 -and $ui.AvatarList.Items[0].Tag -eq "Assets/Bot_Avatar.prefab" -and
        $ui.ListEmpty.Visibility -eq "Collapsed" -and $ui.EmptyTitle.Text -match "Pick an avatar")
    $longItem = $ui.AvatarList.Items[1]
    Check "a very long avatar name is cut short with '...' in the list, whole in its tooltip and for screen readers" ($longItem.Content.Children[0].TextTrimming -eq "CharacterEllipsis" -and
        $longItem.ToolTip -eq $longName -and [Windows.Automation.AutomationProperties]::GetName($longItem) -match "has a Quest version")
    Check "before an avatar is picked, the 'drop it here' panel shows instead of empty details" ($ui.EmptyPanel.Visibility -eq "Visible" -and $ui.DetailScroll.Visibility -eq "Collapsed")
    Check "before an avatar is picked, its buttons are off and say 'Pick an avatar first'" (-not $ui.FixButton.IsEnabled -and [string]$ui.FixButton.ToolTip -eq "Pick an avatar first." -and
        [string]$ui.UploadPcButton.ToolTip -eq "Pick an avatar first.")

    $sample = [pscustomobject]@{
        prefab = "Assets/Bot_Avatar.prefab"; name = "Bot"; headline = "The PC version is ready! Quest needs a bit more."
        pc_status = "pass"; quest_status = "fail"; fix_plan = @("Read/Write on Bot.fbx: off -> on"); fix_can_undo = $false
        quest_version = $null; quest_version_needed = $true; height_m = 1.7; eyes_m = 1.6; floor_notes = @()
        groups = @([pscustomobject]@{ group = "Fixed for you by the Fix step"; items = @([pscustomobject]@{ code = "mesh_read_write_disabled"; title = "Meshes locked against reading"; meaning = "A setting stops VRChat's tools from reading the meshes."; platform = $null; objects = @("Bot.fbx") }) })
        only_author_may_use = $true; vrm_terms = @('"Bot" by Maker (VRM 0.x)'); pictures = @(); quest_pictures = @()
    }
    Show-Avatar $sample
    Check "after picking, the details show, with technical codes in the helpers' fold (closed)" ($ui.EmptyPanel.Visibility -eq "Collapsed" -and $ui.DetailScroll.Visibility -eq "Visible" -and
        $ui.DetailsText.Text -match "\[mesh_read_write_disabled\] on: Bot\.fbx" -and $ui.DetailsText.Text -match "inspect_result\.json" -and $ui.DetailsFold.Visibility -eq "Collapsed")
    $ui.DetailsToggle.RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent)))
    $opened = $ui.DetailsFold.Visibility -eq "Visible" -and $ui.DetailsToggle.Content -match "Hide"
    $ui.DetailsToggle.RaiseEvent((New-Object Windows.RoutedEventArgs([Windows.Controls.Button]::ClickEvent)))
    Check "Show details for helpers opens and closes the fold" ($opened -and $ui.DetailsFold.Visibility -eq "Collapsed")
    Check "details for a helper hide the toolkit folder and the Windows user name" (-not (Helper-Text).Contains($Root) -and
        (-not $env:USERPROFILE -or -not (Helper-Text).Contains($env:USERPROFILE)) -and (Helper-Text) -match "--- activity")
    Check "long steps show the time and a note" ((Busy-Label "Checking..." (New-TimeSpan -Seconds 42)) -eq "Checking...  0:42`nUnity is working; this can take a minute." -and
        (Busy-Label "Checking..." (New-TimeSpan -Seconds 5)) -eq "Checking...  0:05")
    $drag = New-Object Windows.DataObject([Windows.DataFormats]::FileDrop, [string[]]@("C:\Downloads\My Avatar.unitypackage"))
    Show-DropOverlay $drag
    $goodDrop = $ui.DropOverlay.Visibility -eq "Visible" -and $ui.DropText.Text -eq "Drop to add My Avatar.unitypackage" -and $ui.DropHint.Text -match "Booth package"
    Show-DropOverlay (New-Object Windows.DataObject([Windows.DataFormats]::FileDrop, [string[]]@("C:\x\photo.png")))
    Check "dragging a file over the window says what dropping it will do (and refuses other files)" ($goodDrop -and $ui.DropText.Text -eq "Can't add this one" -and $ui.DropHint.Text -match "Not an avatar file")
    $ui.DropOverlay.Visibility = "Collapsed"
    Check "avatar details: name and two status pills, named for screen readers" ($ui.AvatarName.Text -eq "Bot" -and $ui.Pills.Children.Count -eq 2 -and
        [Windows.Automation.AutomationProperties]::GetName($ui.Pills.Children[0]) -eq "PC  READY")
    Check "avatar details: findings in plain words, the Fix plan and the Quest hint" ($ui.Findings.Text -match "Meshes locked" -and
        $ui.Findings.Text -match "WHAT FIX IT FOR ME WOULD CHANGE" -and $ui.Findings.Text -match "need a Quest version")
    Check "the creator's terms are shown when only the author may use it" ($ui.TermsBox.Visibility -eq "Visible" -and $ui.TermsText.Text -match "only the author")
    Check "buttons follow the report: Fix, Make Quest version, Upload PC on; Upload Quest, Undo, Remove off" ($ui.FixButton.IsEnabled -and $ui.QuestButton.IsEnabled -and
        $ui.UploadPcButton.IsEnabled -and -not $ui.UploadQuestButton.IsEnabled -and -not $ui.UndoFixButton.IsEnabled -and -not $ui.RemoveQuestButton.IsEnabled)
    Check "off buttons say why in their tooltip; on buttons say what they do" ([string]$ui.UploadQuestButton.ToolTip -match "make a Quest version first" -and
        [string]$ui.UndoFixButton.ToolTip -match "Nothing to undo" -and [string]$ui.RemoveQuestButton.ToolTip -match "no Quest version" -and
        [string]$ui.QuestLookButton.ToolTip -match "Make a Quest version first" -and [string]$ui.FixButton.ToolTip -match "you can undo it" -and
        @($ui.FixButton, $ui.UploadQuestButton | Where-Object { -not [Windows.Controls.ToolTipService]::GetShowOnDisabled($_) }).Count -eq 0)
    $script:job = @{ Label = "x" }
    Update-Buttons
    $busyOff = -not $ui.FixButton.IsEnabled -and [string]$ui.FixButton.ToolTip -match "Wait for the current step" -and -not $ui.AddButton.IsEnabled
    $script:job = $null
    Update-Buttons
    Check "while busy, buttons are off and say 'wait for the current step'" $busyOff
    $script:doctor = [pscustomobject]@{ needed = 0; advised = 1; rows = @([pscustomobject]@{ state = "OPTIONAL"; what = "Quest uploads (Android Build Support)"; detail = "not installed"; todo = "x" }) }
    $passing = $sample.PSObject.Copy(); $passing.quest_status = "pass"; $passing.quest_version = "Assets/q.prefab"
    Show-Avatar $passing
    Check "Upload for Quest is off, and says why, while Android Build Support is missing" (-not $ui.UploadQuestButton.IsEnabled -and [string]$ui.UploadQuestButton.ToolTip -match "Android Build Support" -and
        $ui.QuestButton.Content -match "again" -and $ui.RemoveQuestButton.IsEnabled)
    $script:doctor = $null
    Show-Avatar $sample
    Check "facts line says the avatar stands right" ($ui.Facts.Text -match "1.7 m tall" -and $ui.Facts.Text -match "Looking good")
    Check "access keys on the main buttons" ((@($ui.AddButton, $ui.RefreshButton, $ui.FixButton, $ui.UndoFixButton, $ui.QuestButton, $ui.QuestLookButton, $ui.RemoveQuestButton,
        $ui.UploadPcButton, $ui.UploadQuestButton, $ui.CopyButton, $ui.DetailsToggle, $ui.MenuButton) | Where-Object { [string]$_.Content -notmatch '_' }).Count -eq 0)
    Check "screen-reader names on the list, pictures and logs" ((@($ui.AvatarList, $ui.Pic1, $ui.Pic2, $ui.Pic3, $ui.Log, $ui.DetailsText, $ui.Busy, $ui.MenuButton) |
        Where-Object { -not [Windows.Automation.AutomationProperties]::GetName($_) }).Count -eq 0)

    # The avatar check failing.
    $script:lastError = $null
    Step-Failed "The avatar check didn't finish."
    Show-Avatar $null
    Check "a step that writes no report: a notice with what happened and Try again; the avatar side says it couldn't be checked" ($ui.NoticeBox.Visibility -eq "Visible" -and
        $ui.NoticeText.Text -match "^The avatar check didn't finish. Unity stopped before it finished" -and $ui.NoticeButton.Visibility -eq "Visible" -and
        $ui.NoticeButton.Content -eq "_Try again" -and $ui.Headline.Text -match "couldn't be checked" -and -not $ui.UploadPcButton.IsEnabled)
    Hide-Notice
    $e1 = Explain-Error "The Unity Editor has this project open. Close the Unity window, then run this again."
    $e2 = Explain-Error "Unity 2022.3.22f1 (the version this project uses) was not found at C:\x. Install it through Unity Hub or VRChat Creator Companion."
    $e3 = Explain-Error "File not found: C:\x\a.unitypackage"
    $e4 = Explain-Error "Something odd happened."
    $e5 = Explain-Error "Unity could not switch to Android (it is on StandaloneWindows64). For Quest, ..."
    Check "frosty.ps1 errors in plain words: Unity open, Unity missing, file gone, Quest module, anything else" ($e1.Text -match "Close Unity" -and $e1.Action -eq "retry" -and
        $e2.Text -match "isn't installed" -and $e2.Action -eq "doctor" -and $e3.Text -match "isn't there any more" -and -not $e3.Action -and
        $e4.Text -match "Something odd happened. If it keeps happening" -and $e5.Action -eq "doctor")
    $projectDir = Join-Path $tmp "project"
    New-Item -ItemType Directory -Force -Path (Join-Path $projectDir "Temp") | Out-Null
    $lockFile = Join-Path $projectDir "Temp\UnityLockfile"
    Set-Content $lockFile "x"
    $closedLock = Test-UnityOpen $projectDir
    $held = [IO.File]::Open($lockFile, "Open", "ReadWrite", "None")
    $openLock = Test-UnityOpen $projectDir
    $held.Close()
    Check "Unity having the project open is noticed (its lock file is held), and a left-over lock file is not" ((-not $closedLock) -and $openLock -and -not (Test-UnityOpen (Join-Path $tmp "none")))
    $old = Join-Path $tmp "old_result.json"
    Set-Content $old '{"name":"old"}'
    (Get-Item $old).LastWriteTime = (Get-Date).AddMinutes(-10)
    $script:lastStarted = Get-Date
    $stale = Fresh-Json $old
    Set-Content $old '{"name":"new"}'
    $fresh = Fresh-Json $old
    Set-Content $old '{"name":'
    $broken = Fresh-Json $old
    $script:lastStarted = $null
    Check "reports left over from an earlier run, or half written, are not shown as new results" ($null -eq $stale -and $fresh.name -eq "new" -and $null -eq $broken)
    Check "closing while busy warns and says what happens to the step" ((Close-Warning "Checking your avatar...") -match "still checking your avatar\." -and
        (Close-Warning "Checking your avatar...") -match "carries on hidden" -and (Close-Warning "Uploading: confirm in the Unity window...") -match "doesn't stop it")

    # Starting frosty.ps1: quoting, UTF-8 output and the exit code, with a stand-in script.
    $refused = $false; try { [void](Quote-Argument 'a"b') } catch { $refused = $true }
    Check "arguments: spaces quoted, trailing backslashes doubled, double quotes refused" ((Quote-Argument "C:\a b\") -eq '"C:\a b\\"' -and (Quote-Argument "x") -eq "x" -and
        (Quote-Argument "") -eq '""' -and $refused)
    $realFrosty = $Frosty
    $Frosty = Join-Path $tmp "stand in's frosty.ps1"
    Set-Content $Frosty 'param([string] $T) Write-Host ("got [" + $T + "]"); Write-Host "ERROR: stopped"; exit 3' -Encoding ASCII
    $odd = "C:\Avatars " + [char]0x30A2 + "'s `$x\"
    $outFile = Join-Path $tmp "out.txt"
    $p = Start-Process powershell.exe -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", (Quote-Argument (Frosty-Command @($odd)))) `
                       -WindowStyle Hidden -PassThru -Wait -RedirectStandardOutput $outFile
    $pump = @{ Read = 0 }; $script:lastError = $null
    Pump-Output $pump $outFile $false
    $Frosty = $realFrosty
    Check "frosty.ps1 gets odd file names whole, its output reaches the log in UTF-8, and its exit code and ERROR line come back" ($p.ExitCode -eq 3 -and
        $ui.Log.Text.Contains("got [" + $odd + "]") -and $script:lastError -eq "stopped")
    $script:lastError = $null

    # Files that are not what they seem.
    $script:said = @()
    $emptyFile = Join-Path $tmp "Empty.unitypackage"; [IO.File]::WriteAllBytes($emptyFile, [byte[]]@())
    Add-AvatarFile $emptyFile
    Add-AvatarFile (Join-Path $tmp "gone.vrm")
    $badZip = Join-Path $tmp "broken.zip"; Set-Content $badZip "this is not a zip"
    Add-AvatarFile $badZip
    $photo = Join-Path $tmp "photo.png"; Set-Content $photo "x"
    Add-AvatarFile $photo
    Check "empty, missing, damaged and wrong files each get a clear message" ($script:said.Count -eq 4 -and $script:said[0] -match "didn't finish downloading" -and
        $script:said[1] -match "isn't there any more" -and $script:said[2] -match "Damaged zip" -and $script:said[3] -match "Not an avatar file")
    Check "file kinds: package, VRoid, model, zip, Blender, other" ((Kind-Of "C:\a b\My Avatar.unitypackage") -eq "package" -and (Kind-Of "x.VRM") -eq "vrm" -and
        (Kind-Of "x.fbx") -eq "model" -and (Kind-Of "x.zip") -eq "zip" -and (Kind-Of "x.blend") -eq "blend" -and (Kind-Of "x.png") -eq "other")
    New-Item -ItemType Directory -Path (Join-Path $tmp "in\Avatar") -Force | Out-Null
    Set-Content (Join-Path $tmp "in\Avatar\My Avatar.unitypackage") "x"
    Set-Content (Join-Path $tmp "in\readme.txt") "x"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $tmp "in"), (Join-Path $tmp "booth.zip"))
    $inside = @(Avatar-Files-In-Zip (Join-Path $tmp "booth.zip"))
    Check "a zip's avatar files are named without unpacking it" ($inside.Count -eq 1 -and $inside[0] -match "My Avatar\.unitypackage$")

    # Import results: an avatar, no avatar, a damaged package.
    $script:said = @()
    $script:job = @{ Label = "x" }    # so the follow-up list step is not started
    New-Item -ItemType Directory -Force -Path $Logs | Out-Null
    $importReport = Join-Path $Logs "package_import.json"
    $hadReport = Test-Path $importReport
    if ($hadReport) { Copy-Item $importReport (Join-Path $tmp "package_import.json.keep") }
    Set-Content $importReport '{"mode":"imported","avatar_prefabs":[],"message":"Imported, but no ready avatar prefab"}'
    After-Import 0
    Set-Content $importReport '{"mode":"failed","message":"The package could not be read or imported: invalid gzip header"}'
    After-Import 1
    if ($hadReport) { Copy-Item (Join-Path $tmp "package_import.json.keep") $importReport -Force } else { Remove-Item $importReport }
    $script:job = $null
    Check "a package with no avatar in it, and a damaged package, are explained with what to do" ($script:said.Count -ge 1 -and $script:said[0] -match "no ready-to-wear avatar" -and
        $ui.NoticeText.Text -match "damaged or only partly downloaded")
    Hide-Notice

    # Dialogs: Enter and Escape.
    $dialog = New-Dialog "Text" "Heading?" "Do it" "Cancel" $false
    $safe = New-Dialog "Text" "Close?" "Close anyway" "Keep it open" $true
    $message = New-Dialog "Text" "Done" "OK" $null $false
    Check "dialogs: Enter means the main button (or the safe one for risky questions), Escape means Cancel" ($dialog.FindName("YesButton").IsDefault -and $dialog.FindName("NoButton").IsCancel -and
        $safe.FindName("NoButton").IsDefault -and -not $safe.FindName("YesButton").IsDefault -and $message.FindName("YesButton").IsCancel -and
        $message.FindName("NoButton").Visibility -eq "Collapsed")

    # Remembered things: last avatar, window place (kept on screen), shortcut, icon.
    Remember "Assets/X/Bot_Avatar.prefab"
    Save-State @{ window = [ordered]@{ left = 100; top = 80; width = 1000; height = 700; maximized = $false } }
    $state = Load-State
    Check "the last avatar and the window's place are remembered for next time" ((Recall) -eq "Assets/X/Bot_Avatar.prefab" -and $state.window.width -eq 1000)
    $screens = New-Object Windows.Rect(0, 0, 1280, 720)
    $work = New-Object Windows.Rect(0, 0, 1280, 680)
    $back = Fit-Placement $state.window $screens $work
    $lost = Fit-Placement ([pscustomobject]@{ left = 3000; top = 200; width = 1600; height = 1000; maximized = $false }) $screens $work
    Check "a remembered place is used when it is on screen, and moved back and shrunk to fit when it is not" ($back.Left -eq 100 -and $back.Width -eq 1000 -and
        $lost.Left -lt 1280 -and $lost.Width -eq 1280 -and $lost.Height -eq 680)
    $link = New-Shortcut $tmp
    $shell = New-Object -ComObject WScript.Shell
    $made = $shell.CreateShortcut($link)
    Check "Create a desktop shortcut makes a working .lnk to the studio" ((Test-Path $link) -and $made.TargetPath -match "powershell\.exe$" -and
        $made.Arguments -match "FrostyNeonStudio\.ps1" -and $made.Arguments -match "-WindowStyle Hidden" -and $made.WorkingDirectory -eq $Root)
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    Check "a missing icon file is skipped quietly" (-not (Set-AppIcon (Join-Path $tmp "none.ico")))

    # Pictures of the window, for a visual check (-Screenshot <file.png>): filled, drop highlight, empty project,
    # setup missing with a notice, the smallest window with a long name, and a dialog.
    $shot = $(if ($Screenshot) { $Screenshot } else { Join-Path $tmp "selftest.png" })
    $base = [IO.Path]::ChangeExtension($shot, $null).TrimEnd(".")
    $window.WindowStartupLocation = "Manual"; $window.Left = 20; $window.Top = 20
    $sample.pictures = @(); Show-Avatar $sample
    $window.Show()
    Save-Screenshot $shot
    if ($Screenshot) {
        Show-DropOverlay $drag; Save-Screenshot ($base + "_drop.png"); $ui.DropOverlay.Visibility = "Collapsed"
        Show-List ([pscustomobject]@{ avatars = @() }); $script:current = $null; Show-Empty; Save-Screenshot ($base + "_empty.png")
        $script:doctor = $missingDoctor; Show-Setup $missingDoctor; Show-Empty
        Show-Notice $UnityOpenText "retry"; Save-Screenshot ($base + "_setup.png"); Hide-Notice
        $script:doctor = $null; Show-Setup ([pscustomobject]@{ needed = 0; advised = 0; rows = @([pscustomobject]@{ state = "OK"; what = "Unity 2022.3.22f1"; detail = ""; todo = "" }) })
        Show-List ([pscustomobject]@{ avatars = @([pscustomobject]@{ name = $longName; kind = "Booth package"; prefab = "Assets/Long/Long_Avatar.prefab"; quest_version = $null },
                                                  [pscustomobject]@{ name = "Bot"; kind = "ready-made"; prefab = "Assets/Bot_Avatar.prefab"; quest_version = $null }) })
        $long = $sample.PSObject.Copy(); $long.name = $longName; Show-Avatar $long
        $script:quietSelect = $true; $ui.AvatarList.SelectedIndex = 0; $script:quietSelect = $false
        $window.Width = $window.MinWidth; $window.Height = $window.MinHeight
        Save-Screenshot ($base + "_small.png")
        $d = New-Dialog ("Upload '" + $longName + "' for PC as a private avatar?`n`nUnity opens by itself and signs in to VRChat.") "Upload for PC?" "Upload" "Cancel" $false
        $d.Owner = $window; $d.WindowStartupLocation = "Manual"; $d.Left = 60; $d.Top = 60
        $d.Show(); Save-Screenshot ($base + "_dialog.png") $d; $d.Close()
    }
    $window.Close()
    Check "the window renders to a picture" ((Test-Path $shot) -and (Get-Item $shot).Length -gt 10000)
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host ("Frosty Neon Studio self-test: " + $(if ($failed) { "FAILED ($failed of $checks)" } else { "PASS" }) + ", $checks checks")
    exit $(if ($failed) { 1 } else { 0 })
}

# ------------------------------------------------------------------ start
Place-Window
$window.Add_ContentRendered({ $timer.Start(); Log "Frosty Neon Studio: checking this PC and finding your avatars."; Start-Doctor })
[void]$window.ShowDialog()

param(
    [Parameter(Mandatory = $true)][string]$SnapActionsPath,
    [ValidateSet('Brave', 'Chrome', 'Edge')][string]$Browser = 'Brave'
)

$ErrorActionPreference = 'Stop'
$executable = (Resolve-Path -LiteralPath $SnapActionsPath).Path
if ([IO.Path]::GetFileName($executable) -ne 'SnapActions.exe') {
    throw 'Select the SnapActions.exe built with browser selection support.'
}
$hostDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SnapActions\Browser'
[IO.Directory]::CreateDirectory($hostDirectory) | Out-Null
$manifestPath = Join-Path $hostDirectory 'com.snapactions.selection.json'
$manifest = @{
    name = 'com.snapactions.selection'
    description = 'SnapActions browser selection bridge'
    path = $executable
    type = 'stdio'
    allowed_origins = @('chrome-extension://ccgckebadlhbplacbcjbfohpinbdoehh/')
} | ConvertTo-Json
[IO.File]::WriteAllText($manifestPath, $manifest, [Text.UTF8Encoding]::new($false))
$browserKey = @{
    Brave = 'BraveSoftware\Brave-Browser'
    Chrome = 'Google\Chrome'
    Edge = 'Microsoft\Edge'
}[$Browser]
$browserKeys = @($browserKey)
# Brave builds also use Chromium's Chrome registry lookup on Windows.
if ($Browser -eq 'Brave') { $browserKeys += 'Google\Chrome' }
foreach ($registration in $browserKeys) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("Software\$registration\NativeMessagingHosts\com.snapactions.selection")
    try { $key.SetValue('', $manifestPath) } finally { $key.Dispose() }
}
Write-Output "Registered SnapActions for $Browser. Load this browser-extension folder as an unpacked extension."

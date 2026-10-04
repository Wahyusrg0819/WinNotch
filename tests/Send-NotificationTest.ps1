# Run with Windows PowerShell 5.1; sends only this local test toast.
param([switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
[void][Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
[void][Windows.UI.Notifications.ToastNotification, Windows.UI.Notifications, ContentType = WindowsRuntime]
[void][Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]
$sender = Get-StartApps | Where-Object { $_.Name -eq 'Windows PowerShell' } | Select-Object -First 1
if (-not $sender) { throw 'The Windows PowerShell Start menu identity is required for this test.' }
if (-not $CheckOnly) {
    $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
    $xml.LoadXml('<toast><visual><binding template="ToastGeneric"><text>WinNotch live test</text><text>A real Windows notification. Dismissing its notch preview should keep this original in Windows.</text></binding></visual><audio silent="true"/></toast>')
    $toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
    $toast.Tag = 'WinNotchTest'
    $toast.Group = 'WinNotchValidation'
    $toast.ExpirationTime = [DateTimeOffset]::Now.AddMinutes(10)
    [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($sender.AppID).Show($toast)
}
$ours = @([Windows.UI.Notifications.ToastNotificationManager]::History.GetHistory($sender.AppID) | Where-Object { $_.Tag -eq 'WinNotchTest' -and $_.Group -eq 'WinNotchValidation' })
[pscustomobject]@{ TestToastPresentInWindows = $ours.Count -gt 0; CheckedAt = [DateTimeOffset]::Now.ToString('O') } | ConvertTo-Json

# Run with Windows PowerShell 5.1; sends only this local test toast.
param(
    [switch]$CheckOnly,
    [ValidateSet('Windows PowerShell', 'Windows PowerShell ISE')][string]$Sender = 'Windows PowerShell',
    [ValidateLength(1, 160)][string]$Title = 'WinNotch live test'
)
$ErrorActionPreference = 'Stop'
[void][Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
[void][Windows.UI.Notifications.ToastNotification, Windows.UI.Notifications, ContentType = WindowsRuntime]
[void][Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime]
$senderApp = Get-StartApps | Where-Object { $_.Name -eq $Sender } | Select-Object -First 1
if (-not $senderApp) { throw "The $Sender Start menu identity is required for this test." }
if (-not $CheckOnly) {
    $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
    $xml.LoadXml('<toast><visual><binding template="ToastGeneric"><text>WinNotch live test</text><text>A real Windows notification. Dismissing its notch preview should keep this original in Windows.</text></binding></visual><audio silent="true"/></toast>')
    $xml.SelectSingleNode('//text').InnerText = $Title
    $toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
    $toast.Tag = 'WinNotchTest'
    $toast.Group = 'WinNotchValidation'
    $toast.ExpirationTime = [DateTimeOffset]::Now.AddMinutes(10)
    [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($senderApp.AppID).Show($toast)
}
$ours = @([Windows.UI.Notifications.ToastNotificationManager]::History.GetHistory($senderApp.AppID) | Where-Object { $_.Tag -eq 'WinNotchTest' -and $_.Group -eq 'WinNotchValidation' })
[pscustomobject]@{ Sender = $Sender; TestToastPresentInWindows = $ours.Count -gt 0; CheckedAt = [DateTimeOffset]::Now.ToString('O') } | ConvertTo-Json

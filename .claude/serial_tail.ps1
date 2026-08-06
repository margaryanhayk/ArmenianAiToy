$ErrorActionPreference = 'Stop'
$logPath = 'C:\Users\hayk.margaryan\Documents\Projects\ArmenianAiToy\.claude\monitor.log'
if (Test-Path $logPath) { Remove-Item $logPath -Force }
$port = New-Object System.IO.Ports.SerialPort 'COM7', 115200, 'None', 8, 'One'
$port.DtrEnable = $false
$port.RtsEnable = $false
$port.ReadTimeout = 500
$port.NewLine = "`n"
$port.Encoding = [System.Text.Encoding]::UTF8
try { $port.Open() } catch { Write-Output "OPEN_FAIL: $($_.Exception.Message)"; exit 1 }
Write-Output "OPENED"
while ($true) {
    try {
        $line = $port.ReadLine()
        Add-Content -Path $logPath -Value $line -Encoding UTF8
    } catch [System.TimeoutException] {
        # no data, loop
    } catch {
        Add-Content -Path $logPath -Value "ERR: $($_.Exception.Message)" -Encoding UTF8
        Start-Sleep -Milliseconds 200
    }
}

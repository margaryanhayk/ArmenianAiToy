# Read-only serial monitor for the toy.
#
# It NEVER touches DTR or RTS. On the ESP32-S3's native USB-CDC port those
# two lines are the reset/boot-mode gesture -- pulsing them is what put the
# toy into DOWNLOAD(USB/UART0) mode on 2026-08-17 and cost an evening. The
# .NET SerialPort defaults leave both false; this script asserts that
# explicitly so a future edit cannot quietly re-enable them.
param(
  [string]$Port = "COM7",
  [int]$Seconds = 120,
  [string]$Out  = ""
)
$sp = New-Object System.IO.Ports.SerialPort $Port,115200,'None',8,'One'
$sp.DtrEnable  = $false
$sp.RtsEnable  = $false
$sp.ReadTimeout = 500
$sp.Open()
$deadline = (Get-Date).AddSeconds($Seconds)
$lines = New-Object System.Collections.Generic.List[string]
while ((Get-Date) -lt $deadline) {
  if (-not $sp.IsOpen) { break }
  try {
    $line = $sp.ReadLine()
    Write-Host $line
    $lines.Add($line)
  } catch [TimeoutException] {
  } catch {
    # The port vanished (a second monitor grabbed it, or the toy was
    # unplugged). Stop rather than spew one stack trace per 500 ms -- the
    # noise buried a real log once and cost a re-run.
    break
  }
}
$sp.Close()
if ($Out -ne "") {
  [IO.File]::WriteAllLines($Out, $lines, (New-Object Text.UTF8Encoding($false)))
}

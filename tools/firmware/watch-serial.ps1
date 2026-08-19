# Read-only serial monitor for the toy.
#
# It NEVER touches DTR or RTS. On the ESP32-S3's native USB-CDC those two
# lines are the reset/boot-mode gesture -- pulsing them is what put the toy
# into DOWNLOAD(USB/UART0) mode on 2026-08-17 and cost an evening. The .NET
# SerialPort defaults leave both false; this asserts it explicitly so a later
# edit cannot quietly re-enable them.
#
# It RECONNECTS. The S3's USB is provided by the chip itself, so every reset
# re-enumerates the device and the host port vanishes for a second or two.
# A monitor that exits on that loses everything after the first reboot --
# on 2026-08-19 a 300 s capture ended at 34 s and threw away the owner's
# button presses, which is the whole thing it had been started to record.
param(
  [string]$Port = "COM7",
  [int]$Seconds = 120,
  [string]$Out  = ""
)
$deadline = (Get-Date).AddSeconds($Seconds)
$lines = New-Object System.Collections.Generic.List[string]

while ((Get-Date) -lt $deadline) {
  $sp = $null
  try {
    $sp = New-Object System.IO.Ports.SerialPort $Port,115200,'None',8,'One'
    $sp.DtrEnable   = $false
    $sp.RtsEnable   = $false
    $sp.ReadTimeout = 500
    $sp.Open()
  } catch {
    Start-Sleep -Milliseconds 400   # port not back yet after a re-enumerate
    continue
  }
  try {
    while ((Get-Date) -lt $deadline -and $sp.IsOpen) {
      try {
        $line = $sp.ReadLine()
        Write-Host $line
        $lines.Add($line)
      } catch [TimeoutException] {
      } catch {
        break        # port went away -- fall out and re-open
      }
    }
  } finally {
    try { if ($sp.IsOpen) { $sp.Close() } } catch { }
  }
}

if ($Out -ne "") {
  [IO.File]::WriteAllLines($Out, $lines, (New-Object Text.UTF8Encoding($false)))
}

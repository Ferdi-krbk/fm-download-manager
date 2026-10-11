Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$exe = (Resolve-Path "src\McDm.App\bin\Release\net8.0-windows\MCDownloadManager.exe").Path
function Procs { @(Get-Process MCDownloadManager -ErrorAction SilentlyContinue) }
function Port { try { $c = New-Object Net.Sockets.TcpClient; $c.Connect('127.0.0.1',48213); $c.Close(); $true } catch { $false } }
function Wins { $p = Procs; if (-not $p) { return 'none' }
  $cond = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::ProcessIdProperty, $p[0].Id)
  $els = [Windows.Automation.AutomationElement]::RootElement.FindAll('Children', $cond)
  ($els | Where-Object { -not $_.Current.IsOffscreen -and $_.Current.Name } | ForEach-Object { "'" + $_.Current.Name + "'" }) -join ',' }
function WaitUntil($f, $sec=10) { $end=(Get-Date).AddSeconds($sec); while((Get-Date) -lt $end){ if(& $f){return $true}; Start-Sleep -Milliseconds 300 }; $false }
Start-Process $exe
[void](WaitUntil { (Wins) -match 'MC Download' })
"1 started: procs=$((Procs).Count) visibleWindows=$(Wins) bridgePort=$(Port)"
(Procs)[0].CloseMainWindow() | Out-Null
[void](WaitUntil { (Wins) -notmatch 'MC Download' })
"2 closed window: alive=$((Procs).Count -eq 1) visibleWindows=$(Wins) bridgePort=$(Port)"
Start-Process $exe
[void](WaitUntil { (Wins) -match 'MC Download' })
"3 second launch: procs=$((Procs).Count) visibleWindows=$(Wins)"
Start-Process $exe -ArgumentList '--quit'
[void](WaitUntil { (Procs).Count -eq 0 } 15)
"4 --quit: procs=$((Procs).Count) bridgePort=$(Port)"
Start-Process $exe -ArgumentList '--background'
[void](WaitUntil { Port } 10); Start-Sleep 2
"5 --background: procs=$((Procs).Count) visibleWindows=[$(Wins)] bridgePort=$(Port)"
Start-Process $exe -ArgumentList '--quit'
[void](WaitUntil { (Procs).Count -eq 0 } 15)
"6 final: procs=$((Procs).Count)"

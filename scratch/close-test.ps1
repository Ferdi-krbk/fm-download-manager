Add-Type @"
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class W {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static List<string> For(int pid) { var r = new List<string>(); EnumWindows((h,l)=>{ uint p; GetWindowThreadProcessId(h,out p); if(p==pid){ var sb=new StringBuilder(256); GetWindowText(h,sb,256); r.Add(h.ToInt64()+"|"+sb+"|vis="+IsWindowVisible(h)); } return true;}, IntPtr.Zero); return r; }
}
"@
$exe = (Resolve-Path "src\McDm.App\bin\Release\net8.0-windows\MCDownloadManager.exe").Path
$p = Start-Process $exe -PassThru; Start-Sleep 4
"before:"; [W]::For($p.Id)
$h = [IntPtr][long](([W]::For($p.Id) | ? { $_ -match 'MC Download Manager\|vis=True' } | % { $_.Split('|')[0] })[0])
[W]::PostMessage($h, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null; Start-Sleep 2
"after WM_CLOSE:"; [W]::For($p.Id)
"alive: " + (-not $p.HasExited)
Start-Process $exe -ArgumentList '--quit'; Start-Sleep 3

# Helpers for capturing the RUNNING MochiDock (it is never launched from here). Dot-source this file from an ELEVATED shell:
# MochiDock runs as Administrator and Windows blocks simulated input and window queries from a normal process.
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
if (-not ('Cap' -as [type])) {
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class Cap {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] static extern void mouse_event(uint f, int dx, int dy, int d, IntPtr e);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  // Moves a window to the top of the topmost group without activating it or changing its size or position.
  public static void RaiseTopmost(IntPtr h) { SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  public class Win { public IntPtr H; public bool Visible; public int L, T, R, B; public string Title, Class; public bool TopMost; }
  public static List<Win> ForProcess(uint pid) {
    var list = new List<Win>();
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid) {
        RECT r; GetWindowRect(h, out r);
        var t = new StringBuilder(200); var c = new StringBuilder(200);
        GetWindowText(h, t, 200); GetClassName(h, c, 200);
        list.Add(new Win { H = h, Visible = IsWindowVisible(h), L = r.L, T = r.T, R = r.R, B = r.B, Title = t.ToString(), Class = c.ToString(), TopMost = (GetWindowLong(h, -20) & 0x8) != 0 });
      }
      return true;
    }, IntPtr.Zero);
    return list;
  }
  // Bounding box of pixels further than tol from the plane through the four corner pixels (BGRA bytes).
  public static int[] BBox(byte[] b, int stride, int w, int h, int tol) {
    int minX = w, minY = h, maxX = -1, maxY = -1;
    int[] tl = new int[3], tr = new int[3], bl = new int[3], br = new int[3];
    for (int c = 0; c < 3; c++) { tl[c] = b[c]; tr[c] = b[(w - 1) * 4 + c]; bl[c] = b[(h - 1) * stride + c]; br[c] = b[(h - 1) * stride + (w - 1) * 4 + c]; }
    for (int y = 0; y < h; y++) {
      double fy = y / (double)(h - 1);
      for (int x = 0; x < w; x++) {
        double fx = x / (double)(w - 1);
        for (int c = 0; c < 3; c++) {
          double pred = tl[c] * (1 - fx) * (1 - fy) + tr[c] * fx * (1 - fy) + bl[c] * (1 - fx) * fy + br[c] * fx * fy;
          if (Math.Abs(pred - b[y * stride + x * 4 + c]) > tol) {
            if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y; break;
          }
        }
      }
    }
    return new int[] { minX, minY, maxX, maxY };
  }
  public static void Down() { mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); }
  public static void Up() { mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); }
}
"@
}
[void][Cap]::SetProcessDPIAware()

function Get-DockWindows {
  $proc = Get-Process MochiDock -ErrorAction Stop | Select-Object -First 1
  [Cap]::ForProcess([uint32]$proc.Id) | Where-Object { $_.Visible -and ($_.R - $_.L) -gt 20 -and ($_.B - $_.T) -gt 10 }
}

function Save-Region([int]$x, [int]$y, [int]$w, [int]$h, [string]$path) {
  $b = [Drawing.Bitmap]::new($w, $h)
  $g = [Drawing.Graphics]::FromImage($b)
  $g.CopyFromScreen($x, $y, 0, 0, [Drawing.Size]::new($w, $h))
  $g.Dispose(); $b.Save($path, [Drawing.Imaging.ImageFormat]::Png); $b.Dispose()
}

function Get-MainDock { Get-DockWindows | Where-Object { $_.Title -eq 'Mochi Dock' } | Select-Object -First 1 }
# The backdrop is topmost, so put the dock back above it (it is topmost too). Call once after starting the backdrop.
function Raise-Dock { [Cap]::RaiseTopmost((Get-MainDock).H) }

# Captures the dock window, then crops to what the dock actually draws. The backdrop is a smooth gradient, so a plane fitted to the
# four corners of the capture predicts it; any pixel further than $Tolerance from that plane belongs to the dock.
function Save-Dock([string]$path, [int]$pad = 18, [int]$Tolerance = 14, [string[]]$Redact = @()) {
  $d = Get-MainDock
  $w = $d.R - $d.L; $h = $d.B - $d.T
  $bmp = [Drawing.Bitmap]::new($w, $h)
  $g = [Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($d.L, $d.T, 0, 0, [Drawing.Size]::new($w, $h)); $g.Dispose()
  # Redaction: any Text element whose name matches one of the patterns is painted over with the colour just left of it.
  if ($Redact.Count -gt 0) {
    $root = [Windows.Automation.AutomationElement]::FromHandle($d.H)
    $texts = $root.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Text' }
    $g2 = [Drawing.Graphics]::FromImage($bmp)
    foreach ($t in $texts) {
      $name = $t.Current.Name; $hit = $false
      foreach ($pat in $Redact) { if ($name -match $pat) { $hit = $true } }
      if (-not $hit) { continue }
      $r = $t.Current.BoundingRectangle
      $rx = [int]($r.X - $d.L) - 2; $ry = [int]($r.Y - $d.T) - 1; $rw = [int]$r.Width + 4; $rh = [int]$r.Height + 2
      if ($rw -le 4 -or $rw -gt 400 -or $rh -gt 40) { continue }
      $sx = [Math]::Max(0, $rx - 3); $sy = [Math]::Min($h - 1, [Math]::Max(0, $ry + [int]($rh / 2)))
      $brush = [Drawing.SolidBrush]::new($bmp.GetPixel($sx, $sy))
      $g2.FillRectangle($brush, $rx, $ry, $rw, $rh); $brush.Dispose()
      Write-Host "redacted: $($name.Substring(0, [Math]::Min(6, $name.Length)))... ($rw x $rh)"
    }
    $g2.Dispose()
  }
  $rect = [Drawing.Rectangle]::new(0, 0, $w, $h)
  $data = $bmp.LockBits($rect, [Drawing.Imaging.ImageLockMode]::ReadOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $bytes = New-Object byte[] ($data.Stride * $h); [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length); $bmp.UnlockBits($data)
  $stride = $data.Stride
  $box = [Cap]::BBox($bytes, $stride, $w, $h, $Tolerance)
  $minX = $box[0]; $minY = $box[1]; $maxX = $box[2]; $maxY = $box[3]
  if ($maxX -le $minX) { $bmp.Save($path, [Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose(); return }
  $x0 = [Math]::Max(0, $minX - $pad); $x1 = [Math]::Min($w - 1, $maxX + $pad)
  $y0 = 0; $y1 = [Math]::Min($h - 1, $maxY + $pad)   # the dock is attached to the top edge: no padding above
  $crop = $bmp.Clone([Drawing.Rectangle]::new($x0, $y0, $x1 - $x0 + 1, $y1 - $y0 + 1), $bmp.PixelFormat)
  $crop.Save($path, [Drawing.Imaging.ImageFormat]::Png); $crop.Dispose(); $bmp.Dispose()
}

function Move-Mouse([int]$x, [int]$y, [int]$steps = 18, [int]$delayMs = 12) {
  $p = [Windows.Forms.Cursor]::Position
  for ($i = 1; $i -le $steps; $i++) {
    $t = $i / $steps; $t = $t * $t * (3 - 2 * $t)   # smoothstep
    [void][Cap]::SetCursorPos([int]($p.X + ($x - $p.X) * $t), [int]($p.Y + ($y - $p.Y) * $t))
    Start-Sleep -Milliseconds $delayMs
  }
}

function Click-At([int]$x, [int]$y) {
  Move-Mouse $x $y
  Start-Sleep -Milliseconds 80
  [Cap]::Down(); Start-Sleep -Milliseconds 60; [Cap]::Up()
}

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
function Get-Header {
  # Header buttons of the expanded dock in left-to-right order: the tab buttons, then pin and settings (always the last two).
  $d = Get-MainDock
  $root = [Windows.Automation.AutomationElement]::FromHandle($d.H)
  $btn = [Windows.Automation.Condition]::TrueCondition
  $found = $root.FindAll([Windows.Automation.TreeScope]::Descendants, $btn) | Where-Object {
    $_.Current.ControlType.ProgrammaticName -eq 'ControlType.Button' -and $_.Current.BoundingRectangle.Top -lt 44 -and $_.Current.BoundingRectangle.Width -gt 0 }
  $sorted = @($found | Sort-Object { $_.Current.BoundingRectangle.X })
  $rects = $sorted | ForEach-Object { $_.Current.BoundingRectangle }
  $tabs = @($rects | Select-Object -First ($rects.Count - 2))
  $tips = @($sorted | Select-Object -First ($sorted.Count - 2) | ForEach-Object { $_.Current.HelpText })   # each tab's ToolTip
  [pscustomobject]@{ Tabs = $tabs; Tips = $tips; Pin = $rects[-2]; Gear = $rects[-1] }
}
function Click-Rect($r) { Click-At ([int]($r.X + $r.Width / 2)) ([int]($r.Y + $r.Height / 2)) }
function Click-Tab([int]$i) { Click-Rect ((Get-Header).Tabs[$i]) }
# Clicks the tab whose ToolTip is $tip (for example 'Weather Forecast'), so the scripts work whichever optional tabs are switched on.
function Click-TabNamed([string]$tip) {
  $h = Get-Header; $i = [Array]::IndexOf($h.Tips, $tip)
  if ($i -lt 0) { throw "No '$tip' tab in the header (tabs: $($h.Tips -join ', ')). Switch it on in Settings > Tabs." }
  Click-Rect $h.Tabs[$i]
}
# Parks the pointer on the dock's top edge strip (above the header buttons, so no tooltip) while the card settles.
function Rest-Mouse { Raise-Dock; $d = Get-MainDock; Move-Mouse ($d.L + 340) ($d.T + 3) 10; Start-Sleep -Milliseconds 1700 }
function Find-Control([string]$name, [string]$type = 'ControlType.CheckBox') {
  $d = Get-MainDock; $root = [Windows.Automation.AutomationElement]::FromHandle($d.H)
  $root.FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -eq $name -and $_.Current.ControlType.ProgrammaticName -eq $type } | Select-Object -First 1
}

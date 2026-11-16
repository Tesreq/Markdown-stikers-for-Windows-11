param()
. "$PSScriptRoot\common.ps1"
Add-Type -AssemblyName System.Drawing
$destination = Resolve-WorkspacePath 'src\StickyDesk.App\Assets\StickyDesk.ico'
New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null

function New-RoundedPath([float]$X, [float]$Y, [float]$Width, [float]$Height, [float]$Radius) {
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return ,$path
}

$frames = @()
foreach ($size in @(16, 24, 32, 48, 64, 128, 256)) {
    $bitmap = New-Object Drawing.Bitmap($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $stream = New-Object IO.MemoryStream
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($size / 256.0, $size / 256.0)
        $outer = New-RoundedPath 0 0 256 256 56
        $paper = New-RoundedPath 45 30 166 196 17
        $background = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(42, 53, 62))
        $surface = New-Object Drawing.SolidBrush([Drawing.Color]::FromArgb(255, 248, 222))
        $accent = New-Object Drawing.Pen([Drawing.Color]::FromArgb(190, 151, 57), 8)
        $ink = New-Object Drawing.Pen([Drawing.Color]::FromArgb(100, 112, 109), 8)
        try {
            $accent.StartCap = $accent.EndCap = [Drawing.Drawing2D.LineCap]::Round
            $ink.StartCap = $ink.EndCap = [Drawing.Drawing2D.LineCap]::Round
            $graphics.FillPath($background, $outer)
            $graphics.FillPath($surface, $paper)
            $graphics.DrawLine($accent, 73, 65, 104, 65)
            $graphics.DrawLine($ink, 73, 109, 182, 109)
            $graphics.DrawLine($ink, 73, 146, 182, 146)
            $graphics.DrawLine($ink, 73, 183, 143, 183)
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $frames += [pscustomobject]@{ Size = $size; Bytes = $stream.ToArray() }
        } finally {
            $outer.Dispose(); $paper.Dispose(); $background.Dispose(); $surface.Dispose(); $accent.Dispose(); $ink.Dispose()
        }
    } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
$output = [IO.File]::Create($destination)
$writer = New-Object IO.BinaryWriter($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
    $offset = 6 + 16 * $frames.Count
    foreach ($frame in $frames) {
        $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $frame.Bytes.Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose(); $output.Dispose() }
Write-Host "Создана иконка: $destination"

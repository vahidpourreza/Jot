Add-Type -AssemblyName System.Drawing
$iconPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\assets\jot.ico'))
$bitmaps = [Collections.Generic.List[byte[]]]::new()
$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
foreach ($size in $sizes) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 24.0, $size / 24.0)
    $shape = [Drawing.Drawing2D.GraphicsPath]::new([Drawing.Drawing2D.FillMode]::Alternate)
    $shape.AddLine(5,2,13,2); $shape.AddLine(13,2,13,8)
    $shape.AddBezier(13,8,13,9.1,13.9,10,15,10); $shape.AddLine(15,10,21,10)
    $shape.AddLine(21,10,21,20); $shape.AddArc(17,18,4,4,0,90)
    $shape.AddLine(19,22,5,22); $shape.AddArc(3,18,4,4,90,90)
    $shape.AddLine(3,20,3,4); $shape.AddArc(3,2,4,4,180,90)
    $shape.CloseFigure()
    $shape.AddRectangle([Drawing.RectangleF]::new(7,12,10,2)); $shape.AddRectangle([Drawing.RectangleF]::new(7,16,7,2))
    $shape.AddPolygon([Drawing.PointF[]]@([Drawing.PointF]::new(15,2),[Drawing.PointF]::new(21,8),[Drawing.PointF]::new(15,8)))
    $halo=[Drawing.Pen]::new([Drawing.Color]::FromArgb(235,255,255,255),[single]1.2)
    $halo.LineJoin=[Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawPath($halo,$shape)
    $brush=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(30,30,30))
    $graphics.FillPath($brush,$shape)
    $halo.Dispose(); $brush.Dispose()
    $memory = [IO.MemoryStream]::new()
    $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
    $bitmaps.Add($memory.ToArray())
    $memory.Dispose(); $graphics.Dispose(); $shape.Dispose(); $bitmap.Dispose()
}
$stream = [IO.File]::Create($iconPath)
$writer = [IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $encoded = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$encoded); $writer.Write([byte]$encoded)
    $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$bitmaps[$i].Length); $writer.Write([uint32]$offset)
    $offset += $bitmaps[$i].Length
}
foreach ($bytes in $bitmaps) { $writer.Write($bytes) }
$writer.Dispose()
Write-Output $iconPath

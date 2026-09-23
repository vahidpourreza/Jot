Add-Type -AssemblyName System.Drawing
$iconPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\assets\jot.ico'))
$bitmaps = [Collections.Generic.List[byte[]]]::new()
$sizes = @(16, 24, 32, 48, 64, 128, 256)
foreach ($size in $sizes) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.ScaleTransform($size / 24.0, $size / 24.0)
    $shape = [Drawing.Drawing2D.GraphicsPath]::new()
    $shape.AddArc(4, 4, 4, 4, 180, 90); $shape.AddArc(16, 4, 4, 4, 270, 90)
    $shape.AddArc(16, 18, 4, 4, 0, 90); $shape.AddArc(4, 18, 4, 4, 90, 90)
    $shape.CloseFigure()
    foreach($layer in @(@('#ffffff',2.9),@('#1e1e1e',2.0))) {
        $pen = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($layer[0]),[single]$layer[1])
        $pen.StartCap = $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [Drawing.Drawing2D.LineJoin]::Round
        $graphics.DrawLine($pen,8,2,8,6); $graphics.DrawLine($pen,12,2,12,6); $graphics.DrawLine($pen,16,2,16,6)
        $graphics.DrawPath($pen,$shape)
        $graphics.DrawLine($pen,8,10,14,10); $graphics.DrawLine($pen,8,14,16,14); $graphics.DrawLine($pen,8,18,13,18)
        $pen.Dispose()
    }
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

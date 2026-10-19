# Rasterizza il sottoinsieme SVG usato dall'icona con System.Drawing incluso in Windows.
# Nessun pacchetto, download o runtime aggiuntivo. Il sorgente SVG resta la fonte geometrica.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$root=Split-Path $PSScriptRoot
[xml]$svg=Get-Content -LiteralPath (Join-Path $root 'assets/OnlyTweak.svg') -Raw
function Convert-Icon([int]$size) {
    $bitmap=[Drawing.Bitmap]::new($size,$size)
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform($size/64.0,$size/64.0)
        $rect=$svg.svg.rect; $x=[float]$rect.x; $y=[float]$rect.y; $w=[float]$rect.width; $h=[float]$rect.height; $d=2*[float]$rect.rx
        $path=[Drawing.Drawing2D.GraphicsPath]::new()
        $brush=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($rect.fill))
        try {
            $path.AddArc($x,$y,$d,$d,180,90); $path.AddArc($x+$w-$d,$y,$d,$d,270,90)
            $path.AddArc($x+$w-$d,$y+$h-$d,$d,$d,0,90); $path.AddArc($x,$y+$h-$d,$d,$d,90,90); $path.CloseFigure()
            $graphics.FillPath($brush,$path)
        } finally { $brush.Dispose(); $path.Dispose() }
        $lines=$svg.svg.g[0]; $pen=[Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($lines.stroke),[float]$lines.'stroke-width')
        try {
            $pen.StartCap=$pen.EndCap=[Drawing.Drawing2D.LineCap]::Round
            foreach($segment in [regex]::Matches($lines.path.d,'M(\d+) (\d+)v(\d+)')) {
                $lx=[float]$segment.Groups[1].Value; $ly=[float]$segment.Groups[2].Value; $length=[float]$segment.Groups[3].Value
                $graphics.DrawLine($pen,$lx,$ly,$lx,$ly+$length)
            }
        } finally { $pen.Dispose() }
        $knobs=$svg.svg.g[1]; $brush=[Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml($knobs.fill))
        $pen=[Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml($knobs.stroke),[float]$knobs.'stroke-width')
        try {
            foreach($circle in $knobs.circle) {
                $radius=[float]$circle.r; $cx=[float]$circle.cx-$radius; $cy=[float]$circle.cy-$radius
                $graphics.FillEllipse($brush,$cx,$cy,2*$radius,2*$radius); $graphics.DrawEllipse($pen,$cx,$cy,2*$radius,2*$radius)
            }
        } finally { $brush.Dispose(); $pen.Dispose() }
    } finally { $graphics.Dispose() }
    return ,$bitmap
}
$sizes=@(16,20,24,32,48,64,128,256)
$images=@(foreach($size in $sizes) {
    $bitmap=Convert-Icon $size; $stream=[IO.MemoryStream]::new()
    try { $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png); ,$stream.ToArray() } finally { $stream.Dispose(); $bitmap.Dispose() }
})
$output=[IO.File]::Create((Join-Path $root 'assets/OnlyTweak.ico')); $writer=[IO.BinaryWriter]::new($output)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset=6+16*$sizes.Count
    for($i=0;$i -lt $sizes.Count;$i++) {
        $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset+=$images[$i].Length
    }
    foreach($bytes in $images){$writer.Write([byte[]]$bytes)}
} finally { $writer.Dispose(); $output.Dispose() }
$preview=Convert-Icon 512
try { $preview.Save((Join-Path $root 'assets/OnlyTweak.png'),[Drawing.Imaging.ImageFormat]::Png) } finally { $preview.Dispose() }

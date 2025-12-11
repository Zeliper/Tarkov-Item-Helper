$content = [System.IO.File]::ReadAllText('C:\Users\Zeliper\Desktop\LightHouse.txt')
$startIdx = $content.IndexOf('<svg')
$endIdx = $content.IndexOf('</svg>') + 6
$svg = $content.Substring($startIdx, $endIdx - $startIdx)
$outputPath = 'C:\Users\Zeliper\source\repos\TarkovHelper\TarkovDBEditor\Resources\LighthouseMap.svg'
[System.IO.File]::WriteAllText($outputPath, $svg, [System.Text.Encoding]::UTF8)
Write-Host "SVG extracted successfully. Length: $($svg.Length) characters"

# Data Point 1
$g1_x = -96.14; $g1_z = -36.50
$m1_x = 1646.14; $m1_y = 2013.5

# Data Point 2
$g2_x = -79.41; $g2_z = -110.26
$m2_x = 1629.41; $m2_y = 1939.74

# Solve linear system:
# mapX = gameX * scaleX + offsetX
# mapY = gameZ * scaleZ + offsetY

# For X:
$scaleX = ($m1_x - $m2_x) / ($g1_x - $g2_x)
Write-Host "ScaleX: $scaleX"

$offsetX = $m1_x - ($g1_x * $scaleX)
Write-Host "OffsetX: $offsetX"

# For Y (using Z):
$scaleZ = ($m1_y - $m2_y) / ($g1_z - $g2_z)
Write-Host "ScaleZ: $scaleZ"

$offsetZ = $m1_y - ($g1_z * $scaleZ)
Write-Host "OffsetZ: $offsetZ"

Write-Host ""
Write-Host "Verification Point 1:"
$calcX1 = $g1_x * $scaleX + $offsetX
$calcY1 = $g1_z * $scaleZ + $offsetZ
Write-Host "Calculated: ($calcX1, $calcY1)"
Write-Host "Expected:   ($m1_x, $m1_y)"

Write-Host ""
Write-Host "Verification Point 2:"
$calcX2 = $g2_x * $scaleX + $offsetX
$calcY2 = $g2_z * $scaleZ + $offsetZ
Write-Host "Calculated: ($calcX2, $calcY2)"
Write-Host "Expected:   ($m2_x, $m2_y)"

Write-Host ""
Write-Host "Final Formula:"
Write-Host "mapX = gameX * $scaleX + $offsetX"
Write-Host "mapY = gameZ * $scaleZ + $offsetZ"

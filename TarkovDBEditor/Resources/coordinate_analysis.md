# Lighthouse Map Coordinate Analysis

## Known Data Points

### From HTML (marker-arrow)
- Map X: 1646.14 px
- Map Y: 2013.5 px
- Rotation: 187.261 deg

### From Screenshot Filename
- Game X: -96.14
- Game Y: 18.95 (height, unused for 2D map)
- Game Z: -36.50
- Quaternion: (0.11351, 0.06271, -0.00543, 0.99154)

### Map Dimensions
- SVG Width: 3100 px
- SVG Height: 3700 px
- Background rect: x=1000, y=1000, width=1100, height=1700

## Reverse Engineering the Formula

### Step 1: Calculate Scale and Offset

If mapX = gameX * scaleX + offsetX
And mapY = gameZ * scaleY + offsetY

From single point:
- 1646.14 = -96.14 * scaleX + offsetX
- 2013.5 = -36.50 * scaleY + offsetY

Need at least 2 points to solve, but we can estimate based on map bounds.

### Step 2: Estimate from Map Background

The visible map area is at:
- x: 1000 to 2100 (width 1100)
- y: 1000 to 2700 (height 1700)

Center of map background:
- centerX = 1000 + 1100/2 = 1550
- centerY = 1000 + 1700/2 = 1850

### Step 3: Possible Formula

Looking at the marker position (1646.14, 2013.5) relative to center (1550, 1850):
- Offset from center: X = +96.14, Y = +163.5

Game coordinates: X = -96.14, Z = -36.50

Hypothesis 1: Simple linear with negation
- mapX = -gameX + offset_x
- mapY = -gameZ * scale + offset_y

Test:
- mapX = -(-96.14) + 1550 = 96.14 + 1550 = 1646.14 ✓ EXACT MATCH!
- mapY = -(-36.50) * ? + ? = 2013.5

For mapY:
If mapY = -gameZ * scaleY + offsetY
2013.5 = 36.50 * scaleY + offsetY

Need more constraints. Let's assume the map is roughly centered.

### Step 4: Yaw Calculation

Quaternion: (qx=0.11351, qy=0.06271, qz=-0.00543, qw=0.99154)

Standard Yaw calculation:
siny_cosp = 2 * (qw * qy + qx * qz) = 2 * (0.99154 * 0.06271 + 0.11351 * (-0.00543))
         = 2 * (0.06218 - 0.00062) = 2 * 0.06156 = 0.12312

cosy_cosp = 1 - 2 * (qy² + qz²) = 1 - 2 * (0.00393 + 0.00003) = 1 - 0.00792 = 0.99208

yaw_rad = atan2(0.12312, 0.99208) = 0.1235 rad = 7.08°

But HTML shows: rotate(187.261deg)

So the formula must include:
rotation = yaw + 180° (or some other transformation)

Let's check: 7.08 + 180 = 187.08° ≈ 187.261° ✓ Close match!

## Final Formulas

### Position
```
mapX = -gameX + 1550
mapY = gameZ * scaleY + offsetY  (needs calibration)
```

### Alternative: Check if Y uses different scale
From the data:
- mapY = 2013.5
- gameZ = -36.50

If centered at y=1850 with some scale:
2013.5 = 1850 + (-36.50) * scale
163.5 = -36.50 * scale
scale = -4.479 (negative, meaning Z axis is inverted)

So: mapY = 1850 - gameZ * 4.479 = 1850 - (-36.50) * 4.479 = 1850 + 163.5 = 2013.5 ✓

But this scale seems arbitrary. Let's reconsider.

### Check X scale too
mapX = 1646.14, gameX = -96.14
1646.14 = 1550 + (-gameX) * 1.0
1646.14 = 1550 + 96.14 ✓

So X scale is 1.0 (or -1.0 depending on direction)

### Revised Formula
```
mapX = 1550 - gameX * 1.0 = 1550 - gameX
mapY = 1850 - gameZ * 4.479  (approximate)
```

Or more likely, there's a consistent scale:
```
mapX = centerX - gameX * scale
mapY = centerY - gameZ * scale
```

If scale is same for both:
From X: scale_x = 1.0 (since 96.14 maps to 96.14 offset)
From Y: 163.5 / 36.50 = 4.479

The scales are different! This suggests the game world aspect ratio differs from the map.

## Rotation Formula
```
mapRotation = yaw_degrees + 180
```

Where yaw is calculated from quaternion using standard formula.

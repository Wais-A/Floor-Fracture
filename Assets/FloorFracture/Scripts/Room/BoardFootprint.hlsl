#ifndef FLOOR_FRACTURE_BOARD_FOOTPRINT
#define FLOOR_FRACTURE_BOARD_FOOTPRINT

// The stationary and rising room floor must open around exactly the same panes.
bool FloorHasPane(float2 board, float heightAboveFloor, float4 halfSize, uint lowMask, uint highMask)
{
    float2 exterior = abs(board) - halfSize.xy;
    if (max(max(exterior.x, exterior.y), abs(heightAboveFloor) - .06) >= 0) return false;
    if (halfSize.w == 0) return true;
    uint2 size = (uint2)round(2 * halfSize.xy * halfSize.w);
    uint2 cell = min((uint2)((board + halfSize.xy) * halfSize.w), size - 1u);
    uint index = cell.y * size.x + cell.x;
    uint mask = index < 32u ? lowMask : highMask;
    return ((mask >> (index & 31u)) & 1u) != 0u;
}

#endif

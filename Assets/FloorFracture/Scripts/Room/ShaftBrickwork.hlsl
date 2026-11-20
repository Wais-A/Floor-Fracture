#ifndef FLOOR_FRACTURE_SHAFT_BRICKWORK
#define FLOOR_FRACTURE_SHAFT_BRICKWORK

// The shaft and room floor use the same half-brick running bond and mortar profile.
float2 ShaftBrickCell(float2 courses, float offset)
{
    return float2(courses.x + floor(courses.y) * .5 + offset, courses.y);
}

float ShaftBrickFace(float2 cell, float filterWidth)
{
    float2 position = frac(cell);
    float2 joint = min(position, 1 - position);
    return smoothstep(.008 - filterWidth, .041 + filterWidth, min(joint.x, joint.y));
}

#endif

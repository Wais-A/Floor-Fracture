#ifndef FLOOR_FRACTURE_SHAFT_BRICKWORK
#define FLOOR_FRACTURE_SHAFT_BRICKWORK

// The rising floor and deep chamber share a running bond measured in metres.
half3 ShaftBrickwork(float2 metres)
{
    const half3 stone = half3(.165, .285, .36);
    const half3 mortar = half3(.044, .078, .099);
    float2 courses = metres / float2(.54, .27);
    float2 width = fwidth(courses);
    float footprint = max(width.x, width.y);
    float filterWidth = min(.16, footprint * .5);
    float2 cell = float2(courses.x + floor(courses.y) * .5, courses.y);
    float2 position = frac(cell);
    float2 joint = min(position, 1 - position);
    float face = smoothstep(.008 - filterWidth, .041 + filterWidth, min(joint.x, joint.y));
    float shade = frac(52.9829189 * frac(dot(floor(cell), float2(.06711056, .00583715))));
    float bevel = smoothstep(.018, .095, min(position.x, 1-position.y))
                - smoothstep(.018, .095, min(1-position.x, position.y));
    half3 detailed = lerp(mortar, stone * (.88 + .23 * shade + .12 * bevel), face);
    // Very distant courses in the deep shaft become subpixel. Use their mean color.
    return lerp(detailed, lerp(mortar, stone, .92), smoothstep(.28, .85, footprint));
}

#endif

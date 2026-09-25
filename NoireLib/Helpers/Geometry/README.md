# Helper Documentation : Geometry Helpers

You are reading the documentation for the `Geometry` static helpers.

## Table of Contents
- [Overview](#overview)
- [Ray Intersections](#ray-intersections)
- [Closest Approach](#closest-approach)
- [Volumes and Polygons](#volumes-and-polygons)
- [Frustum Culling](#frustum-culling)
- [2D Point Tests](#2d-point-tests)
- [Curves](#curves)
- [SVG Paths](#svg-paths)
- [Rotations](#rotations)
- [Used by](#used-by)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

Five types in the `NoireLib.Helpers` namespace covering the geometry a plugin needs to hit-test, build and place what
it draws:

- **`Geometry3DHelper`** - ray/plane, ray/sphere, ray/triangle, ray/box and ray/ring intersections, closest-approach
  solvers, AABB overlap, and a convex-polygon clip.
- **`Geometry2DHelper`** - point-to-segment distance, point-in-convex-quad and winding numbers for hit-testing a shape,
  and Bezier and Catmull-Rom samplers for building one.
- **`SvgPathHelper`** - reads an SVG path's `d` attribute into flattened point lists.
- **`TransformHelper`** - `LookRotation`, `FromToRotation` and a `Matrix4x4.Decompose` that cannot hand back garbage.
- **`FrustumPlanes`** - Gribb-Hartmann plane extraction from a view-projection matrix, plus a sphere test.

Every method is a pure function of its arguments. Nothing reads game state. All of it runs on any thread and tests
without a game running.

Conventions: rays take a normalized direction unless a parameter says otherwise, matrices are row-vector, and angles
are radians.

---

## Ray Intersections

```csharp
using NoireLib.Helpers;

// Where does the cursor ray meet the ground plane?
Geometry3DHelper.RayPlane(origin, direction, Vector3.Zero, Vector3.UnitY, out float t, out Vector3 hit);

// Nearest non-negative root; true even when the origin is inside the sphere.
Geometry3DHelper.RaySphere(origin, direction, center, radius, out float t);

// Moller-Trumbore, two-sided on purpose: a back-facing triangle is still under the cursor.
Geometry3DHelper.RayTriangle(origin, direction, a, b, c, out float t);

// Slab test. invDirection is the componentwise reciprocal, hoisted out of the caller's loop.
var inv = new Vector3(1f / direction.X, 1f / direction.Y, 1f / direction.Z);
Geometry3DHelper.RayBox(origin, inv, min, max, tMax);

// A flat ring on a plane, the shape a rotation handle presents.
Geometry3DHelper.RayRing(origin, direction, center, axis, ringRadius, tolerance, out float t);
```

`RayBox` takes each slab's own min and max. A box whose corners arrive swapped still describes the same box, never
an empty one. A direction component of zero is passed as an infinite reciprocal and the slab still resolves.

---

## Closest Approach

```csharp
// The point on an axis line nearest the ray: the solve behind axis-constrained dragging.
// False when the ray is near parallel, where axisParam falls back to projecting the ray origin.
Geometry3DHelper.ClosestAxisParam(rayOrigin, rayDirection, axisPoint, axisDirection, out float axisParam);

// Distance from a ray to a finite segment, for hit-testing a line handle against a pick radius.
float d = Geometry3DHelper.RaySegmentDistance(rayOrigin, rayDirection, a, b, out float rayT);

// Signed sweep about an axis, both points measured from a center after projection into the axis plane.
float angle = Geometry3DHelper.SignedAngleOnPlane(center, axis, from, to);
```

---

## Volumes and Polygons

```csharp
// Touching faces count as an overlap.
Geometry3DHelper.AabbOverlap(aMin, aMax, bMin, bMax);

// Normalize with a fallback, never a NaN.
Vector3 dir = Geometry3DHelper.SafeNormalize(v, Vector3.UnitY);

// Per-component grid snap; a component whose step is zero or less passes through.
Vector3 snapped = Geometry3DHelper.Snap(position, new Vector3(0.5f, 0f, 0.5f));

// Sutherland-Hodgman. Clip against each of a box's six half-spaces in turn to trim a polygon to that box.
var result = new List<Vector3>();
Geometry3DHelper.ClipConvexPolygon(polygon, result, axis: 0, limit: halfWidth, keepGreater: false);
```

`ClipConvexPolygon` clears `result` before it adds anything. A stale vertex never survives a clip. Pass two
different lists and swap them between passes.

---

## Frustum Culling

```csharp
var frustum = FrustumPlanes.FromViewProj(viewProj);

if (frustum.Intersects(center, radius))
    Draw(thing);
```

Five planes, not six. Under an infinite-far projection the far plane is degenerate and is skipped, never
extracted and normalized to nothing. The sphere test is conservative. A sphere just outside a corner can still
report true.

---

## 2D Point Tests

```csharp
// Distance to a finite segment, measured to the nearer endpoint when the segment has no length.
float d = Geometry2DHelper.PointToSegmentDistance(cursor, a, b);

// Winding-agnostic: a quad projected from 3D works whichever way it happens to face.
bool inside = Geometry2DHelper.PointInConvexQuad(cursor, a, b, c, d);

// The building block both rest on: which side of a directed edge a point falls on.
float side = Geometry2DHelper.Cross(a, b, p);

// The same distance squared, for comparing against a squared limit in a tight loop.
float d2 = Geometry2DHelper.PointToSegmentDistanceSquared(cursor, a, b);

// Non-zero means inside under the non-zero fill rule: concave and self-crossing outlines fill as SVG fills them.
bool filled = Geometry2DHelper.WindingNumber(outline, cursor) != 0;
```

---

## Curves

Every sampler writes into a caller's span and returns how many points it wrote: nothing allocates.

```csharp
Span<Vector2> points = stackalloc Vector2[33];

// 16 segments: 17 points, the start included.
int n = Geometry2DHelper.SampleCubic(points, start, control1, control2, end, 16);

// Chained pieces skip their start, which ends the previous piece.
n += Geometry2DHelper.SampleQuadratic(points[n..], end, control, next, 8, includeStart: false);

// Through every point, the end tangents taken from the end points: (count - 1) * 10 + 1 points.
int m = Geometry2DHelper.SampleCatmullRom(points, keyPoints, 10);
```

---

## SVG Paths

```csharp
// Every command, absolute and relative: M L H V C S Q T A Z.
SvgSubpath[] subpaths = SvgPathHelper.Flatten("M2 2h12v12H2z M5 5l6 6");

foreach (var subpath in subpaths)
    drawList.AddPolyline(subpath.Points, color, 1f, subpath.Closed);

// Curves are cut until no point of the true curve is further than the tolerance, in path units.
SvgSubpath[] fine = SvgPathHelper.Flatten(iconPath, tolerance: 0.01f);
```

A subpath with fewer than two points is left out. Reading stops at the first character that is neither a command nor
a number, keeping what was read before it.

---

## Rotations

```csharp
// The rotation whose +Z aims along a direction. The up hint resolves the roll; a hint parallel to
// forward is substituted, never producing a NaN.
Quaternion facing = TransformHelper.LookRotation(target - position, Vector3.UnitY);

// Aim a mesh built along a fixed axis. Opposed directions get a half turn about an arbitrary perpendicular.
Quaternion aim = TransformHelper.FromToRotation(Vector3.UnitY, direction);

// Guaranteed outputs even when Matrix4x4.Decompose refuses the matrix.
TransformHelper.DecomposeSafe(in world, out Vector3 scale, out Quaternion rotation, out Vector3 translation);
```

---

## Used by

Draw3D's picker (`NoireDraw3D.Pick`), the mesh BVH, the native gizmo's handle hit-tests and drag solvers, scene
frustum culling, world-decal polygon trimming, and `SceneNode.LookAt`. `GizmoMath` builds its drag solvers on it.
NoireUI's `NoireRaster` fills and strokes with `WindingNumber` and `PointToSegmentDistanceSquared`, and
`RasterShape.Path` reads its outline with `SvgPathHelper`.

---

## Troubleshooting

- **A pick lands where nothing is drawn.** The epsilons here differ per method on purpose and match what the renderer
  draws. Changing one moves the pick away from the pixels.
- **`RayTriangle` accepts a triangle you expected it to reject.** It is two-sided by design; add your own facing test
  against the triangle normal if you need one side only.
- **A dragged object collapses to nothing.** Read the scale out of `DecomposeSafe`, not `Matrix4x4.Decompose`, whose
  outputs are unspecified when it returns false.

---

## See Also

- [MathHelper](../MathHelper.cs) - scalar and 2D-distance maths, including `Snap` for a single value.
- [Draw3D](../../Draw3D/README.md) - the renderer these primitives were extracted from.
- [Interaction](../../Draw3D/Interaction/README.md) - the gizmo and picking layer built on them.

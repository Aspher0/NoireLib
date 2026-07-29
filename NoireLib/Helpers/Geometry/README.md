# Helper Documentation : Geometry Helpers

You are reading the documentation for the `Geometry` static helpers.

## Table of Contents
- [Overview](#overview)
- [Ray Intersections](#ray-intersections)
- [Closest Approach](#closest-approach)
- [Volumes and Polygons](#volumes-and-polygons)
- [Frustum Culling](#frustum-culling)
- [2D Point Tests](#2d-point-tests)
- [Rotations](#rotations)
- [Used by](#used-by)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

Four types in the `NoireLib.Helpers` namespace covering the geometry a plugin needs to hit-test and place what it
draws:

- **`Geometry3DHelper`** - ray/plane, ray/sphere, ray/triangle, ray/box and ray/ring intersections, closest-approach
  solvers, AABB overlap, and a convex-polygon clip.
- **`Geometry2DHelper`** - point-to-segment distance and point-in-convex-quad, for hit-testing a shape after it is
  drawn.
- **`TransformHelper`** - `LookRotation`, `FromToRotation` and a `Matrix4x4.Decompose` that cannot hand back garbage.
- **`FrustumPlanes`** - Gribb-Hartmann plane extraction from a view-projection matrix, plus a sphere test.

Every method is a pure function of its arguments. Nothing reads game state, so all of it runs on any thread and tests
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

`RayBox` takes each slab's own min and max, so a box whose corners arrive swapped describes the same box rather than
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

// Normalize with a fallback instead of a NaN.
Vector3 dir = Geometry3DHelper.SafeNormalize(v, Vector3.UnitY);

// Per-component grid snap; a component whose step is zero or less passes through.
Vector3 snapped = Geometry3DHelper.Snap(position, new Vector3(0.5f, 0f, 0.5f));

// Sutherland-Hodgman. Clip against each of a box's six half-spaces in turn to trim a polygon to that box.
var result = new List<Vector3>();
Geometry3DHelper.ClipConvexPolygon(polygon, result, axis: 0, limit: halfWidth, keepGreater: false);
```

`ClipConvexPolygon` clears `result` before it adds anything, so a stale vertex never survives a clip. Pass two
different lists and swap them between passes.

---

## Frustum Culling

```csharp
var frustum = FrustumPlanes.FromViewProj(viewProj);

if (frustum.Intersects(center, radius))
    Draw(thing);
```

Five planes, not six: under an infinite-far projection the far plane is degenerate, so it is skipped rather than
extracted and normalized to nothing. The sphere test is conservative, so a sphere just outside a corner can still
report true.

---

## 2D Point Tests

```csharp
// Distance to a finite segment, measured to the nearer endpoint when the segment has no length.
float d = Geometry2DHelper.PointToSegmentDistance(cursor, a, b);

// Winding-agnostic, so a quad projected from 3D works whichever way it happens to face.
bool inside = Geometry2DHelper.PointInConvexQuad(cursor, a, b, c, d);

// The building block both rest on: which side of a directed edge a point falls on.
float side = Geometry2DHelper.Cross(a, b, p);
```

---

## Rotations

```csharp
// The rotation whose +Z aims along a direction. The up hint resolves the roll; a hint parallel to
// forward is substituted rather than producing a NaN.
Quaternion facing = TransformHelper.LookRotation(target - position, Vector3.UnitY);

// Aim a mesh built along a fixed axis. Opposed directions get a half turn about an arbitrary perpendicular.
Quaternion aim = TransformHelper.FromToRotation(Vector3.UnitY, direction);

// Guaranteed outputs even when Matrix4x4.Decompose refuses the matrix.
TransformHelper.DecomposeSafe(in world, out Vector3 scale, out Quaternion rotation, out Vector3 translation);
```

---

## Used by

Draw3D's picker (`NoireDraw3D.Pick`), the mesh BVH, the native gizmo's handle hit-tests and drag solvers, scene
frustum culling, world-decal polygon trimming, and `SceneNode.LookAt`. `InteractMath` and `GizmoMath` remain the
documented names for the interaction layer and forward here.

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

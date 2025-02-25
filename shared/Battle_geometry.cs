﻿using System;
using static shared.CharacterState;

namespace shared {
    public partial class Battle {
        public static (bool, float, float) calcPushbacks(float oldDx, float oldDy, ConvexPolygon a, ConvexPolygon b, ref SatResult overlapResult) {
            float origX = a.X, origY = a.Y;
            try {
                a.SetPosition(origX + oldDx, origY + oldDy);
                overlapResult.OverlapMag = 0;
                overlapResult.OverlapX = 0;
                overlapResult.OverlapY = 0;
                overlapResult.AContainedInB = true;
                overlapResult.BContainedInA = true;
                overlapResult.AxisX = 0;
                overlapResult.AxisY = 0;

                bool overlapped = isPolygonPairOverlapped(a, b, ref overlapResult);
                if (true == overlapped) {
                    float pushbackX = overlapResult.OverlapMag * overlapResult.OverlapX;
                    float pushbackY = overlapResult.OverlapMag * overlapResult.OverlapY;
                    return (true, pushbackX, pushbackY);
                } else {
                    return (false, 0, 0);
                }
            } finally {
                a.SetPosition(origX, origY);
            }
        }

        public static int calcHardPushbacksNorms(int joinIndex, PlayerDownsync currPlayerDownsync, PlayerDownsync thatPlayerInNextFrame, Collider playerCollider, ConvexPolygon playerShape, float snapIntoPlatformOverlap, Vector effPushback, Vector[] hardPushbackNorms, Collision collision, ref SatResult overlapResult) {
            float virtualGripToWall = 0.0f;
            if (OnWall == currPlayerDownsync.CharacterState && 0 == thatPlayerInNextFrame.VelX && currPlayerDownsync.DirX == thatPlayerInNextFrame.DirX) {
                float xfac = 1.0f;
                if (0 > thatPlayerInNextFrame.DirX) {
                    xfac = -xfac;
                }
                virtualGripToWall = xfac * (float)(currPlayerDownsync.Speed) * VIRTUAL_GRID_TO_COLLISION_SPACE_RATIO;
            }
            int retCnt = 0;
            bool collided = playerCollider.CheckAllWithHolder(virtualGripToWall, 0, collision);
            if (!collided) {
                return retCnt;
            }

            while (true) {
                var (exists, bCollider) = collision.PopFirstContactedCollider();

                if (!exists || null == bCollider) {
                    break;
                }
                bool isBarrier = false;

                switch (bCollider.Data) {
                    case PlayerDownsync v1:
                    case Bullet v2:
                        break;
                    default:
                        // By default it's a regular barrier, even if data is nil, note that Golang syntax of switch-case is kind of confusing, this "default" condition is met only if "!*PlayerDownsync && !*Bullet".
                        isBarrier = true;
                        break;
                }

                if (!isBarrier) {
                    continue;
                }
                ConvexPolygon bShape = bCollider.Shape;

                var (overlapped, pushbackX, pushbackY) = calcPushbacks(0, 0, playerShape, bShape, ref overlapResult);

                if (!overlapped) {
                    continue;
                }
                // ALWAY snap into hardPushbacks!
                // [OverlapX, OverlapY] is the unit vector that points into the platform
                pushbackX = (overlapResult.OverlapMag - snapIntoPlatformOverlap) * overlapResult.OverlapX;
                pushbackY = (overlapResult.OverlapMag - snapIntoPlatformOverlap) * overlapResult.OverlapY;

                hardPushbackNorms[retCnt].X = overlapResult.OverlapX;
                hardPushbackNorms[retCnt].Y = overlapResult.OverlapY;

                effPushback.X += pushbackX;
                effPushback.Y += pushbackY;
                retCnt++;
            }
            return retCnt;
        }

        public static float InvSqrt32(float x) {
            float xhalf = 0.5f * x;
            int i = BitConverter.SingleToInt32Bits(x);
            i = 0x5f3759df - (i >> 1);
            x = BitConverter.Int32BitsToSingle(i);
            x = x * (1.5f - xhalf * x * x);
            return x;
        }

        public static double InvSqrt64(double x) {
            double xhalf = 0.5 * x;
            long i = BitConverter.DoubleToInt64Bits(x);
            i = 0x5fe6eb50c7b537a9 - (i >> 1);
            x = BitConverter.Int64BitsToDouble(i);
            x = x * (1.5 - xhalf * x * x);
            return x;
        }

        public static bool isPolygonPairOverlapped(ConvexPolygon a, ConvexPolygon b, ref SatResult result) {
            int aCnt = a.Points.Cnt;
            int bCnt = b.Points.Cnt;
            // Single point case
            if (1 == aCnt && 1 == bCnt) {
                result.OverlapMag = 0;
                Vector? aPoint = a.GetPointByOffset(0);
                Vector? bPoint = b.GetPointByOffset(0);
                return null != aPoint && null != bPoint && aPoint.X == bPoint.X && aPoint.Y == bPoint.Y;
            }

            if (1 < aCnt) {
                // Deliberately using "Points" instead of "SATAxes" to avoid unnecessary heap memory alloc
                for (int i = 0; i < aCnt; i++) {
                    Vector? u = a.GetPointByOffset(i);
                    if (null == u) {
                        throw new ArgumentNullException("Getting a null point u from polygon a!");
                    }
                    Vector? v = a.GetPointByOffset(0);
                    if (i != aCnt - 1) {
                        v = a.GetPointByOffset(i + 1);
                    }
                    if (null == v) {
                        throw new ArgumentNullException("Getting a null point v from polygon a!");
                    }
                    float dx = v.X - u.X;
                    float dy = v.Y - u.Y;
                    float invSqrtForAxis = InvSqrt32(dx * dx + dy * dy);
                    dx *= invSqrtForAxis;
                    dy *= invSqrtForAxis;
                    if (isPolygonPairSeparatedByDir(a, b, dx, dy, ref result)) {
                        return false;
                    }
                }
            }

            if (1 < bCnt) {
                for (int i = 0; i < bCnt; i++) {
                    Vector? u = b.GetPointByOffset(i);
                    if (null == u) {
                        throw new ArgumentNullException("Getting a null point u from polygon b!");
                    }
                    Vector? v = b.GetPointByOffset(0);
                    if (i != bCnt - 1) {
                        v = b.GetPointByOffset(i + 1);
                    }
                    if (null == v) {
                        throw new ArgumentNullException("Getting a null point v from polygon b!");
                    }
                    float dx = v.X - u.X;
                    float dy = v.Y - u.Y;
                    float invSqrtForAxis = InvSqrt32(dx * dx + dy * dy);
                    dx *= invSqrtForAxis;
                    dy *= invSqrtForAxis;
                    if (isPolygonPairSeparatedByDir(a, b, dx, dy, ref result)) {
                        return false;
                    }
                }
            }

            return true;
        }

        public static bool isPolygonPairSeparatedByDir(ConvexPolygon a, ConvexPolygon b, float axisX, float axisY, ref SatResult result) {
            /*
				[WARNING] This function is deliberately made private, it shouldn't be used alone (i.e. not along the norms of a polygon), otherwise the pushbacks calculated would be meaningless.

				Consider the following example
				a: {
					anchor: [1337.19 1696.74]
					points: [[0 0] [24 0] [24 24] [0 24]]
				},
				b: {
					anchor: [1277.72 1570.56]
					points: [[642.57 319.16] [0 319.16] [5.73 0] [643.75 0.90]]
				}

				e = (-2.98, 1.49).Unit()
			*/

            float aStart = MAX_FLOAT32;
            float aEnd = -MAX_FLOAT32;
            float bStart = MAX_FLOAT32;
            float bEnd = -MAX_FLOAT32;
            for (int i = 0; i < a.Points.Cnt; i++) {
                Vector? p = a.GetPointByOffset(i);
                if (null == p) {
                    throw new ArgumentNullException("Getting a null point from polygon a!");
                }
                float dot = (p.X + a.X) * axisX + (p.Y + a.Y) * axisY;

                if (aStart > dot) {
                    aStart = dot;
                }

                if (aEnd < dot) {
                    aEnd = dot;
                }
            }

            for (int i = 0; i < b.Points.Cnt; i++) {
                Vector? p = b.GetPointByOffset(i);
                if (null == p) {
                    throw new ArgumentNullException("Getting a null point from polygon b!");
                }
                float dot = (p.X + b.X) * axisX + (p.Y + b.Y) * axisY;

                if (bStart > dot) {
                    bStart = dot;
                }

                if (bEnd < dot) {
                    bEnd = dot;
                }
            }

            if (aStart > bEnd || aEnd < bStart) {
                // Separated by unit vector (axisX, axisY)
                return true;
            }

            float overlapProjected = 0;

            if (aStart < bStart) {
                result.AContainedInB = false;

                if (aEnd < bEnd) {
                    overlapProjected = aEnd - bStart;
                    result.BContainedInA = false;
                } else {
                    float option1 = aEnd - bStart;
                    float option2 = bEnd - aStart;
                    if (option1 < option2) {
                        overlapProjected = option1;
                    } else {
                        overlapProjected = -option2;
                    }
                }
            } else {
                result.BContainedInA = false;

                if (aEnd > bEnd) {
                    overlapProjected = aStart - bEnd;
                    result.AContainedInB = false;
                } else {
                    float option1 = aEnd - bStart;
                    float option2 = bEnd - aStart;
                    if (option1 < option2) {
                        overlapProjected = option1;
                    } else {
                        overlapProjected = -option2;
                    }
                }
            }

            float currentOverlap = result.OverlapMag;
            float absoluteOverlap = overlapProjected;
            if (overlapProjected < 0) {
                absoluteOverlap = -overlapProjected;
            }

            if ((0 == result.AxisX && 0 == result.AxisY) || (currentOverlap > absoluteOverlap)) {
                float sign = 1;
                if (overlapProjected < 0) {
                    sign = -1;
                }

                result.OverlapMag = absoluteOverlap;
                result.OverlapX = axisX * sign;
                result.OverlapY = axisY * sign;
            }

            result.AxisX = axisX;
            result.AxisY = axisY;

            // the specified unit vector (axisX, axisY) doesn't separate "a" and "b", overlap result is generated
            return false;
        }

        public static (int, int) PolygonColliderCtrToVirtualGridPos(float wx, float wy) {
            // [WARNING] Introduces loss of precision!
            // In JavaScript floating numbers suffer from seemingly non-deterministic arithmetics, and even if certain libs solved this issue by approaches such as fixed-point-number, they might not be used in other libs -- e.g. the "collision libs" we're interested in -- thus couldn't kill all pains.
            int vx = (int)(Math.Round(wx * COLLISION_SPACE_TO_VIRTUAL_GRID_RATIO));
            int vy = (int)(Math.Round(wy * COLLISION_SPACE_TO_VIRTUAL_GRID_RATIO));
            return (vx, vy);
        }

        public static (float, float) VirtualGridToPolygonColliderCtr(int vx, int vy) {
            // No loss of precision
            float wx = (float)(vx) * VIRTUAL_GRID_TO_COLLISION_SPACE_RATIO;
            float wy = (float)(vy) * VIRTUAL_GRID_TO_COLLISION_SPACE_RATIO;
            return (wx, wy);
        }

        public static (float, float) PolygonColliderCtrToBL(float wx, float wy, float halfBoundingW, float halfBoundingH, float topPadding, float bottomPadding, float leftPadding, float rightPadding, float collisionSpaceOffsetX, float collisionSpaceOffsetY) {
            return (wx - halfBoundingW - leftPadding + collisionSpaceOffsetX, wy - halfBoundingH - bottomPadding + collisionSpaceOffsetY);
        }

        public static (float, float) PolygonColliderBLToCtr(float cx, float cy, float halfBoundingW, float halfBoundingH, float topPadding, float bottomPadding, float leftPadding, float rightPadding, float collisionSpaceOffsetX, float collisionSpaceOffsetY) {
            return (cx + halfBoundingW + leftPadding - collisionSpaceOffsetX, cy + halfBoundingH + bottomPadding - collisionSpaceOffsetY);
        }

        public static (int, int) PolygonColliderBLToVirtualGridPos(float cx, float cy, float halfBoundingW, float halfBoundingH, float topPadding, float bottomPadding, float leftPadding, float rightPadding, float collisionSpaceOffsetX, float collisionSpaceOffsetY) {
            var (wx, wy) = PolygonColliderBLToCtr(cx, cy, halfBoundingW, halfBoundingH, topPadding, bottomPadding, leftPadding, rightPadding, collisionSpaceOffsetX, collisionSpaceOffsetY);
            return PolygonColliderCtrToVirtualGridPos(wx, wy);
        }

        public static (float, float) VirtualGridToPolygonColliderBLPos(int vx, int vy, float halfBoundingW, float halfBoundingH, float topPadding, float bottomPadding, float leftPadding, float rightPadding, float collisionSpaceOffsetX, float collisionSpaceOffsetY) {
            var (wx, wy) = VirtualGridToPolygonColliderCtr(vx, vy);
            return PolygonColliderCtrToBL(wx, wy, halfBoundingW, halfBoundingH, topPadding, bottomPadding, leftPadding, rightPadding, collisionSpaceOffsetX, collisionSpaceOffsetY);
        }
    }
}


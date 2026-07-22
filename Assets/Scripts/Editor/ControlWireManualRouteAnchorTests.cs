using System;
using System.Collections.Generic;
using System.Reflection;
using ElectricalSim.Core;
using UnityEditor;
using UnityEngine;

namespace ElectricalSim.Editor
{
    public static class ControlWireManualRouteAnchorTests
    {
        private const float Epsilon = 0.01f;

        [MenuItem("Tools/Tests/Run Control Wire Manual Route Anchor Tests")]
        public static void RunTests()
        {
            AssertAnchorsAndOrthogonality(
                "one waypoint",
                new List<Vector2>
                {
                    new Vector2(0f, 0f),
                    new Vector2(120f, 0f),
                    new Vector2(120f, 80f),
                    new Vector2(220f, 80f)
                },
                new Vector2(24f, 36f),
                new Vector2(264f, 116f));

            AssertAnchorsAndOrthogonality(
                "five waypoints",
                new List<Vector2>
                {
                    new Vector2(0f, 0f),
                    new Vector2(72f, 0f),
                    new Vector2(72f, 48f),
                    new Vector2(132f, 48f),
                    new Vector2(132f, 126f),
                    new Vector2(210f, 126f),
                    new Vector2(210f, 180f),
                    new Vector2(300f, 180f)
                },
                new Vector2(-30f, 24f),
                new Vector2(336f, 222f));

            Debug.Log("Control wire manual route anchor tests: 2/2 passed.");
        }

        private static void AssertAnchorsAndOrthogonality(
            string name,
            List<Vector2> persisted,
            Vector2 currentStart,
            Vector2 currentEnd)
        {
            var original = new List<Vector2>(persisted);
            var route = Build(currentStart, currentEnd, persisted, true);
            if (route.Count < 2)
            {
                throw new InvalidOperationException(name + ": route has fewer than two points.");
            }

            AssertApproximately(name + " start", currentStart, route[0]);
            AssertApproximately(name + " end", currentEnd, route[route.Count - 1]);
            for (var i = 0; i < route.Count - 1; i++)
            {
                var delta = route[i + 1] - route[i];
                if (delta.sqrMagnitude <= Epsilon * Epsilon)
                {
                    throw new InvalidOperationException(name + ": contains a zero-length segment.");
                }

                if (Mathf.Abs(delta.x) > Epsilon && Mathf.Abs(delta.y) > Epsilon)
                {
                    throw new InvalidOperationException(name + ": contains a diagonal segment.");
                }
            }

            if (persisted.Count != original.Count)
            {
                throw new InvalidOperationException(name + ": mutated the persisted route length.");
            }

            for (var i = 0; i < persisted.Count; i++)
            {
                AssertApproximately(name + " persisted point " + i, original[i], persisted[i]);
            }
        }

        private static List<Vector2> Build(Vector2 start, Vector2 end, IReadOnlyList<Vector2> persisted, bool fallbackHorizontal)
        {
            var method = typeof(WireView).GetMethod(
                "BuildAnchoredManualRoute",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                throw new InvalidOperationException("WireView.BuildAnchoredManualRoute was not found.");
            }

            return (List<Vector2>)method.Invoke(null, new object[] { start, end, persisted, fallbackHorizontal });
        }

        private static void AssertApproximately(string name, Vector2 expected, Vector2 actual)
        {
            if ((expected - actual).sqrMagnitude > Epsilon * Epsilon)
            {
                throw new InvalidOperationException(name + ": expected " + expected + ", got " + actual + ".");
            }
        }
    }
}

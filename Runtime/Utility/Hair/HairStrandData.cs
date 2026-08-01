using System;
using UnityEngine;

namespace VividRP.Runtime
{
    [Serializable]
    public struct HairStrandPoint
    {
        public Vector3 Position;
        public float Radius;
        public Vector2 UV;

        public HairStrandPoint(Vector3 position, float radius, Vector2 uv)
        {
            Position = position;
            Radius = radius;
            UV = uv;
        }
    }

    [Serializable]
    public struct HairStrandSegment
    {
        public HairStrandPoint Start;
        public HairStrandPoint End;

        public HairStrandSegment(HairStrandPoint start, HairStrandPoint end)
        {
            Start = start;
            End = end;
        }
    }
}

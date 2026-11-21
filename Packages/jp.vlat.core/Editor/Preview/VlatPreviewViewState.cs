using UnityEngine;

namespace VLat.Editor.Authoring
{
    internal enum VlatPreviewView
    {
        Main,
        SubA,
        SubB,
    }

    internal sealed class VlatPreviewViewState
    {
        private const float MinimumDistance = 0.05f;
        private const float MaximumDistance = 100f;
        private readonly float _defaultYaw;
        private float _defaultDistance;

        internal VlatPreviewViewState(float defaultYaw)
        {
            _defaultYaw = defaultYaw;
            _defaultDistance = 2f;
            Reset();
        }

        internal float Yaw { get; private set; }
        internal float Pitch { get; private set; }
        internal float Distance { get; private set; }
        internal Vector2 Pan { get; private set; }

        internal void SetFramingDistance(float distance)
        {
            _defaultDistance = Mathf.Clamp(distance, MinimumDistance, MaximumDistance);
            Reset();
        }

        internal void SetAngles(float yaw, float pitch)
        {
            Yaw = NormalizeYaw(yaw);
            Pitch = Mathf.Clamp(pitch, -89f, 89f);
        }

        internal void Orbit(Vector2 delta)
        {
            SetAngles(Yaw + (delta.x * 0.35f), Pitch - (delta.y * 0.35f));
        }

        internal void PanBy(Vector2 delta)
        {
            var scale = Distance * 0.0015f;
            Pan += new Vector2(-delta.x * scale, delta.y * scale);
        }

        internal void Zoom(float wheelDelta)
        {
            Distance = Mathf.Clamp(
                Distance * Mathf.Exp(wheelDelta * 0.08f),
                MinimumDistance,
                MaximumDistance);
        }

        internal void ClearPan() => Pan = Vector2.zero;

        internal void Reset()
        {
            Yaw = _defaultYaw;
            Pitch = 0f;
            Distance = _defaultDistance;
            Pan = Vector2.zero;
        }

        private static float NormalizeYaw(float yaw)
        {
            return Mathf.Repeat(yaw + 180f, 360f) - 180f;
        }
    }
}

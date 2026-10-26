using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Aquarium
{
    /// <summary>
    /// One physical screen of the installation: a rectangle in world space plus the camera that renders
    /// the view "through" it. The camera uses a generalised off-axis perspective projection (Kooima)
    /// from the shared eye point, so adjacent screens join without seams or perspective breaks.
    /// </summary>
    public sealed class ProjectionScreen : MonoBehaviour
    {
        public ScreenId Id { get; private set; }
        public Camera Camera { get; private set; }
        public RippleSurface Ripple { get; set; }
        public int DisplayIndex { get; private set; }
        public int GlassLayer { get; private set; }

        /// <summary>Screen corners as seen by the viewer: bottom-left, bottom-right, top-left (world space).</summary>
        public Vector3 BottomLeft { get; private set; }
        public Vector3 BottomRight { get; private set; }
        public Vector3 TopLeft { get; private set; }

        public Vector3 Right => (BottomRight - BottomLeft).normalized;
        public Vector3 Up => (TopLeft - BottomLeft).normalized;
        /// <summary>Unit normal pointing from the screen towards the viewer (into the tunnel).</summary>
        public Vector3 Normal => Vector3.Cross(Up, Right).normalized;
        public float Width => (BottomRight - BottomLeft).magnitude;
        public float Height => (TopLeft - BottomLeft).magnitude;
        public float Aspect => Width / Mathf.Max(Height, 1e-4f);

        public void Initialize(ScreenId id, Vector3 bottomLeft, Vector3 bottomRight, Vector3 topLeft,
            int displayIndex, int glassLayer, int cullingMask, Color background, bool postProcessing, SmaaQuality aa)
        {
            Id = id;
            BottomLeft = bottomLeft;
            BottomRight = bottomRight;
            TopLeft = topLeft;
            DisplayIndex = displayIndex;
            GlassLayer = glassLayer;

            Camera = GetComponent<Camera>();
            if (Camera == null) Camera = gameObject.AddComponent<Camera>();
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = background;
            Camera.cullingMask = cullingMask;
            Camera.allowHDR = true;
            Camera.allowMSAA = false;
            Camera.depth = (int)id;
            Camera.targetDisplay = displayIndex;

            var data = Camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = postProcessing;
            // The ripple glass samples the opaque colour texture.
            data.requiresColorOption = CameraOverrideOption.On;
            data.requiresDepthOption = CameraOverrideOption.On;
            data.renderShadows = true;
            data.antialiasing = aa == SmaaQuality.Off ? AntialiasingMode.None : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = aa == SmaaQuality.High ? UnityEngine.Rendering.Universal.AntialiasingQuality.High : UnityEngine.Rendering.Universal.AntialiasingQuality.Medium;
            data.stopNaN = true;
        }

        /// <summary>Route this screen's camera to another display (0-based).</summary>
        public void SetDisplay(int displayIndex)
        {
            DisplayIndex = displayIndex;
            if (Camera != null) Camera.targetDisplay = displayIndex;
        }

        /// <summary>World point on the screen surface for normalised screen coordinates (0..1).</summary>
        public Vector3 PointAt(Vector2 uv)
        {
            return BottomLeft + (BottomRight - BottomLeft) * uv.x + (TopLeft - BottomLeft) * uv.y;
        }

        /// <summary>Recompute the camera pose and the asymmetric frustum for the given eye position.</summary>
        public void UpdateProjection(Vector3 eye, float farClip)
        {
            Vector3 vr = Right, vu = Up, vn = Normal;
            Vector3 va = BottomLeft - eye;
            Vector3 vb = BottomRight - eye;
            Vector3 vc = TopLeft - eye;

            float distance = -Vector3.Dot(va, vn); // eye -> screen plane
            if (distance < 0.01f) distance = 0.01f;

            // Near plane just in front of the glass: nothing inside the tunnel can be drawn.
            float near = distance * 0.98f;
            float scale = near / distance;
            float l = Vector3.Dot(vr, va) * scale;
            float r = Vector3.Dot(vr, vb) * scale;
            float b = Vector3.Dot(vu, va) * scale;
            float t = Vector3.Dot(vu, vc) * scale;

            transform.SetPositionAndRotation(eye, Quaternion.LookRotation(-vn, vu));
            Camera.nearClipPlane = near;
            Camera.farClipPlane = farClip;
            Camera.projectionMatrix = Matrix4x4.Frustum(l, r, b, t, near, farClip);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Id == ScreenId.Floor ? new Color(0.3f, 1f, 0.6f) : new Color(0.3f, 0.7f, 1f);
            Vector3 tr = TopLeft + (BottomRight - BottomLeft);
            Gizmos.DrawLine(BottomLeft, BottomRight);
            Gizmos.DrawLine(BottomRight, tr);
            Gizmos.DrawLine(tr, TopLeft);
            Gizmos.DrawLine(TopLeft, BottomLeft);
        }
    }
}

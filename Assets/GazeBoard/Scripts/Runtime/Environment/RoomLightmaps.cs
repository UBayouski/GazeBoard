using UnityEngine;

namespace GazeBoard.Environment
{
    /// <summary>
    /// Remembers which part of the baked lightmap each room renderer uses and applies it again
    /// whenever the room is switched on. The room is inactive when the scene loads (passthrough
    /// is the default view), and this makes its baked lighting independent of that.
    /// Filled in by the Calm Room bake command.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomLightmaps : MonoBehaviour
    {
        [SerializeField] private Renderer[] _renderers = new Renderer[0];
        [SerializeField] private int[] _indices = new int[0];
        [SerializeField] private Vector4[] _scaleOffsets = new Vector4[0];

        /// <summary>Reads the lightmap assignment of every child renderer. Call right after a bake.</summary>
        public void Capture()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);
            _indices = new int[_renderers.Length];
            _scaleOffsets = new Vector4[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                _indices[i] = _renderers[i].lightmapIndex;
                _scaleOffsets[i] = _renderers[i].lightmapScaleOffset;
            }
        }

        private void OnEnable()
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer r = _renderers[i];
                if (r == null || i >= _indices.Length || _indices[i] < 0 || _indices[i] >= 65534) continue;
                r.lightmapIndex = _indices[i];
                r.lightmapScaleOffset = _scaleOffsets[i];
            }
        }
    }
}

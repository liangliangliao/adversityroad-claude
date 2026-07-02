using UnityEngine;
using UnityEngine.EventSystems;

namespace AdversityRoad.Mobile
{
    /// <summary>屏幕右侧透明区域：拖动转镜头，写入 MobileInput.LookDelta。</summary>
    public class TouchLookArea : MonoBehaviour, IDragHandler
    {
        public float sensitivity = 0.15f;

        public void OnDrag(PointerEventData e)
        {
            MobileInput.LookDelta += e.delta * sensitivity;
        }
    }
}

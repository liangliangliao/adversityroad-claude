using UnityEngine;
using UnityEngine.EventSystems;

namespace AdversityRoad.Mobile
{
    /// <summary>虚拟按钮：按下/抬起转发到 MobileInput。</summary>
    public class VirtualButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public string buttonName = "Light";

        public void OnPointerDown(PointerEventData e) => MobileInput.Press(buttonName);
        public void OnPointerUp(PointerEventData e) => MobileInput.Release(buttonName);
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace AdversityRoad.UI
{
    /// <summary>通用属性条：绑定 Slider，平滑过渡。</summary>
    public class StatBar : MonoBehaviour
    {
        public Slider slider;
        public float smoothSpeed = 5f;
        float _target = 1f;

        /// <summary>上一次设置的比例（供 HUD 判断数值升降）。</summary>
        public float LastRatio => _target;

        public void SetValue(float cur, float max) => _target = max > 0 ? cur / max : 0;

        void Update()
        {
            if (slider != null)
                slider.value = Mathf.Lerp(slider.value, _target, smoothSpeed * Time.deltaTime);
        }
    }
}

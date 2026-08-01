using UnityEngine;
using UnityEngine.UI;

namespace AdversityRoad.UI
{
    /// <summary>通用属性条：绑定 Slider，平滑过渡。
    /// 可选挂一个数值读数（如 HP 的「168 / 200」）——只有一截彩色长度而没有数字时，
    /// 玩家只能靠目测猜自己还剩多少；血量低、条又短的时候更是完全读不出来。</summary>
    public class StatBar : MonoBehaviour
    {
        public Slider slider;
        public Text valueText;          // 可选：数值读数
        public float smoothSpeed = 5f;
        float _target = 1f;

        /// <summary>上一次设置的比例（供 HUD 判断数值升降）。</summary>
        public float LastRatio => _target;

        public void SetValue(float cur, float max)
        {
            _target = max > 0 ? cur / max : 0;
            if (valueText != null)
                valueText.text = Mathf.CeilToInt(Mathf.Max(0f, cur)) + " / " + Mathf.CeilToInt(max);
        }

        void Update()
        {
            if (slider != null)
                slider.value = Mathf.Lerp(slider.value, _target, smoothSpeed * Time.deltaTime);
        }
    }
}

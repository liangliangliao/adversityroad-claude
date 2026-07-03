using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Personalization;

namespace AdversityRoad.AI
{
    /// <summary>
    /// 敌人台词气泡：头顶 3D 文字 + 底部字幕。
    /// 心理攻击时喊话，追击中周期性低语——语言层面的实时心理压迫。
    /// </summary>
    public class EnemyDialogue : MonoBehaviour
    {
        public float bubbleHeight = 3.4f;   // 抬高，避免文字盖住角色
        public string displayName = "敌人";

        TextMesh _tm;
        float _hideAt;

        void Awake()
        {
            var go = new GameObject("SpeechBubble");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0, bubbleHeight, 0);
            _tm = go.AddComponent<TextMesh>();
            _tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _tm.fontSize = 48;
            _tm.characterSize = 0.028f;     // 缩小气泡：不再遮挡血条与角色
            _tm.anchor = TextAnchor.LowerCenter;
            _tm.alignment = TextAlignment.Center;
            _tm.color = new Color(1f, 0.85f, 0.85f);
            var r = go.GetComponent<MeshRenderer>();
            if (_tm.font != null) r.material = _tm.font.material;
            _tm.text = "";
        }

        void LateUpdate()
        {
            if (_tm == null) return;
            if (Camera.main != null)
                _tm.transform.rotation = Quaternion.LookRotation(
                    _tm.transform.position - Camera.main.transform.position);
            if (_tm.text.Length > 0 && Time.time > _hideAt) _tm.text = "";
        }

        /// <summary>喊出一句针对弱点轴的恶意低语（气泡 + 底部字幕）。</summary>
        public void Taunt(WeaknessAxis axis, string zoneId, bool major)
        {
            string line = DialogueLibrary.GetTaunt(axis, zoneId);
            Show(line, major ? 3.5f : 2.5f);
            if (major) GameEvents.RaiseSubtitle("『" + displayName + "』：" + line);
        }

        public void Show(string line, float duration)
        {
            if (_tm == null) return;
            _tm.text = Wrap(line, 12);
            _hideAt = Time.time + duration;
        }

        static string Wrap(string s, int perLine)
        {
            if (s.Length <= perLine) return s;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                sb.Append(s[i]);
                if ((i + 1) % perLine == 0 && i < s.Length - 1) sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}

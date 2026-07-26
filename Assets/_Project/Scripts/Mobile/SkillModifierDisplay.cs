using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AdversityRoad.Mobile
{
    /// <summary>
    /// 修饰键「术」的界面反馈：按住时把六个核心键的字面换成**实际装备的技能名**，
    /// 松开即还原。
    ///
    /// 这一层不是装饰，是修饰键方案能否成立的前提——
    /// 手柄上 L1+□ 之所以人人会用，是因为按住 L1 屏幕会亮出四个技能图标；
    /// 若按住「术」之后六个键还写着"拳剑重跳闪挡"，玩家永远不会知道自己能放技能。
    ///
    /// 技能名从 SkillExecutor.equippedSkills 现取（而不是写死"定气还火盾收"），
    /// 换了装备技能，按键提示自动跟着变。
    /// </summary>
    public class SkillModifierDisplay : MonoBehaviour
    {
        /// <summary>
        /// 参与映射的核心键。**技能槽序号不在这里写死**——从 MobileInput.MappedSkill()
        /// 现查（"Skill3" → 槽 2），保证按键提示与真正会释放的技能永远是同一份映射。
        /// 两处各存一份顺序，是那种改了一处、另一处静默错位的隐患。
        /// </summary>
        static readonly string[] CoreButtons =
            { "Light", "Kick", "Heavy", "Jump", "Dodge", "Guard" };

        /// <summary>"Skill3" → 2；无映射或格式不符返回 -1。</summary>
        static int SlotOf(string button)
        {
            string mapped = MobileInput.MappedSkill(button);
            if (string.IsNullOrEmpty(mapped) || !mapped.StartsWith("Skill")) return -1;
            return int.TryParse(mapped.Substring(5), out int n) ? n - 1 : -1;
        }

        class Slot
        {
            public Text label;
            public Image image;
            public string baseText;
            public Color baseColor;
            public int skillIndex;
        }

        readonly List<Slot> _slots = new List<Slot>();
        Combat.SkillExecutor _exec;
        Image _artImage;
        Color _artBase;
        bool _shown;
        bool _bound;

        void Start() => Bind();

        void Bind()
        {
            foreach (string btn in CoreButtons)
            {
                int slot = SlotOf(btn);
                if (slot < 0) continue;
                var t = transform.Find("Btn_" + btn);
                if (t == null) continue;
                var label = t.GetComponentInChildren<Text>();
                var img = t.GetComponent<Image>();
                if (label == null || img == null) continue;
                _slots.Add(new Slot
                {
                    label = label, image = img,
                    baseText = label.text, baseColor = img.color,
                    skillIndex = slot,
                });
            }
            var art = transform.Find("Btn_" + MobileInput.Modifier);
            if (art != null) { _artImage = art.GetComponent<Image>(); if (_artImage != null) _artBase = _artImage.color; }
            _bound = _slots.Count > 0;
        }

        void Update()
        {
            // 按钮由 MobileControls.Build() 生成，执行顺序不定，Start 可能早于生成——惰性补绑
            if (!_bound) { Bind(); if (!_bound) return; }

            bool held = MobileInput.ModifierHeld;

            // 「术」自身随点选剩余时间收亮：玩家能看见这个模式还剩多久，
            // 而不是"我点了术然后忘了，过一会儿按拳莫名其妙放了个技能"
            if (_artImage != null)
            {
                float t01 = MobileInput.ModifierLatch01;
                _artImage.color = held
                    ? new Color(Mathf.Lerp(_artBase.r, 1f, 0.35f * t01),
                                Mathf.Lerp(_artBase.g, 1f, 0.35f * t01),
                                Mathf.Lerp(_artBase.b, 1f, 0.35f * t01),
                                Mathf.Lerp(_artBase.a, 1f, 0.5f * t01))
                    : _artBase;
            }

            if (held == _shown) return;
            _shown = held;

            if (_exec == null) _exec = FindObjectOfType<Combat.SkillExecutor>();
            var skills = _exec != null ? _exec.equippedSkills : null;

            foreach (var s in _slots)
            {
                if (held)
                {
                    bool has = skills != null && s.skillIndex < skills.Count
                               && skills[s.skillIndex] != null;
                    s.label.text = has ? ShortName(skills[s.skillIndex].displayName) : "—";
                    // 未装备该槽的键压暗，明确"这个位置现在按了没用"
                    s.image.color = has ? SkillTint(s.baseColor) : Dim(s.baseColor);
                }
                else
                {
                    s.label.text = s.baseText;
                    s.image.color = s.baseColor;
                }
            }
        }

        /// <summary>技能名取首字（定心·四象归一 → 定）：按钮只有一个字的位置。</summary>
        static string ShortName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return "—";
            int cut = displayName.IndexOfAny(new[] { '·', '「', '（', '(' });
            string head = cut > 0 ? displayName.Substring(0, cut) : displayName;
            return head.Length <= 2 ? head : head.Substring(0, 1);
        }

        /// <summary>技能态配色：统一偏向「术」的蓝紫，让"整排键换了身份"一眼可见。</summary>
        static Color SkillTint(Color b) =>
            new Color(Mathf.Lerp(b.r, 0.45f, 0.65f), Mathf.Lerp(b.g, 0.6f, 0.65f),
                      Mathf.Lerp(b.b, 0.98f, 0.65f), Mathf.Max(b.a, 0.85f));

        static Color Dim(Color b) => new Color(b.r * 0.35f, b.g * 0.35f, b.b * 0.35f, b.a * 0.6f);
    }
}

using UnityEngine;
using UnityEngine.UI;
using AdversityRoad.Core;

namespace AdversityRoad.UI
{
    /// <summary>
    /// 敌人头顶状态条（世界空间画布）：名字+等级、血条、韧性条、情绪标签。
    /// </summary>
    public class EnemyStatusBar : MonoBehaviour
    {
        Image _hpFill;
        Image _postureFill;
        Text _nameText;
        Text _emotionText;
        Transform _root;

        public static EnemyStatusBar Create(Transform owner, string displayName, float height)
        {
            var barGo = new GameObject("StatusBar");
            barGo.transform.SetParent(owner, false);
            barGo.transform.localPosition = new Vector3(0, height, 0);
            var bar = barGo.AddComponent<EnemyStatusBar>();
            bar.Build(displayName);
            return bar;
        }

        void Build(string displayName)
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var crt = canvasGo.GetComponent<RectTransform>();
            crt.sizeDelta = new Vector2(220, 64);
            canvasGo.transform.localScale = Vector3.one * 0.012f;
            _root = canvasGo.transform;

            _nameText = UiUtil.MakeText(_root, "Name", displayName, 24, TextAnchor.MiddleCenter, Color.white);
            UiUtil.SetRect(_nameText, new Vector2(0.5f, 1f), new Vector2(0, -12), new Vector2(220, 26));

            _emotionText = UiUtil.MakeText(_root, "Emotion", "", 18, TextAnchor.MiddleCenter,
                new Color(1f, 0.75f, 0.5f));
            UiUtil.SetRect(_emotionText, new Vector2(0.5f, 1f), new Vector2(0, -34), new Vector2(220, 20));

            _hpFill = MakeBar(new Vector2(0, -50), new Color(0.85f, 0.2f, 0.2f), out _);
            _postureFill = MakeBar(new Vector2(0, -60), new Color(0.95f, 0.8f, 0.3f), out _);
        }

        Image MakeBar(Vector2 pos, Color color, out Image bg)
        {
            var bgGo = new GameObject("BarBg", typeof(Image));
            bgGo.transform.SetParent(_root, false);
            UiUtil.SetRect(bgGo.GetComponent<Image>(), new Vector2(0.5f, 1f), pos, new Vector2(180, 8));
            bg = bgGo.GetComponent<Image>();
            bg.color = new Color(0, 0, 0, 0.6f);
            bg.raycastTarget = false;

            var fillGo = new GameObject("Fill", typeof(Image));
            fillGo.transform.SetParent(bgGo.transform, false);
            var frt = fillGo.GetComponent<RectTransform>();
            frt.anchorMin = new Vector2(0, 0);
            frt.anchorMax = new Vector2(1, 1);
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;
            frt.pivot = new Vector2(0, 0.5f);
            var fill = fillGo.GetComponent<Image>();
            fill.color = color;
            fill.raycastTarget = false;
            return fill;
        }

        public void SetHealth(float cur, float max)
        {
            if (_hpFill != null)
                _hpFill.rectTransform.anchorMax = new Vector2(max > 0 ? Mathf.Clamp01(cur / max) : 0, 1);
        }

        public void SetPosture(float cur, float max)
        {
            if (_postureFill != null)
                _postureFill.rectTransform.anchorMax = new Vector2(max > 0 ? Mathf.Clamp01(cur / max) : 0, 1);
        }

        public void SetEmotion(string emotion)
        {
            if (_emotionText != null && _emotionText.text != emotion) _emotionText.text = emotion;
        }

        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (Camera.main != null && _root != null)
                _root.rotation = Quaternion.LookRotation(_root.position - Camera.main.transform.position);
        }
    }
}

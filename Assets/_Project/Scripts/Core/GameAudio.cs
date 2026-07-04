using UnityEngine;

namespace AdversityRoad.Core
{
    /// <summary>
    /// 程序化音效（零外部资产）：运行时用 PCM 合成一组打击/挥击/格挡/警示音，
    /// 通过一个 AudioSource 池 2D 播放。CombatFeedback 与受击逻辑统一走这里。
    /// 之前项目完全无声——这是补齐「命中/挥击/前摇有音效」的最小实现。
    /// </summary>
    public static class GameAudio
    {
        public enum Sfx { Swing, Hit, HeavyHit, Block, Parry, Alert, Hurt, Dodge, Death, Cast }

        static bool _ready;
        static AudioSource[] _pool;
        static int _next;
        static AudioClip[] _clips;
        const int SampleRate = 44100;

        public static float MasterVolume = 0.7f;

        static void Ensure()
        {
            if (_ready) return;
            _ready = true;

            var go = new GameObject("GameAudio");
            Object.DontDestroyOnLoad(go);
            _pool = new AudioSource[8];
            for (int i = 0; i < _pool.Length; i++)
            {
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;   // 2D：简单可靠，不依赖听者位置
                _pool[i] = s;
            }

            _clips = new AudioClip[System.Enum.GetValues(typeof(Sfx)).Length];
            _clips[(int)Sfx.Swing]    = Whoosh(0.16f, 900f, 260f, 0.35f);
            _clips[(int)Sfx.Hit]      = Impact(0.14f, 200f, 0.7f, 0.5f);
            _clips[(int)Sfx.HeavyHit] = Impact(0.28f, 95f, 1f, 0.85f);
            _clips[(int)Sfx.Block]    = Tone(0.12f, 520f, 0.35f, 0.5f);
            _clips[(int)Sfx.Parry]    = Tone(0.22f, 1250f, 0.3f, 0.55f);
            _clips[(int)Sfx.Alert]    = Beep(0.18f, 760f, 0.4f);
            _clips[(int)Sfx.Hurt]     = Impact(0.2f, 150f, 0.85f, 0.6f);
            _clips[(int)Sfx.Dodge]    = Whoosh(0.22f, 520f, 140f, 0.28f);
            _clips[(int)Sfx.Death]    = Impact(0.5f, 70f, 1f, 0.9f);
            _clips[(int)Sfx.Cast]     = Tone(0.3f, 340f, 0.25f, 0.5f);
        }

        public static void Play(Sfx s, float volume = 1f, float pitchJitter = 0.08f)
        {
            Ensure();
            var clip = _clips[(int)s];
            if (clip == null) return;
            var src = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            src.volume = Mathf.Clamp01(volume * MasterVolume);
            src.PlayOneShot(clip);
        }

        // ---------- 合成 ----------

        /// <summary>挥击风声：带通噪声，频率由高扫到低，快速衰减。</summary>
        static AudioClip Whoosh(float dur, float startHz, float endHz, float vol)
        {
            int n = Mathf.RoundToInt(SampleRate * dur);
            var data = new float[n];
            float prev = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float noise = Random.Range(-1f, 1f);
                // 单极点低通，截止随时间下移 → 风声由锐变闷
                float cut = Mathf.Lerp(startHz, endHz, t) / SampleRate;
                prev = Mathf.Lerp(prev, noise, Mathf.Clamp01(cut * 6f));
                float env = Mathf.Sin(t * Mathf.PI);            // 淡入淡出
                data[i] = prev * env * vol;
            }
            return Make(data, "sfx_whoosh");
        }

        /// <summary>打击闷响：低频正弦「咚」+ 起手噪声爆点，指数衰减。</summary>
        static AudioClip Impact(float dur, float hz, float thump, float vol)
        {
            int n = Mathf.RoundToInt(SampleRate * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Exp(-t * 9f);
                float body = Mathf.Sin(2f * Mathf.PI * hz * t * (1f - 0.3f * t)) * thump; // 略微下坠
                float crack = Random.Range(-1f, 1f) * Mathf.Exp(-t * 45f) * 0.6f;         // 起手脆响
                data[i] = (body + crack) * env * vol;
            }
            return Make(data, "sfx_impact");
        }

        /// <summary>金属/格挡音：双正弦 + 快衰减。</summary>
        static AudioClip Tone(float dur, float hz, float vol, float decay)
        {
            int n = Mathf.RoundToInt(SampleRate * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Exp(-t / decay);
                float s = Mathf.Sin(2f * Mathf.PI * hz * t)
                        + 0.5f * Mathf.Sin(2f * Mathf.PI * hz * 2.01f * t);
                data[i] = s * 0.5f * env * vol;
            }
            return Make(data, "sfx_tone");
        }

        /// <summary>警示提示音：方波感短鸣（前摇读招）。</summary>
        static AudioClip Beep(float dur, float hz, float vol)
        {
            int n = Mathf.RoundToInt(SampleRate * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Sin(t * Mathf.PI);
                float sq = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * hz * t)) * 0.5f;
                data[i] = sq * env * vol;
            }
            return Make(data, "sfx_beep");
        }

        static AudioClip Make(float[] data, string name)
        {
            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

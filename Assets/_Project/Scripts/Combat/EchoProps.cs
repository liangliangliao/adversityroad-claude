using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;

namespace AdversityRoad.Combat
{
    /// <summary>旧事回声馆的全局小状态：已归档展柜数（归档 3 座解锁终局镜面平台）。</summary>
    public static class EchoState
    {
        public const int ArchivesNeeded = 3;
        public static int Archived;

        public static void Reset() { Archived = 0; }
    }

    /// <summary>
    /// 旧事展柜：靠近先触发一段旧我回声（反刍上升），随后站在旁边完成「归档」——
    /// 看见事实 → 命名感受 → 提取边界 → 转化行动。归档后展柜熄灭，旧事不再循环播放。
    /// 不能击碎（旧事不是砸掉的，是整理好的）。
    /// </summary>
    public class EchoDisplayCase : MonoBehaviour
    {
        public string memoryLabel = "失败记录";
        public float interactRange = 3.2f;
        public float archiveTime = 2.5f;

        static readonly string[] ArchiveSteps =
            { "看见事实……", "命名感受……", "提取边界……", "转化行动。" };

        bool _echoTriggered, _archived;
        float _progress;
        int _stepShown = -1;
        MeshRenderer _glow;

        /// <summary>展柜顶部的回声光（归档后熄灭），建造时注入。</summary>
        public void SetGlow(MeshRenderer glow) => _glow = glow;

        void Update()
        {
            if (_archived) return;
            var p = FindObjectOfType<PlayerController>();
            if (p == null) return;
            float dist = Vector3.Distance(transform.position, p.transform.position);

            if (dist > interactRange)
            {
                // 走远归档中断，进度缓慢回退（旧事整理需要停下来面对）
                if (_progress > 0) _progress = Mathf.Max(0, _progress - Time.deltaTime * 0.6f);
                return;
            }

            // 第一次靠近：旧我回声炸开（反刍上升）——这就是没归档的旧事的样子
            if (!_echoTriggered)
            {
                _echoTriggered = true;
                p.Stats.AddRumination(10f);
                GameAudio.Play(GameAudio.Sfx.Hurt, 0.5f);
                GameEvents.RaiseSubtitle("旧事回声「" + memoryLabel +
                    "」开始循环播放——站在展柜前完成归档，让它安静下来。");
                return;
            }

            // 站定归档：分四步推进（复盘四栏的空间化）
            _progress += Time.deltaTime;
            int step = Mathf.Min(ArchiveSteps.Length - 1,
                Mathf.FloorToInt(_progress / archiveTime * ArchiveSteps.Length));
            if (step != _stepShown)
            {
                _stepShown = step;
                GameEvents.RaiseSubtitle("归档「" + memoryLabel + "」：" + ArchiveSteps[step]);
            }
            if (_progress >= archiveTime) Archive(p);
        }

        void Archive(PlayerController p)
        {
            _archived = true;
            EchoState.Archived++;
            p.Stats.ReduceRumination(18f);
            p.Stats.RestoreAxis(Personalization.WeaknessAxis.FailureFear, 15f);
            if (_glow != null) _glow.enabled = false;
            CombatFeedback.HitSpark(transform.position + Vector3.up * 1.2f,
                new Color(0.7f, 0.85f, 1f), 8);
            GameAudio.Play(GameAudio.Sfx.Parry, 0.8f);
            GameEvents.RaiseSubtitle(EchoState.Archived >= EchoState.ArchivesNeeded
                ? "「" + memoryLabel + "」已归档（" + EchoState.Archived + "/" +
                  EchoState.ArchivesNeeded + "）——回声止息，终局镜面平台的门开了。"
                : "「" + memoryLabel + "」已归档（" + EchoState.Archived + "/" +
                  EchoState.ArchivesNeeded + "）——事实留下，回放停止。");
        }
    }

    /// <summary>
    /// 终局大门：旧事归档满 3 座自动升起——先整理旧事，才见旧我。
    /// </summary>
    public class EchoBossGate : MonoBehaviour
    {
        bool _opened;
        float _riseT;

        void Update()
        {
            if (!_opened)
            {
                if (EchoState.Archived >= EchoState.ArchivesNeeded)
                {
                    _opened = true;
                    GameAudio.Play(GameAudio.Sfx.HeavyHit, 0.6f);
                }
                return;
            }
            // 大门缓缓沉入地面
            _riseT += Time.deltaTime;
            transform.position += Vector3.down * 2.2f * Time.deltaTime;
            if (_riseT > 3f) Destroy(gameObject);
        }
    }

    /// <summary>
    /// 整合圆环（旧我终局第四阶段）：站入圆环持续数秒完成「旧我整合式」——
    /// 不是杀死旧我，而是更新它。由 OldSelfBoss 在第四阶段生成。
    /// </summary>
    public class IntegrationCircle : MonoBehaviour
    {
        public float integrateTime = 3f;
        public System.Action onIntegrated;

        float _progress;
        int _stepShown = -1;
        bool _done;

        static readonly string[] Steps =
        {
            "旧事归档：过去发生过，但不是我的全部……",
            "目标钉稳：失败是事实，不是身份……",
            "旧我整合式：你曾经保护过我——",
        };

        void Update()
        {
            if (_done) return;
            var p = FindObjectOfType<PlayerController>();
            if (p == null) return;
            float dist = Vector3.Distance(transform.position, p.transform.position);
            if (dist > 3.2f)
            {
                if (_progress > 0) _progress = Mathf.Max(0, _progress - Time.deltaTime);
                return;
            }

            _progress += Time.deltaTime;
            int step = Mathf.Min(Steps.Length - 1,
                Mathf.FloorToInt(_progress / integrateTime * Steps.Length));
            if (step != _stepShown)
            {
                _stepShown = step;
                GameEvents.RaiseSubtitle(Steps[step]);
                GameAudio.Play(GameAudio.Sfx.Parry, 0.5f);
            }
            if (_progress >= integrateTime)
            {
                _done = true;
                onIntegrated?.Invoke();
            }
        }
    }

    /// <summary>
    /// 影子护卫：整合后的旧我——跟在玩家身后的半透明影子。
    /// 不是被消灭的敌人，而是被更新的旧模式；靠近时反刍消退更快。
    /// </summary>
    public class ShadowGuardian : MonoBehaviour
    {
        public float followDistance = 2.6f;
        public float moveSpeed = 6f;

        Transform _player;

        public static ShadowGuardian Spawn(Vector3 pos)
        {
            var root = new GameObject("ShadowGuardian");
            root.transform.position = pos;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0, 0.1f, 0);
            body.transform.localScale = new Vector3(0.85f, 0.95f, 0.85f);
            body.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.18f, 0.18f, 0.28f), 0.45f);

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0, 1.3f, 0);
            head.transform.localScale = Vector3.one * 0.48f;
            head.GetComponent<MeshRenderer>().sharedMaterial =
                CombatFeedback.EnergyMaterial(new Color(0.22f, 0.22f, 0.32f), 0.5f);

            return root.AddComponent<ShadowGuardian>();
        }

        void Update()
        {
            if (_player == null)
            {
                var p = FindObjectOfType<PlayerController>();
                if (p == null) return;
                _player = p.transform;
            }

            // 跟在玩家身后偏左的位置（护卫位）
            Vector3 want = _player.position - _player.forward * followDistance
                - _player.right * 0.9f;
            Vector3 to = want - transform.position;
            to.y = 0;
            if (to.magnitude > 0.3f)
                transform.position += to.normalized *
                    Mathf.Min(moveSpeed, to.magnitude * 2.5f) * Time.deltaTime;
            // 贴地
            if (Physics.Raycast(transform.position + Vector3.up * 3f, Vector3.down,
                    out RaycastHit hit, 20f))
                transform.position = new Vector3(transform.position.x,
                    hit.point.y + 1.0f, transform.position.z);
            Vector3 face = _player.position - transform.position; face.y = 0;
            if (face.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(face), 4f * Time.deltaTime);

            // 影子护卫在场：反刍消退加速（过去成了守护而非回放）
            var pc = _player.GetComponent<PlayerController>();
            if (pc != null && pc.Stats.rumination > 0)
                pc.Stats.ReduceRumination(0.8f * Time.deltaTime);
        }
    }
}

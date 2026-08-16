using UnityEngine;
using AdversityRoad.Core;
using AdversityRoad.Player;
using AdversityRoad.World;

namespace AdversityRoad.OpenWorld
{
    /// <summary>
    /// 家里的猫。
    ///
    /// 它不承担任何系统功能——不给 buff、不解锁东西、不推进任何进度。
    /// 这正是它存在的理由：住处是全游戏唯一没有敌人也没有倒计时的地方，
    /// 而一个真的有人住的家里，总该有个不为你的目标服务的活物。
    ///
    /// 行为：自己在屋里慢慢晃 → 玩家走近会凑过来 → 喂过之后一段时间黏着你，
    /// 吃饱了就回窝趴着。全部是程序化几何，和世界其它部分同一套构件。
    /// </summary>
    public class PetCat : MonoBehaviour
    {
        public Vector3 homeAt;          // 猫窝
        public float wanderRadius = 9f;

        Transform _player;
        Vector3 _target;
        float _nextPick;
        float _fedUntil = -99f;
        float _lastMeow = -99f;
        Transform _tail;
        float _bob;

        /// <summary>刚喂过：这段时间它会跟着你。</summary>
        public bool Fed => Time.time < _fedUntil;

        public static PetCat Create(WorldContext ctx, Vector3 at, Vector3 houseCenter)
        {
            var root = new GameObject("PetCat");
            root.transform.position = at + Vector3.up * 0.28f;

            var body = ZoneBuilder.Decoration(ctx, "CatBody", Vector3.zero, new Vector3(0.34f, 0.30f, 0.72f), Fur);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = Vector3.zero;

            var head = ZoneBuilder.Decoration(ctx, "CatHead", Vector3.zero, new Vector3(0.30f, 0.28f, 0.28f), Fur);
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0, 0.14f, 0.44f);

            for (int i = -1; i <= 1; i += 2)
            {
                var ear = ZoneBuilder.Decoration(ctx, "CatEar", Vector3.zero, new Vector3(0.09f, 0.14f, 0.06f), Fur);
                ear.transform.SetParent(root.transform, false);
                ear.transform.localPosition = new Vector3(i * 0.09f, 0.30f, 0.44f);

                var eye = ZoneBuilder.Decoration(ctx, "CatEye", Vector3.zero, new Vector3(0.06f, 0.06f, 0.03f),
                    new Color(0.35f, 0.85f, 0.55f));
                eye.transform.SetParent(root.transform, false);
                eye.transform.localPosition = new Vector3(i * 0.07f, 0.16f, 0.58f);
            }

            // 四条腿
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var leg = ZoneBuilder.Decoration(ctx, "CatLeg", Vector3.zero, new Vector3(0.09f, 0.26f, 0.09f), Fur);
                    leg.transform.SetParent(root.transform, false);
                    leg.transform.localPosition = new Vector3(sx * 0.12f, -0.26f, sz * 0.24f);
                }

            var tail = ZoneBuilder.Decoration(ctx, "CatTail", Vector3.zero, new Vector3(0.07f, 0.07f, 0.5f), Fur);
            tail.transform.SetParent(root.transform, false);
            tail.transform.localPosition = new Vector3(0, 0.14f, -0.46f);

            var c = root.AddComponent<PetCat>();
            c.homeAt = at;
            c._target = at;
            c._tail = tail.transform;
            return c;
        }

        static readonly Color Fur = new Color(0.66f, 0.60f, 0.54f);

        void Update()
        {
            if (_player == null)
            {
                var pc = FindObjectOfType<PlayerController>();
                if (pc != null) _player = pc.transform;
            }

            float dt = Time.deltaTime;
            bool follow = _player != null &&
                          (Fed || Vector3.Distance(_player.position, transform.position) < 4.5f) &&
                          Vector3.Distance(_player.position, homeAt) < 26f;

            if (follow)
            {
                _target = _player.position - (_player.position - transform.position).normalized * 1.3f;
            }
            else if (Time.time > _nextPick)
            {
                // 自己晃：在猫窝附近随便挑个点，走到了就歇一会儿
                _nextPick = Time.time + Random.Range(3.5f, 8f);
                var off = Random.insideUnitCircle * wanderRadius;
                _target = homeAt + new Vector3(off.x, 0, off.y);
            }

            Vector3 to = _target - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist > 0.35f)
            {
                float speed = follow ? 2.6f : 1.2f;
                transform.position += to.normalized * speed * dt;
                transform.rotation = Quaternion.Slerp(transform.rotation,
                    Quaternion.LookRotation(to.normalized), 6f * dt);
                // 走路时轻轻起伏，不然像在地上滑
                _bob += dt * speed * 6f;
                var p = transform.position;
                p.y = GroundY() + 0.28f + Mathf.Abs(Mathf.Sin(_bob)) * 0.05f;
                transform.position = p;
            }
            else
            {
                var p = transform.position;
                p.y = Mathf.Lerp(p.y, GroundY() + 0.28f, 6f * dt);
                transform.position = p;
            }

            // 尾巴永远在动：这一下比多加十个构件都管用
            if (_tail != null)
                _tail.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 2.2f) * 18f,
                    Mathf.Sin(Time.time * 1.6f) * 22f, 0);

            if (follow && Time.time - _lastMeow > 25f && dist < 3f)
            {
                _lastMeow = Time.time;
                GameEvents.RaiseSubtitle(Fed
                    ? "【猫】喵——（它绕着你的脚踝转了一圈）"
                    : "【猫】喵？（它抬头看你，尾巴慢慢摆着）");
            }
        }

        float GroundY()
        {
            if (Physics.Raycast(transform.position + Vector3.up * 1.2f, Vector3.down,
                    out var hit, 4f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return homeAt.y;
        }

        /// <summary>喂它一次：跑过来吃，之后一分钟黏着你。</summary>
        public void Feed()
        {
            _fedUntil = Time.time + 60f;
            _lastMeow = Time.time;
            GameEvents.RaiseSubtitle("【猫】它小跑过来，埋头吃了起来——尾巴翘得老高。");
            GameAudio.Play(GameAudio.Sfx.Cast, 0.4f);
        }
    }

    /// <summary>猫食盆：走近按 E 喂猫。</summary>
    public class CatFeeder : MonoBehaviour
    {
        public PetCat cat;
        public float range = 2.6f;

        PlayerController _player;
        float _lastHint = -99f;
        float _lastFeed = -99f;

        void Update()
        {
            if (_player == null)
            {
                _player = FindObjectOfType<PlayerController>();
                if (_player == null) return;
            }
            if (Vector3.Distance(transform.position, _player.transform.position) > range) return;

            if (Time.time - _lastHint > 8f)
            {
                _lastHint = Time.time;
                GameEvents.RaiseSubtitle("【猫食盆】按 E 添一点粮。");
            }
            if (!Input.GetKeyDown(KeyCode.E) && !Mobile.MobileInput.GetDown("Interact")) return;
            if (Time.time - _lastFeed < 3f) return;
            _lastFeed = Time.time;
            if (cat != null) cat.Feed();
        }
    }
}

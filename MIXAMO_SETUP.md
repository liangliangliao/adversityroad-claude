# 接入 Mixamo 动捕人形角色（真人级战斗动作）

程序化方块角色有硬性上限，达不到动捕真实度。本项目已写好**动捕集成代码**：
运行时若发现动捕资源就自动接管（用 Playables 播放 Mixamo 动画片段），
**找不到资源则自动回退到原来的程序化方块角色**——所以在你导入资源之前，构建始终正常。

你需要做的只有一件事：**从 Mixamo 下载免费人形 + 一套动作，按下方命名放进工程**。
全部在 Unity 编辑器里操作，约 15–30 分钟，无需写任何代码。

---

## 一、下载素材（[mixamo.com](https://www.mixamo.com)，免费，需注册）

1. **角色**：Characters 里选一个人形（如 `Y Bot`、`X Bot` 或任意角色）→ Download，
   Format = **FBX for Unity(.fbx)**，Pose = **T-pose**。
2. **动作**：Animations 里搜索并逐个下载（Format 同上，**勾选 In Place**，无需皮肤 = `Without Skin`）。
   建议至少下载这些（括号是搜索关键词）：

   | 用途（放置文件名） | Mixamo 搜索关键词 | 循环 |
   | --- | --- | --- |
   | `Idle` | idle | ✅ |
   | `CombatIdle` | sword and shield idle / fighting idle | ✅ |
   | `Walk` | walking | ✅ |
   | `Run` | running | ✅ |
   | `Attack` | sword slash / katana slash | |
   | `AttackUp` | sword slash up / uppercut slash | |
   | `SwordThrust` | stab / sword thrust | |
   | `HeavyAttack` | great sword slash / heavy slash | |
   | `AttackSpin` | spin attack / 360 sword | |
   | `AttackLeap` | jump attack / leaping slash | |
   | `PunchJab` | jab / punch | |
   | `PunchCross` | cross punch / hook | |
   | `AttackKick` | front kick / roundhouse kick | |
   | `SideKick` | side kick | |
   | `SpinKick` | spin kick / tornado kick | |
   | `JumpKick` | flying kick | |
   | `Hit` | hit reaction / hit to body | |
   | `Knockdown` | knocked down / falling back death 前段 | |
   | `Death` | dying / death | |
   | `Dodge` | dodge roll / roll | |
   | `Guard` | blocking idle | ✅ |
   | `Cast` | casting spell | |
   | `Sweep` | low sweep kick | |
   | `JumpAttack` | 同 AttackLeap 可复用 | |

   > 缺哪个动作，代码会自动忽略该招式（回退到不换姿势），不会报错。至少要有
   > **Idle / Walk / Run**，否则整个动捕模式判定无效、回退方块角色。

---

## 二、导入并设为 Humanoid

1. 把角色 FBX 拖进 `Assets/`（例如 `Assets/_Project/Characters/Model/`）。
2. 选中该 FBX → Inspector → **Rig** 选项卡 → Animation Type = **Humanoid** → Apply。
3. 把每个**动作 FBX** 也拖进工程，逐个选中 → **Rig** → Animation Type = **Humanoid**，
   Avatar Definition = **Copy From Other Avatar** → Source 选第 2 步角色的 Avatar → Apply。
4. 对 `Idle / Walk / Run / CombatIdle / Guard`：选中动作 → **Animation** 选项卡 →
   勾选 **Loop Time** → Apply。

---

## 三、把动画片段放进 Resources（按约定命名）

代码通过 `Resources.Load` 按**固定路径与文件名**加载，务必一致：

```
Assets/_Project/Resources/Characters/
├── PlayerModel.prefab        # 见第四步
├── EnemyModel.prefab         # 可选；没有就复用 PlayerModel
└── Anims/
    ├── Idle.anim   Walk.anim   Run.anim   CombatIdle.anim
    ├── Attack.anim  AttackUp.anim  SwordThrust.anim  HeavyAttack.anim
    ├── AttackSpin.anim  AttackLeap.anim  JumpAttack.anim  Sweep.anim
    ├── PunchJab.anim  PunchCross.anim
    ├── AttackKick.anim  SideKick.anim  SpinKick.anim  JumpKick.anim
    ├── Hit.anim  Knockdown.anim  Death.anim  Dodge.anim  Guard.anim  Cast.anim
```

取出片段的两种方式（任选）：
- **简单**：在 FBX 上展开三角箭头 → 选中里面的 AnimationClip → `Ctrl/Cmd+D` 复制出独立
  `.anim` → 拖进 `Resources/Characters/Anims/` 并**改成上表文件名**。
- 或直接把导入好的动作 FBX 放到 `Resources/Characters/Anims/` 并把 FBX 改名为上表名字
  （代码 `Resources.Load<AnimationClip>` 也能取到同名 FBX 内的主片段）。

> 文件名**大小写要完全一致**（`Attack` 不是 `attack`）。

---

## 四、做角色预制体（模型 + Animator）

1. 把角色模型 FBX 拖进场景 → 它下面会有骨骼层级。
2. 选中根物体，Inspector 里确认有 **Animator** 组件，且 **Avatar** = 该模型的 Avatar，
   **Controller 留空**（我们用 Playables 驱动，不需要 Controller）。
3. 把这个物体拖回 `Assets/_Project/Resources/Characters/`，命名 **`PlayerModel`**，
   存成预制体。删除场景里的实例。
4. 敌人想用不同外观就再做一个 **`EnemyModel`**；否则不做，代码会自动复用 `PlayerModel`。

---

## 五、提交 → 自动构建

把 `Assets/_Project/Resources/Characters/` 整个目录（模型、Avatar、.anim、.prefab 及其
`.meta`）提交并推送到 `main` 或 `claude/**` 分支。GitHub Actions 会打出带动捕动作的 APK。

- 运行时代码检测到 `Resources/Characters/PlayerModel` 即切到动捕模式；
- 找不到就继续用程序化方块角色（不影响构建）。

---

## 代码侧（已完成，无需改动）

- `PlayableAnimator.cs`：Playables 图，locomotion(idle/走/跑/临战) 混合 + 招式层交叉淡入。
- `MecanimCharacter.cs`：实例化模型、进入动捕模式、把兵器挂到右手骨骼。
- `HumanoidAnimator.cs`：有动捕资源就接管，否则走程序化骨骼（同一套对外接口）。
- 招式名 = `PoseState` 枚举名，一一对应上表片段。

导入后如某招时机/朝向不对，或需要把某动作改成循环/单次，告诉我片段名，我来调交叉淡入与映射。

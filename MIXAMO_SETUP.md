# 接入 Mixamo 动捕角色 + 动作（真人级战斗动作）

代码已全部写好。你**只需要把下载好的文件放进指定文件夹**，Unity 会自动把它们配成
Humanoid、自动给走/跑/待机加循环——**无需在 Inspector 手动设置任何东西**。
运行时代码会自动加载它们；找不到就回退到原来的方块角色（所以放之前也不影响运行）。

---

## 一、放文件（就两步）

在工程里建好这个目录结构（没有就新建）：

```
Assets/_Project/Resources/Characters/
├── PlayerModel.fbx          ← 玩家角色模型
├── EnemyModel.fbx           ← 敌人角色模型（可选，不放就复用 PlayerModel）
└── Anims/                    ← 所有动作 FBX 全部丢这里（不用改名）
    ├── Maria WProp J J Ong@Idle.fbx
    ├── Maria WProp J J Ong@Fighting Idle.fbx
    ├── Maria WProp J J Ong@Walking.fbx
    ├── Maria WProp J J Ong@Running.fbx
    ├── Maria WProp J J Ong@Great Sword Slash.fbx
    ├── Maria WProp J J Ong@Great Sword Slash (1).fbx
    ├── Maria WProp J J Ong@Great Sword High Slash.fbx
    ├── Maria WProp J J Ong@Great Sword Jump Attack.fbx
    ├── Maria WProp J J Ong@Stabbing.fbx
    ├── Maria WProp J J Ong@Lead Jab.fbx
    ├── Maria WProp J J Ong@Cross Punch.fbx
    ├── Maria WProp J J Ong@Kicking.fbx
    ├── Maria WProp J J Ong@Side Kick.fbx
    ├── Maria WProp J J Ong@Spin Flip Kick.fbx
    ├── Maria WProp J J Ong@Flying Kick.fbx
    ├── Maria WProp J J Ong@Hit Reaction.fbx
    ├── Maria WProp J J Ong@Knocked Down.fbx
    ├── Maria WProp J J Ong@Dying.fbx
    └── Maria WProp J J Ong@Spell Casting.fbx
```

**只需两处操作：**
1. 把角色模型 `Maria WProp J J Ong.fbx` 复制进 `Resources/Characters/`，**改名为 `PlayerModel.fbx`**。
2. （可选）把 `Paladin WProp J Nordstrom.fbx` 复制进去，**改名为 `EnemyModel.fbx`**。
3. 其余**所有动作 FBX 原样丢进 `Resources/Characters/Anims/`，不用改名**
   （代码按 Mixamo 的 `@动作名` 自动识别并分配到招式）。

> 动作文件名里的 `@Idle` / `@Side Kick` / `@Great Sword Slash` 这些**后缀**是关键，
> Unity 会用它给动画片段命名，代码据此匹配。别去掉 `@` 后缀就行。

---

## 二、然后……没有然后了

- 放进去后 Unity 会自动导入：**FBX 自动设为 Humanoid，走/跑/待机自动加 Loop**
  （由 `Assets/_Project/Editor/MixamoImportPostprocessor.cs` 完成）。
- 如果你是**先放的文件、后拉的这次代码**，对着
  `Assets/_Project/Resources/Characters` 文件夹右键 → **Reimport** 一次即可让自动设置生效。
- 直接 **Play**：玩家/敌人就换成 Mixamo 动捕动作了。控制台若报缺 Idle/Walk/Run，
  说明这三个基础片段没放对（其余动作缺了会自动跳过、不报错）。

---

## 三、动作 → 招式 对应表（代码里已配好，供你核对）

| 招式 | 采用的 Mixamo 动作 |
| --- | --- |
| 待机 / 临战待机 | Idle / Fighting Idle |
| 走 / 跑 | Walking / Running |
| 轻击（横斩） | Great Sword Slash |
| 连段重斩 / 上撩 | Great Sword Slash (1) / Great Sword High Slash |
| 突刺 | Stabbing |
| 跃劈 / 空中劈 | Great Sword Jump Attack |
| 直拳 / 摆拳 | Lead Jab / Cross Punch |
| 正蹬 / 侧踢 / 旋踢 / 飞踢 | Kicking / Side Kick / Spin Flip Kick / Flying Kick |
| 受击 / 倒地 / 死亡 | Hit Reaction / Knocked Down / Dying |
| 施法 | Spell Casting |

绝招「觉醒·乱舞」会自动把上面几招串成连段演出（配酷炫但不遮挡动作的特效）。
缺哪个动作就自动跳过该招（回到 locomotion），不影响运行。

---

## 四、兵器（大剑）

已改为**自动**，不再挂那把程序化方块剑：

1. 若你下载的角色模型**本身就握着大剑**（Mixamo「Great Sword Pack」的角色多为持械导出），
   直接用模型自带的剑——代码会在骨骼层级里按名字（sword/blade/greatsword…）找到它并绑定刀光，
   **无需任何操作**。
2. 若模型不带剑、你想额外挂一把素材大剑：把大剑做成一个预制体命名为 `Weapon`，
   放到 `Resources/Characters/Weapon.prefab`，代码会实例化并挂到右手骨骼。
3. 两者都没有 → 右手不挂武器（徒手，正常，不会再出现悬空的方块剑）。

---

## 五、提交打包

Unity 里能正常 Play 后，把 `Assets/_Project/Resources/Characters/`（模型、动作 FBX 及其
`.meta`）整个提交推送到 `claude/**` 或 `main`，CI 会打出带动捕动作的 APK 到真机测试。

---

## 六、常见问题（本次已修）

| 现象 | 说明 |
| --- | --- |
| **角色腾空/陷地** | 已自动修：装配时量测模型包围盒，把脚底对齐到角色胶囊体底部。 |
| **角色太小** | 已自动修：自动缩放到标准身高 ~1.85m（大体型敌人在此基础上按体型放大）。 |
| **手里多一把方块剑** | 已自动修：动捕模式不再挂程序化剑，见上面「四、兵器」。 |
| **敌人太容易被打死** | 已加**调试模式**：默认开启「敌人耐揍」（设置面板可关）。正式发布把 `GameDebug.TankyEnemies` 设 false。 |
| **走路腿反向/像「鞋穿反了」** | 已自动修：动作 FBX 不带蒙皮、自建 Avatar 的 T-Pose 常校准失败导致重定向后腿部扭曲。现在 Anims/ 下的动画统一**复用 PlayerModel 的 Avatar**。操作：对 `Resources/Characters` 文件夹右键 → **Reimport**（先有 PlayerModel 再导动画）。 |

---

有任何一招时机/朝向不对，或想调整绝招连段顺序，把片段名发我，我改映射与串招。

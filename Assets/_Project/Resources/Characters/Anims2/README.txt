【公共补充库 / 角色·贰专属动作库】

两个用途：

1. **公共补充**（当前用法）：与角色无关的通用动作放这里——主库里没有的名字
   会从本目录再取一遍。拔剑/收剑（Withdrawing Sword / Sheathing Sword (1)）
   就在这里，两个角色都用得上。
   注意本目录缺 idle/walk/run，**不能**单独作为主库（会整体回退）。

2. **角色·贰专属动作库**（可选）：若想给角色·贰配一套独立动作，用该角色在
   Mixamo 逐个下载与 Anims/ 相同清单的动作 FBX 放进来。缺失/不完整时自动
   回退默认动作库，不影响运行。

寻址规则与 Anims/ 相同，见那边的 README。

【不要往这里堆未接入的动作包】
本目录在 Resources 下，LoadAll 会把里面每一条片段都加载进内存，哪怕没人用。
UAL1/UAL2/KayKit 这类素材储备一律放 Assets/_Project/Animations/ 下（非 Resources），
接入流程见那边的 README.txt。门禁 tools/animchain.py 会把"加载了却没人用"判红。

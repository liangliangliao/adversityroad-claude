Quaternius 通用动作库 1（CC0）。

UAL1_Standard.fbx 是**离线烘焙的源文件**：Editor/UalRetargetBaker.cs 借 Unity 的
人形重定向把它烤成本工程 Mixamo 骨架上的 Generic 片段，产物进
Resources/Characters/AnimsUAL，运行时只认那批产物——所以源文件本身不能进 Resources。
它也是 MixamoImportPostprocessor 里唯一走 Humanoid 导入的文件（按路径特判）。

UAL1_Standard_RM.fbx 是它的根位移变体，目前未接入。

其余说明见上级目录的 README.txt。

# GHPC mods

我自己写的 Gunner, HEAT, PC! 模组。现在每个模组各有一个仓库，这里是入口，也继续保留给 GHPC Mod Manager 用的下载。

| 模组 | 做什么 | 仓库 |
| --- | --- | --- |
| 载具维修菜单 | Q 菜单里加一个 Repair 子菜单，自己点要修哪一块，修一块花一块的时间 | [GHPC-RepairMenu](https://github.com/rhino2024/GHPC-RepairMenu) |
| 残骸清理 | 场上烧着的载具残骸攒够 3 辆就一次性撤掉，省下渲染、粒子和物理的帧 | [GHPC-WreckCleanup](https://github.com/rhino2024/GHPC-WreckCleanup) |
| 命中回放小窗 | 打中车之后右上角开几秒小窗，绕着那辆车转，看车内损伤 | [GHPC-HitCam](https://github.com/rhino2024/GHPC-HitCam) |
| 第三人称瞄准环 | 外部视角下屏幕中心一个固定圆环，炮塔转过去对准环指的地方 | [GHPC-SlewReticle](https://github.com/rhino2024/GHPC-SlewReticle) |

四个都是单个 dll 的模组，不装 AssetBundle，不改游戏文件，删掉 dll 就是卸载。都要 MelonLoader v0.7.2（net35 运行时），下载包里没有它。

## 这个仓库还留着什么

`manager/` 是提交给 GHPC Mod Manager 社区目录的两条条目原文，`v1.0.2` 那两个 zip 是目录现在指向的下载地址。目录那边正在审批的 PR 合入之前，这里的 release 不动，不然装的人会拿不到包。等条目改指各自的仓库之后，这一层就只剩下历史。

本仓库自身的 `RepairMenu/` `WreckCleanup/` `HitCam/` `SlewReticle/` 四个目录是拆分之前的源码副本，留着不动，往后的改动在各仓库的 `src/` 里。

## 许可

MIT。与 Radian Simulations 无关。在当前 Steam 版上实测，游戏更新有可能让模组失效。

## In English

Each mod now has its own repository, linked above. This one stays as the entry point and keeps the release assets the GHPC Mod Manager directory currently downloads, until the pending catalog PR is repointed.

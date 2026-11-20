# Space Rogue Xbox Controller Mod

Xbox Controller Mod v1.1.0 adds Windows XInput support to the original DOS edition of Space Rogue running in DOSBox. This repository contains the import manifest and the C# controller bridge.

- [Download the Mod ZIP](https://space-rogue-mods.zl5626.chatgpt.site/downloads/xbox-controller-1.1.0.zip)
- [Mod Loader and installation guide](https://space-rogue-mods.zl5626.chatgpt.site/en/)

The public loader already includes the mod. For a manual import, use the ZIP in the loader, enable Xbox Controller Support, and launch the original game. The bridge is started by the loader for that DOSBox session. The game and DOSBox must be supplied separately.

To build the bridge, run `node --input-type=module -e "import('./build.mjs').then(m => m.buildController())"` from the repository root on Windows x64 with Node.js 22+ and the .NET Framework x64 C# compiler.

## 简体中文

本 Mod 为 DOSBox 中运行的 Space Rogue DOS 原版提供 Xbox 手柄支持。仓库包含可导入的 Mod 清单及 C# 兼容层。公开版加载器已内置此 Mod，在 Mod 库启用 Xbox Controller Support 后即可启动。需要自备游戏和 DOSBox。

- [下载 Mod ZIP](https://space-rogue-mods.zl5626.chatgpt.site/downloads/xbox-controller-1.1.0.zip)
- [加载器下载与安装指南](https://space-rogue-mods.zl5626.chatgpt.site/)

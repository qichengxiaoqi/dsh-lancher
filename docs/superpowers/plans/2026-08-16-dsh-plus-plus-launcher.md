# dsh++ 启动器实施计划

目标：将现有 .NET 9 WinForms 启动器升级为具有六栏导航、DeepSeek API 管理、系统指令扫描、插件管理和主题定制能力的单文件 Windows 启动器。

实施顺序：核心配置与路径验证 → API 与凭据 → 系统指令与插件 → WinForms UI → 测试、构建与发布。

默认环境：DSH 源码由自动探测器识别，DSH Home 默认是 `%USERPROFILE%\\.dsh`，插件根目录由相邻目录、Home 或 Profile 的 `file:` 依赖识别，Web 地址 `http://127.0.0.1:3080`。

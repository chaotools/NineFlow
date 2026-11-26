# 参与贡献

## 环境要求

- Windows 10 / 11 x64
- .NET 10 SDK

## 构建与测试

```powershell
.\build.ps1        # 还原 + 编译 + 全部测试 + 发布单文件 exe
```

也可以分开执行：

```powershell
dotnet build tests\NineFlow.Tests\NineFlow.Tests.csproj -c Release
dotnet tests\NineFlow.Tests\bin\Release\net10.0-windows\NineFlow.Tests.dll                     # 完整 499 项（467 逻辑 + 32 集成）
dotnet tests\NineFlow.Tests\bin\Release\net10.0-windows\NineFlow.Tests.dll --skip-integration   # 跳过需真实桌面的 32 项
```

## 约定

- 不引入新的第三方依赖（测试也不要引入测试框架）
- 界面改动请附截图：`tools\capture-screen.ps1` 可截窗口
- 提交信息用中文，首行概括改动，正文说明原因
- 用户可见的改动请同步更新 `CHANGELOG.md`

## 分支与规则集

`main` 受仓库规则集保护：

- 禁止直接推送，必须通过 Pull Request
- PR 需要通过 `build` 检查，且分支需与 `main` 保持同步
- 禁止强推与删除 `main`，禁止合并提交（只允许 squash）
- `v*` 标签禁止删除和移动

## 发版流程

1. 修改 `CHANGELOG.md`，把 `[Unreleased]` 内容整理到新版本小节
2. 更新 `src/NineFlow/NineFlow.csproj` 里的版本号（`Version` / `FileVersion` / `AssemblyVersion`）
3. 提交到分支并开 PR（`main` 受规则集保护：必须走 PR、build 检查通过、线性历史），合并后再继续
4. 打标签并推送：`git tag v1.1.x && git push origin v1.1.x`
5. CI 会自动校验版本号、跑测试、打包、创建 GitHub Release（README 的 release 徽章由 shields.io 动态读取，无需手工维护）

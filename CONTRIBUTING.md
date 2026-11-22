# 参与贡献

## 环境要求

- Windows 10 / 11 x64
- .NET 8 SDK

## 构建与测试

```powershell
.\build.ps1        # 还原 + 编译 + 全部测试 + 发布单文件 exe
```

也可以分开执行：

```powershell
dotnet build tests\NineFlow.Tests\NineFlow.Tests.csproj -c Release
dotnet tests\NineFlow.Tests\bin\Release\net8.0-windows\NineFlow.Tests.dll                     # 完整 239 项
dotnet tests\NineFlow.Tests\bin\Release\net8.0-windows\NineFlow.Tests.dll --skip-integration   # 跳过需真实桌面的 18 项
```

## 约定

- 不引入新的第三方依赖（测试也不要引入测试框架）
- 界面改动请附截图：`tools\capture-screen.ps1` 可截窗口
- 提交信息用中文，首行概括改动，正文说明原因
- 用户可见的改动请同步更新 `CHANGELOG.md`

## 发版流程

1. 修改 `CHANGELOG.md`，把 `[Unreleased]` 内容整理到新版本小节
2. 更新 `src/NineFlow/NineFlow.csproj` 里的版本号（`Version` / `FileVersion` / `AssemblyVersion`）
3. 提交并推送 `main`
4. 打标签并推送：`git tag v1.1.x && git push origin v1.1.x`
5. CI 会自动校验版本号、跑测试、打包、创建 GitHub Release，并把 README 的 release 徽章更新到新版本

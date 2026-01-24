# IcoConverter - 图片转 ICO 工具


<div align="center">

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![WPF](https://img.shields.io/badge/WPF-Desktop-512BD4?logo=windows)
![License](https://img.shields.io/badge/License-MIT-green.svg)
![Platform](https://img.shields.io/badge/Platform-Windows-blue.svg)

基于 .NET 8 / WPF 的桌面级 ICO 工具，圆角裁剪、批量转换、EXE 图标提取、ICO→PNG

</div>


### 功能
- 拖放或对话框加载 JPG/PNG/BMP/GIF/TIFF/SVG/ICO/EXE/DLL。
- 自动统一 DPI，并在加载时根据最短边决定可用分辨率。
- 默认导出ICO分辨率 16×16~256×256
- 源图满足条件时自动解锁 512×512 与 1024×1024，并以 PNG 负载写入 ICO。
- 一键导出当前预览 PNG
- 批量任务支持多图排队处理，输出目录自定义。
- ICO转PNG(导出ICO包含的所有分辨率)
- EXE/DLL 资源ICO提取并扫描同名 .mui/.mun 卫星资源，列出所有图标组合

### 蒙版
- 支持圆角矩形、正圆与多边形三种蒙版，半径 0-1024px、边数 3-64、旋转 -180°~180° 可调。
- SkiaSharp 蒙版渲染，透明背景填充。
- 实时预览支持延迟防抖、可按需关闭并改为手动应用。
- 内置撤销/重做栈与历史记录，方便比较不同参数。


### 开发环境
- Windows 11 25H2
- Visual Studio 2026
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download)

### 构建发布

```bash
dotnet restore src/IcoConverter.sln
dotnet build src/IcoConverter.sln -c Release
# 可选：使用 PowerShell 脚本
pwsh ./build.ps1
```

### 调试运行
```bash
dotnet run --project src/IcoConverter.csproj
```

### 快速开始
1. 运行 `IcoConverter.exe`，拖放或选择一张图片/ICO/EXE。
2. 根据需要选择蒙版形状、圆角、边数、旋转，必要时关闭实时预览并手动点“应用蒙版”。
3. 在分辨率面板勾选需要的尺寸（若源图够大，可解锁 512/1024）。
4. 点击“导出 ICO”并选择保存路径；也可用“导出 PNG”导出当前预览。


## 依赖组件
- [HandyControl 3.5.1](https://github.com/HandyOrg/HandyControl)
- [Microsoft.Extensions.DependencyInjection 10.0.2](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection)
- [SkiaSharp 3.119.1](https://github.com/mono/SkiaSharp)
- [Svg.Skia 3.4.1](https://github.com/wieslawsoltes/Svg.Skia)

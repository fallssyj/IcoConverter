# IcoConverter - 图片转 ICO 工具

![IcoConverter](https://raw.githubusercontent.com/fallssyj/IcoConverter/refs/heads/main/img/20260119-182223.png)

<div align="center">

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![WPF](https://img.shields.io/badge/WPF-Desktop-512BD4?logo=windows)
![License](https://img.shields.io/badge/License-MIT-green.svg)
![Platform](https://img.shields.io/badge/Platform-Windows-blue.svg)

一个基于 WPF .NET 8 开发的图片转 ICO 工具，支持圆角处理、批量转换、SVG 导入、主题切换等功能。

</div>

### 🎨 核心功能
- **高质量 ICO 转换**：支持 9 种标准分辨率 (16×16 到 256×256)
- **ICO转PNG**：自动识别 ICO 内包含的所有分辨率按需导出指定尺寸的PNG
- **智能圆角处理**：可调节圆角半径和三种质量级别
- **批量处理**：支持多图片批量转换为 ICO
- **实时预览**：编辑效果实时可见，所见即所得
- **SVG 支持**：自动识别并渲染 SVG
- **主题切换**：浅色/深色主题一键切换

### 🖼️ 图像处理
- **圆角自定义**：0-1024px 可调圆角半径
- **质量分级**：低/中/高三档圆角质量
- **DPI 标准化**：自动统一图像 DPI（可调整）
- **格式支持**：JPG、PNG、BMP、GIF、TIFF、SVG、ICO

## 🧰 开发环境

- 🪟 **Windows 11 25H2**
- 🧑‍💻 **Visual Studio 2026**
- 🧷 **.NET 8.0 SDK**

## 🚀 使用方式

### 图形界面
1. 运行 `IcoConverter.exe`
2. 加载图片（按钮或拖放）
3. 设置圆角、分辨率
4. 预览后导出 ICO、PNG

### 批量转换
1. 点击“批量转换”
2. 选择多张图片
3. 指定输出文件夹

### 命令行
```bash
IcoConverter.exe "C:\path\to\image.png"
```

## 🗂️ 项目结构

```
IcoConverter/
├── README.md
└── src/
    ├── Assets/        # 资源文件
    ├── Models/        # 数据模型
    ├── Services/      # 图像处理
    ├── ViewModels/    # ViewModels
    ├── Utils/         # 命令与工具
    ├── Styles/        # 主题与图标资源
    ├── MainWindow.xaml
    ├── IcoToPngWindow.xaml
    ├── AboutWindow.xaml
    └── App.xaml
```

## 🔧 构建与运行

```bash
dotnet build src/IcoConverter.sln --configuration Release
dotnet run --project src/IcoConverter.csproj
```

## 🙏 致谢

- MiSans
- HandyControl
- Microsoft.Extensions.DependencyInjection
- System.Drawing.Common
- SkiaSharp
- Svg.Skia
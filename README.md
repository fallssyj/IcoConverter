# IcoConverter - 图片转图标工具


<div align="center">

![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet)
![WPF](https://img.shields.io/badge/WPF-Desktop-512BD4?logo=windows)
![License](https://img.shields.io/badge/License-MIT-green.svg)
![Platform](https://img.shields.io/badge/Platform-Windows-blue.svg)

基于 .NET 10 LTS / WPF 的桌面图标工具，使用原生 Fluent 主题，支持导出 Windows ICO、macOS ICNS、PNG、WebP 和网站图标，以及裁剪、批量转换、EXE 图标提取和 ICO / ICNS 转 PNG。

</div>


### 功能
- 拖放或通过文件对话框打开 JPG、PNG、BMP、GIF、TIFF、WebP、SVG、SVGZ、ICO、ICNS、EXE 和 DLL。
- 加载时统一 DPI，根据图像短边确定可选的导出尺寸。
- ICO 提供 16×16 至 256×256 的常规尺寸。图像够大时可选 512×512 和 1024×1024，部分软件可能无法读取这些扩展尺寸。
- 一键导出 macOS ICNS：按 macOS 的尺寸语义写入 `icp4`~`ic10`，并补 `ic11`~`ic14` 的 @2x 别名，Retina 屏取到的是同分辨率原图而不是放大图。尺寸勾选窗口只列出 ICNS 支持的档位。
- 一键导出当前预览 PNG / WebP（导出通道使用高保真渲染）。WebP 导出前可选无损，或按 0~100 的质量有损压缩，保留 alpha。
- 多尺寸 PNG 集：勾选尺寸后每个尺寸输出一张 `名字-16.png`，做 favicon、Electron、Android / iOS 图标集时一次到位。
- 网站图标：一次导出 `favicon.ico`（16、32、48 像素三帧）、16 和 32 像素的 favicon PNG，以及 180×180 的 `apple-touch-icon.png`。
- 批量任务支持多图排队处理，输出目录自定义。同名输出自动加编号，例如 `logo (2).ico`。
- ICO / ICNS 转 PNG：按像素尺寸列出可导出的帧，每种尺寸只列一项。勾选所需尺寸后批量导出，选中列表项可在右侧查看放大的缩略图。
  ICNS 读的是 PNG 载荷的帧（`icp4`~`icp6`、`ic07`~`ic14`），老式 ARGB / JPEG2000 元素不支持。
- EXE/DLL 资源ICO提取并扫描同名 .mui/.mun 卫星资源，列出所有图标组合
  格子视图里带尺寸标注，右侧放大预览选中项并列出逐帧尺寸与位深。
- 窗口材质可选云母（Mica，默认）、亚克力（Acrylic）和纯色，主窗口与对话框共用设置。云母和亚克力需要 Windows 11 22H2 或更高版本。
- 命令行模式：带 `--out` 就不开窗口，见下面「命令行」。

### 预览
- 顶部工具栏可切预览底板：无 / 棋盘格 / 白 / 黑。棋盘格看透明区，白底黑底用来判断图标摆在浅色资源管理器或深色任务栏上还认不认得出，选择记在设置里。
- 导出 ICO / ICNS / PNG 集的尺寸窗口左侧带每档缩略图，右侧按选中的尺寸放大；放大用最近邻，看到的就是这一档的像素本身。

### 蒙版
- 支持圆角矩形、正圆两种蒙版，圆角支持像素/百分比双单位，切换单位时数值自动换算。
- 支持画布扩展（Expand Canvas）与填充模式（透明/白/黑/自定义色），底色可以只填蒙版内部，也可以铺满整张画布。
- 圆形支持内切 / 外切（按长边，溢出裁掉）/ 保留留白；外切时引导线会画出真实半径并补一圈内切参考线。
- 主体对齐：按四周空白裁到主体，再按百分比补留白，批量做图标集时各图主体占比一致。
- SkiaSharp 蒙版渲染，支持边缘超采样（长边 1536px 以内）与大图预览降采样加速。
- 实时预览内置防抖与缓存复用；像素单位的圆角、画布扩展在降采样预览里按同一比例缩放。
- 内置撤销/重做（`Ctrl+Z` / `Ctrl+Y` 或 `Ctrl+Shift+Z`），连续拖动滑块会合并为一步；主体对齐也能撤销。

### 裁剪
- 工具栏裁剪按钮进入裁剪模式，拖动选区或 8 个手柄调整范围。
- 裁剪模式下 `Enter` 应用、`Esc` 取消；只在裁剪模式生效，不影响普通界面的按键。
- 裁剪是非破坏性的。可随时恢复整图并重新框选，裁剪后的图像用于计算蒙版和可选的导出尺寸。


### 开发环境
- Windows 11 25H2
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

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

运行发布包需要安装与包架构一致的 [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)。

1. 运行 `IcoConverter.exe`，把文件拖进窗口，或点击工具栏的「打开」。打开 ICO 或 ICNS 会进入拆帧窗口，打开 EXE 或 DLL 会进入图标提取器。
2. 打开普通图片后，在右侧调整蒙版形状、圆角或画布参数。预览会自动更新。
3. 点击工具栏的「导出」并选择格式。导出 ICO、ICNS 或 PNG 图标集时，先勾选尺寸。导出 WebP 时，先选择有损或无损压缩。导出单张 PNG 时，直接选择保存位置。

### 网站图标

选择「导出 → 导出网站图标」，然后选择输出位置。文件保存在该位置的 `website-icons` 子目录中。重复导出时会为新目录添加编号。

将图标复制到站点根目录后，可将以下代码放到网页的 `<head>` 中。

```html
<link rel="icon" href="/favicon.ico">
<link rel="icon" type="image/png" sizes="16x16" href="/favicon-16x16.png">
<link rel="icon" type="image/png" sizes="32x32" href="/favicon-32x32.png">
<link rel="apple-touch-icon" sizes="180x180" href="/apple-touch-icon.png">
```

### 命令行

使用 `--out` 进入命令行模式，无需打开图形界面。以下示例在 PowerShell 中运行。

```powershell
.\IcoConverter.exe logo.png --out app.ico --sizes 16,32,48,256 --corner 40
.\IcoConverter.exe logo.png --out app.icns --icns
.\IcoConverter.exe logo.png --out icons --pngset --padding 10
.\IcoConverter.exe --out app.ico --help
```

| 参数 | 用途 |
| --- | --- |
| `--out <路径>` | 指定 ICO 或 ICNS 的目标文件，或 PNG 图标集的输出目录。PNG 图标集也接受带扩展名的路径，用其所在目录和文件名作为输出位置和命名前缀。 |
| `--ico`、`--icns`、`--pngset` | 指定输出格式。省略时按 `.ico` 或 `.icns` 扩展名确定格式，其余路径输出 PNG 图标集。 |
| `--sizes 16,32,48,256` | 指定逗号分隔的尺寸。省略时使用所选格式的默认尺寸。PNG 图标集支持 `32x16` 等非方形尺寸，输出名形如 `logo-32x16.png`。 |
| `--corner 20`、`--expand 8` | 设置圆角半径和每边的画布扩展像素数。画布扩展值可为负数。 |
| `--padding 8` | 裁去主体周围的空白，再按百分比补边。范围为 0 到 45，设为 0 时跳过主体对齐。 |
| `--frame auto` | 设置 ICO 帧编码，可选 `auto`、`png` 或 `bmp`。 |

脚本可用进程退出码判断结果。成功为 `0`，处理失败为 `1`，参数错误为 `2`。


## 依赖组件
- [fluentui-system-icons](https://github.com/microsoft/fluentui-system-icons)（界面图标）
- [CommunityToolkit.Mvvm 8.3.2](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
- [Microsoft.Extensions.DependencyInjection 10.0.5](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection)
- [SkiaSharp 3.119.2](https://github.com/mono/SkiaSharp)
- [Svg.Skia 4.2.0](https://github.com/wieslawsoltes/Svg.Skia)

各组件的许可证与版权声明见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

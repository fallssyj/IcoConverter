# 第三方组件与资源

IcoConverter 本身以 MIT 许可证发布，全文见 [LICENSE](LICENSE)。以下第三方组件与资源按各自的许可证随本项目分发，其版权归原作者所有。

## fluentui-system-icons

`src/Assets/Icons.xaml` 中的界面图标取自 [microsoft/fluentui-system-icons](https://github.com/microsoft/fluentui-system-icons) 的 20px Regular 图标，提取 `path` 几何数据后内联为 WPF `PathGeometry`。

MIT License
Copyright (c) 2020 Microsoft Corporation

## NuGet 依赖

| 组件 | 版本 | 许可证 | 项目地址 |
| --- | --- | --- | --- |
| CommunityToolkit.Mvvm | 8.3.2 | MIT | https://github.com/CommunityToolkit/dotnet |
| Microsoft.Extensions.DependencyInjection | 10.0.5 | MIT | https://github.com/dotnet/runtime |
| SkiaSharp | 3.119.2 | MIT | https://github.com/mono/SkiaSharp |
| Svg.Skia | 4.2.0 | MIT | https://github.com/wieslawsoltes/Svg.Skia |

以上直接依赖均声明为 MIT 许可。传递依赖 `Svg.Custom 4.2.0` 使用 [Microsoft Public License（MS-PL）](https://licenses.nuget.org/MS-PL)。

## MIT License

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

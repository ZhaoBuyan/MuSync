# Third-Party Notices

MuSync 的代码与设计大量借鉴/沿用了以下开源项目，特此致谢。按各自许可证要求保留版权声明：

## wuyan1337/yySync

> 播放器状态读取的逆向实现（网易云内存特征码、QQ 音乐偏移、LX Music API）、
> SteamKit2 会话与状态同步逻辑的设计思路源自本项目（MIT 协议）。
>
> https://github.com/wuyan1337/yySync
>
> 原 LICENSE（MIT）：
>
> ```
> MIT License
>
> Copyright (c) 2018 Kyle
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.
> ```

## Music-DiscordRPC 谱系（yySync 的上游参考）

> yySync 的播放器逆向读取与 RPC 设计继承自这条谱系（均已核实为 MIT 协议）：
>
> - https://github.com/kriYamiHikari/Music-DiscordRPC
> - https://github.com/Kxnrl/NetEase-Cloud-Music-DiscordRPC
>
> 版权行：`Copyright (c) 2018 Kyle`（MIT 全文与上方 yySync 附带的一致）。

## SteamKit2

> Steam 网络客户端库：https://github.com/SteamRE/SteamKit
>
> 许可证：**GNU LGPL-2.1-only**（Lesser General Public License 2.1，非 MIT——早期登记不完整，特此补正）
> 版权：Copyright (C) 2018 Ryan Stecker & SteamRE Team
> 版本：SteamKit2 3.4.0（NuGet）
> 许可证全文随包提供：`licenses/LGPL-2.1.txt`；程序内「设置 → 关于 → 开源许可」亦可查看
> 许可证全文出处：https://www.gnu.org/licenses/old-licenses/lgpl-2.1.txt

## 传递依赖（随发布物一起分发）

以下组件由上面列出的依赖自动引入，版本为构建时实际解析到的版本：

| 组件 | 版本 | 许可证 | 版权 / 作者 |
| --- | --- | --- | --- |
| protobuf-net | 3.2.56 | Apache-2.0 | Marc Gravell |
| protobuf-net.Core | 3.2.56 | Apache-2.0 | Marc Gravell |
| System.IO.Hashing | 10.0.1 | MIT | © Microsoft Corporation |
| ZstdSharp.Port | 0.8.7 | MIT | Copyright Oleg Stepanischev |
| Microsoft.Win32.Registry | 5.0.0 | MIT | © Microsoft Corporation |
| System.Security.AccessControl | 6.0.1 | MIT | © Microsoft Corporation |
| System.Security.Cryptography.ProtectedData | 9.0.0 | MIT | © Microsoft Corporation |
| System.Security.Principal.Windows | 5.0.0 | MIT | © Microsoft Corporation |

> Apache-2.0 全文：https://www.apache.org/licenses/LICENSE-2.0
> 微软组件 MIT 全文：https://github.com/dotnet/runtime/blob/main/LICENSE.TXT

## Inno Setup 简体中文语言文件

> 安装器界面的简体中文翻译（`installer/languages/ChineseSimplified.isl`）来自社区翻译项目（MIT 协议）：
>
> https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation
>
> 译者 / Maintainer：Zhenghan Yang (Kira)
>
> 原 LICENSE（MIT）：
>
> ```
> MIT License
>
> Copyright (c) 2019 - 2020 kirakira
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.
> ```

# baml-longhorn

**面向 Windows Longhorn / 预发布版 Avalon 的 BAML（已编译 XAML）解析器与反编译器。**

[English](../../README.md) · [完成报告](COMPLETION.md) · [格式文档](.)

---

## 本项目由 AI 智能体完成

**本项目每一行代码均由 AI 智能体编写：DeepSeek Harness，运行 `deepseek-flash` 模型。**

这里不存在对既有代码的移植。所有记录布局都是通过**并排阅读反编译的 Microsoft 程序集**、
再将每条结论拿到**真实 BAML 字节**上验证而还原出来的——**两种互斥的记录框架**、
**八个世代剖面**，以及区分构建 4093 与 4074 的**五处载荷差异**。
成果以 **241 个真实 BAML 文件**验证，**每一套语料都达到 100% 完整反编译**。

完整说明（已完成什么、已验证什么、什么仍未验证）见 [`COMPLETION.md`](COMPLETION.md)。

---

## 功能

Longhorn 时代的 Avalon 在格式定型之前，会把标记编译成 **BAML**。本工具读取这些字节流，
并将其还原为 XAML 文本。

支持**两种框架**与**八个世代剖面**：

| 框架 | 世代 | 记录结构 |
|---|---|---|
| **SHORT** | 4074、4083、4093 | `[int16 类型]`，可变大小记录再跟 `[int32 大小]` |
| **LONG** | 3683、3718、4015、4033、4039、4042 | `[int64 大小][int16 类型]` |

世代**按结构判别，绝不依赖程序集版本号**——4074 与 4093 的二进制报告**完全相同的文件版本**，
却声明了不同的记录枚举。

## 已验证结果

| 语料 | 文件数 | 结果 |
|---|---|---|
| `samples/xaml`（4074） | 103 | **103 / 103** 完整反编译 |
| `samples/wcp4093`（4093） | 133 | **133 / 133** 完整反编译 |
| `samples/extracted-4093` | 5 | **5 / 5** 完整反编译 |
| `example.baml`（3683，LONG） | 1 | 完整；标签配平 |
| `481.baml`（4015，LONG） | 1 | 完整；标签配平；2,149 条记录 |

属性值解码率：4074 语料 **99.98%**（5,660 / 5,661），4093 语料 **99.99%**（6,802 / 6,803）。
两者各自剩余的 1 项均为**已知假阳性**：一个 BAML 字符串字面上就是 `23 17`。

## 两个前端

### GUI —— `baml-gui.exe`

```
baml-gui.exe [可选目录]
```

左侧为目录树与文件列表，右侧每个视图一个标签页：

| 标签页 | 内容 |
|---|---|
| **XAML** | 反编译器输出——编译的逆过程 |
| **Records** | 每条记录及其偏移、大小，以及按声明顺序排列的载荷字段 |
| **Tree** | 重建的标记树，类型名已解析 |
| **Tables** | 程序集 / 类型 / 属性驻留表与命名空间映射 |
| **Summary** | 方言、置信度、大小、解析完整度、记录直方图 |
| **Recon** | 字符串词元及其之间的原始字节 |

打开目录后，每个文件都会标注**检测到的世代与置信度**——这是判断一个字节流属于 4074、4093
还是 LONG 谱系某个构建的最快方式。视图在首次访问时渲染并缓存，因此浏览 51 KB 的字节流仍保持流畅。

`Ctrl+F` 在当前视图中搜索（`Enter`/`F3` 向前，`Shift+Enter`/`Shift+F3` 向后，均可环绕，
并显示 `n/总数` 计数）——这是必需的，因为 51 KB 的字节流解码出的文本远超一屏所能容纳。

`File > Save XAML as…` 保存当前文档；`File > Export all XAML…` 反编译所选目录下全部 `.baml`，
并镜像源目录结构；被读取器拒绝的文件计入**跳过**，而不会写出空文件。

### CLI —— `baml.exe`

```
baml xaml <文件>           反编译为 XAML 文本
baml records <文件>        解码并转储每条记录及其偏移
baml tree <文件>           重建并打印标记树
baml tables <文件>         打印程序集/类型/属性驻留表
baml detect <文件|目录>    报告检测到的方言与置信度
baml stats <文件|目录>     记录直方图与方言汇总
baml recon <文件>          侦察转储（字符串词元 + 词元间空隙）
```

两个前端都通过同一个门面 `BamlFileView` 渲染，因此**在展示内容与失败报告方式上不可能产生分歧**。

## 构建

Visual Studio 2015 或 MSBuild 14.0、.NET Framework 4.5、C# 6，**无 NuGet 依赖**。

```
"C:\Program Files (x86)\MSBuild\14.0\Bin\MSBuild.exe" BamlLonghorn.sln /t:Rebuild /p:Configuration=Release
```

输出：

```
src\BamlLonghorn\bin\Release\BamlLonghorn.dll
src\BamlLonghorn.Cli\bin\Release\baml.exe
src\BamlLonghorn.Gui\bin\Release\baml-gui.exe
```

## 目录结构

```
BamlLonghorn.sln
src/
  BamlLonghorn/            核心：剖面、走查器、树构建器、XAML 写入器
  BamlLonghorn.Cli/        baml.exe
  BamlLonghorn.Gui/        baml-gui.exe（WinForms）
docs/                      格式文档（英文）
docs/zh-CN/                同一套文档的简体中文版
samples/                   用于验证的 BAML 语料
tools/                     从 Microsoft 清单中提取 BAML 的抽取工具
```

新增一个世代是 `RecordProfile` 里**一张表**，而不是一个解析器：框架、码表与尺寸分类**一同携带**，
而 `BamlRecordWalker` 按记录的**名字**分派载荷，因此同一个读取器可服务于编号不同的枚举。

## 世代如何判别

功能标识 `"PreAlpha"` 作为判别依据毫无用处——4074 与 4093 **都会写入它**。真正区分二者的是
`FormatVersion` 元组：`(0, 0)` 与 `(0, 1)`。

因此 `BamlDetector.MinConfidence` 设为 **100**，使**部分匹配即拒绝**，而不是给出一个自信的
错误解析。这一检查非常具体地重要：把 4093 的字节流交给 4074 读取器，它能撑过数条记录，
然后死在一个「在一个枚举中合法、在另一个中不合法」的码上——例如 `type 33`，
在 4093 中是 `ResourceInfo`，在 4074 中是 `LastRecordType`。

LONG 谱系**完全不写 FormatVersion**，因此按形状检测：一个读起来合理的 `int64` 记录长度，
后跟一个为 `StartDocument` 的 `int16`。两种框架**互斥**，因为一者读类型字段的位置，
另一者读的是「大小的低半部分」。

### 剖面并非总能唯一确定

`example.baml` 在**全部六个** LONG 剖面下都能干净走完，因为它只用了 3683 就已存在的码。
字节**确实无法说明**是哪个构建写出的它，因此读取器报告**能干净走完的最老剖面**——
即数据所能支撑的**最保守声明**。报告最新的剖面会静默接受文档从未用过的码。
只有当文档**确实需要**某个更晚的码时，剖面才会收窄——这正是 `481.baml` 解析为 4015、
而 `example.baml` 停留在 3683 的原因。

## 用源文件校验反编译器

包含 `example.baml` 的档案里也有它编译自的 `example.xaml`：

```xml
<!-- 从 example.baml 反编译得到 -->
<Application1.Example xmlns="using:System;System.Windows;System.Windows.Controls"
                      Background="LightBlue">
  <System.Windows.Controls.Button Height="100px" ID="__El2__">Clicky</…>
  <System.Windows.Controls.TextPanel Foreground="White" FontFamily="Trebuchet MS"
                                     FontSize="72pt" ID="TextArea">Hello, world!</…>
</Application1.Example>
```

```xml
<!-- 源文件 example.xaml -->
<FlowPanel xmlns="using:…" xmlns:def="Definition" def:Language="C#" Background="LightBlue">
  <Button Height="100px" Click="handleClick">Clicky</Button>
  <TextPanel Foreground="White" FontFamily="Trebuchet MS" FontSize="72pt" ID="TextArea">…
```

三处差异，而**三者都是正确的 BAML 语义，不是解析错误**：

1. **根名是 `Application1.Example` 而非 `FlowPanel`。** BAML 里的 `TypeInfo id=0` **确实就是**
   `Application1.Example`：`ac` 把标记编译成了一个**派生自 `FlowPanel` 的生成类**，
   而根元素指向那个类。
2. **`Button` 上的 `ID="__El2__"`。** 该字符串**就在 BAML 里**——是编译器自动分配的 id，
   源文件从未提及。
3. **`Click="handleClick"` 缺失。** 编译器把事件处理器搬进了 `example.dll`，
   因此编译形态中不再出现该事件。

**反编译器忠于字节；差异是编译本身造成的。**

## 语料

| 目录 | 文件数 | 来源 |
|---|---|---|
| `samples/xaml` | 103 | `4074\Microsoft.Windows.WCPClient\Microsoft.Windows.WCPClient.g.resx` |
| `samples/wcp4093` | 133 | `4093\Microsoft.Windows.WCPClient\Microsoft.Windows.WCPClient.g.resx` |
| `samples/extracted-4093` | 5 | `4093\PresentationFramework` + `PresentationUI` 的 `.g.resx` |
| `samples/xaml-3683` | 32 | `3683\Microsoft.Windows.Client\LocalResources.resx`——**源码 XAML 文本，非 BAML** |
| `samples/reference` | 5 | `4047 BAML` 合集 |

`samples/xaml` 与 `samples/wcp4093` **共用 101 个文件名**，因此同一套界面可以跨两代对照。
各套语料的抽取方式与被排除的选项见 [`CORPORA.md`](CORPORA.md)。

## 尚未验证的部分

明确列出，因为**夸大覆盖率的反编译器比一个声明自身边界的更糟**。

* **构建 3718、4033、4039、4042 没有任何样本。** 它们的剖面由反编译枚举定义，且 LONG 写入器
  对整个谱系通用，但**没有任何真实字节检验过它们**。这一点用两种方法各确认一次
  （对每个构建中所有程序集做 `.baml` 资源名扫描；清单普查），并又从第二个角度复核了一次。
  这是**可用素材的缺口，而非实现的缺口**：剖面表与写入器**无需改动**即可消费此类文件。
* **4093 语料已完成反编译，但尚未与源文件比对**，因为没有找到与之匹配的源 XAML。
* **`GenericAttribute` 渲染为 `{uri}local`。** 这是合法 XAML 且无损，但真实写入器会声明前缀。
  它在约 350 个样本中出现 **0 次**，因此任何前缀分配方案都**无法验证**——
  而**无法验证的命名改动比一个有文档的合法回退更糟**。
* **集合属性**（`ClrArrayProperty`、`IListProperty`、`IDictionaryProperty`）已处理，
  但**没有任何样本包含它们**，因此该处理是**预防性**而非经测试的。

## 许可与来源

本仓库代码以 [MIT 许可证](../../LICENSE) 发布。

`samples/` 下的 BAML 样本自 Microsoft Longhorn 预发布构建中**原样提取**，
其权利仍归 Microsoft 所有。它们**仅**作为格式判别夹具与测试数据收录，
**不在 MIT 授权范围之内**。本项目是针对已废弃预发布软件的**独立互操作工具**，
与 Microsoft **无隶属关系，亦未获其背书**。

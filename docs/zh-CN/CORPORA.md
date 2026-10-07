# 语料及其扩充途径

`samples/` 中的每个样本集都提取自 Microsoft 资源清单或二进制资源。本文件记录了
哪个清单产出了哪个样本集，以及哪些来源已被排除，以免重复进行同样的查找。

## 各样本集

| 文件夹 | 文件数 | 来源 | 备注 |
|---|---|---|---|
| `samples\xaml` | 103 | `Microsoft.Windows.WCPClient.g.resx`，build 4074 目录树 | 主要的 4074 语料；192,327 字节 |
| `samples\wcp4093` | **133** | `Microsoft.Windows.WCPClient.g.resx`，build 4093 目录树 | 4093 语料；**其中 101 个文件名与 4074 样本集重合** |
| `samples\extracted-4093` | 5 | `PresentationFramework.g.resx`（1 个）+ `PresentationUI.g.resx`（4 个），均为 build 4093 目录树 | 包含一份 51,684 字节的主题字典 |
| `samples\xaml-3683` | **32** | `LocalResources.resx`，build 3683 目录树 | **源 XAML 文本**，而非 BAML |
| `samples\reference` | 5 | `4047 BAML` 集合 | LONG 家族样本，外加一组源文件/二进制对照 |

## 工具

```
python tools\extract_gresx.py     <x.g.resx> <outdir> [--list]   # base64 <data> entries
python tools\survey_resx.py       <root>...                       # what might be compiled XAML
python tools\extract_bf_xaml.py   <x.resx>  <outdir> [--list]    # BinaryFormatter MemoryStream
```

产出 `xaml` 与 `wcp4093` 的正是 `extract_gresx.py`；清单中的 `<data>` 条目以 base64
编码承载资源，而资源名本身说明了它是什么。

之所以需要 `extract_bf_xaml.py`，是因为 3683 构建存储 XAML 的方式**并非** BAML，而是
经 BinaryFormatter 序列化的 `System.IO.MemoryStream`，其 `_buffer` 中保存着文本：

```
00 01 00 00 00 ff ff ff ff 01 00 00 00 00 00 00 00   stream header
04 01 00 00 00  "System.IO.MemoryStream"             class name
0a 00 00 00                                          member count = 10
07 "_buffer" 07 "_origin" 09 "_position" 07 "_length" 09 "_capacity"
0b "_expandable" 09 "_writable" 0a "_exposable" 07 "_isOpen"
1d "MarshalByRefObject+__identity"                   member-name table
<values, one per member, in that order>
```

各成员是按字母顺序输出的，而 `_buffer` 与其余成员一样是固定大小，因此它的偏移量
无法预测；提取器于是改为扫描 `<` 字节并从该处开始解码，而不是对整套语法建模。

## 哪些来源不可用，以及这如何得到确认

**构建 3718、4015、4033、4039 与 4042 完全没有附带任何 BAML。** 它们经两种方式核查：

1. **资源名扫描。** 逐个构建目录树下的每个 `.dll` 与 `.exe` 都被搜过一遍 `.baml`
   资源名。结果：五个构建中**一个都没有**。这是可靠的检验手段——正是它找到了
   4074 与 4093 中的 WCPClient 语料。
2. **清单普查。** 对每个目录树运行 `survey_resx.py`，只找到 `ExceptionStringTable`、
   `ui.resx`、`Images`、`TrustDialog`、`MessageStringTable` 之类的字符串表——
   没有任何 `.g.resx`，因此也没有嵌入式编译 XAML。

针对 LONG 框架的字节签名扫描也尝试过，但**因不实用而被放弃**：模式
`[int64 size][int16 type]` 会不断命中普通 PE 数据（`size=98` 几乎在每一个程序集中
反复出现），产生的假阳性远多于有效信号。这种四字节对齐的猜测无法取代资源名检验。

因此，3718/4033/4039/4042 的 LONG 剖面是**依据各世代定义的记录码确定的，却没有样本**：
它们可以被选中、也能正常解析，但从未有任何东西拿真实字节去检验过它们。
`example.baml`（3683）与 `481.baml`（4015）仍是仅有的 LONG 样本。

## 构建 3683 属于源 XAML 时代

build 3683 的 `LocalResources.resx`（765,077 字节）以**文本**
形式保存了 **32 份 XAML 文档**，而非编译后的 BAML；`Microsoft.Windows.Client.dll`
也以 `ShellView.xaml` 之类的名称内嵌了同样的这 32 份。这让 3683 可以作为
**Avalon 时代标记语言面貌的基准真值（ground truth）**，但它不是 BAML 语料。

提取出的标记读起来符合那个时代的预期：

```xml
<Canvas
  xmlns="using:System.Windows;System.Windows.Controls;System.Windows.Documents;System.Windows.Shapes;System.Windows.Media;System.Windows.Presenters"
  xmlns:ShellViewControls="using:ShellInterop#Microsoft.Windows.Client.Shell.View.Controls"
  Height="100%" Width="100%" Background="#FF0000">

    <FlowPanel ID="Background" Width="100%" Height="100%">
        <Canvas Width="100%" Height="20%" Background="#FFFFFF"/>
        <Canvas Width="100%" Height="80%" Background="VerticalGradient #FFFFFF #C5D4E7"/>
    </FlowPanel>
```

请注意 `Background="VerticalGradient #FFFFFF #C5D4E7"`——正是 `BamlBrushExpander`
所处理的复合画刷简写，此处出现在一份真实的 3683 文档中，而不是构造出来的用例里。

## 一组值得研究的世代配对

由于有 101 个文件名同时出现在 4074 与 4093 两份语料中，同一个 UI 得以跨这两个世代
进行比较。以 `shellview\modulesizer.baml` 为例：

```
4074   184 B   <System.Windows.Controls.Primitives.Thumb xmlns="http:////schemas.microsoft.com//2005//xaml//" />

4093   205 B   <System.Windows.Controls.Primitives.Thumb xmlns="http:////schemas.microsoft.com//2005//xaml//"
                                                          xmlns:def="Definition" />
```

这 21 字节的差异来自新增的 `xmlns:def="Definition"`——这是两个世代之间一处具体、
可验证的改动，而且两者都能正确反编译。对于今后任何「4074 与 4093 之间，标记语言
本身（而非编码方式）发生了什么变化？」一类的问题，这组配对都是最佳的起点。

## 已验证的覆盖情况

```
samples\xaml          103 files   103 recognised as 4074   103/103 complete XAML
samples\wcp4093       133 files   133 recognised as 4093   133/133 complete XAML
samples\extracted-4093  5 files     5 recognised as 4093     5/5 complete XAML
```

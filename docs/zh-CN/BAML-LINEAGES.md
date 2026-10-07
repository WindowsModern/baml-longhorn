# 按测试分类的 BAML 格式谱系

目前在 Longhorn 资料中已经识别出三种互不兼容的已编译 XAML 格式，其中**两种已被解码**。哪一种对应哪一种，是通过让每个解码器跑遍所有可用文件来确定的，而不是靠推理得出的。

## 分类矩阵

`LONG` = 框架 `[int64 size][int16 type]`，next = `recordStart + size`
`SHORT` = 框架 `[int16 type][int32 size]`，next = `recordStart + 2 + size`

| 文件 | 字节数 | LONG | SHORT |
|---|---|---|---|
| `example.baml`（build 3683，来自 `ac.exe`） | 965 | **干净走查到 EOF，35 条记录** | 在偏移 0 处被拒绝 |
| `481.baml`（更早） | 43,627 | **干净走查到 EOF，2149 条记录** | 在偏移 0 处被拒绝 |
| `HelloWorld-Longhorn-0.0.baml` | 226 | 在偏移 0 处被拒绝 | **干净走查到 EOF，12 条记录** |
| `HelloWorld-AvalonCTP-0.2.baml` | 229 | 在偏移 0 处被拒绝 | 部分（2 条记录，停在 47 处） |
| `modulesizer.baml`（主语料库） | 184 | 在偏移 0 处被拒绝 | **干净走查到 EOF，7 条记录** |
| `desktopaurora-4074-original.baml` | 34,720 | 在偏移 0 处被拒绝 | 部分（919 条记录，停在 8,898 处） |

LONG 这一列的结果只有“干净”或“拒绝”两种，不存在任何含糊之处，因此它是一个具有决定性的判别依据。两个 SHORT 的部分结果都是在*较晚*的位置才停下（229 字节中的第 2 条记录；34,720 字节中的第 919 条记录），可见两个谱系界限分明，而 SHORT 的失败属于载荷层面，而非框架层面。

## 谱系 1 —— LONG 框架（较早；build 3683 与 481）

```
<int64 recordSize>   TOTAL record length, including this 8-byte field
<int16 recordType>
<payload>
```

类型码从 `1 StartDocument`、`11 AssemblyInfo`、`12 TypeInfo`、`3 ElementStart`、`6 …`、`13 …` 开始——也就是说，这就是从 `System.Windows.dll` 中恢复出来的、包含 25 个成员的 `MS.Internal.BamlRecordType` 家族：

```
0 Unknown  1 StartDocument  2 EndDocument  3 Element  4 EndElement
5 ParseLiteralContent  6 XmlnsProperty  7 DynamicProperty  8 DynamicEvent
9 GenericAttribute  10 Text  11 AssemblyInfo  12 TypeInfo  13 AttributeInfo
14 ComplexDynamicProperty  15 EndComplexDynamicProperty  16 ClrObject
17 EndClrObject  18 ClrProperty  19 ClrArrayProperty  20 EndClrArrayProperty
21 ClrComplexProperty  22 EndClrComplexProperty  23 IncludeTag
24 DynamicPropertyCustom
```

树节点记录（`Element`、`ParseLiteralContent`、`Text`、`ClrObject`）带有一个 12 字节的节点头，其中存放的是相对于位置的偏移。该谱系的参考实现是 `docs/reference-bamlread.py`，其 `selftest` 可以对 15 条记录 / 442 字节进行精确的往返。

**这正是本项目最初那份规范所描述的格式。** 后来发现它并不适用于主语料库，这让调查走了一大段弯路——但就本谱系而言，那份规范本身是正确的，上面两次 CLEAN 解码如今已经证明了这一点。

## 谱系 2 —— SHORT 框架（主要目标；4074 时期）

```
<int16 recordType>   always 2 bytes
<int32 recordSize>   only for BamlVariableSizedRecord subclasses
<payload>

next record = (offset of the size field) + size = recordStart + 2 + size
```

由 **build 4074** `PresentationFramework` 的反编译所得 `System.Windows.Serialization` 类定义
（`E:\Profiles\Bruce\Desktop\4074 - PresentationFramework True\`）。包含 34 个成员的
`BamlRecordType`、大小计算表以及所有载荷布局都记在
`BAML4074-FORMAT.md` 中。参考实现：`tools/baml4074.py`。

## 参考样本的来源

* `example.baml` 与 `HelloWorld` 这一对文件来自
  `E:\Profiles\Bruce\Desktop\example-mark-up-files-and-binaries`（附在
  <https://longhorn.ms/avalon-compiling-it/> 上）。该文章证实了工具链的情况：
  `ac`（Avalon 编译器）随 **build 3683** 一同发布，到 4051 时已被弃用，
  并由 `XamlC` 取代。`ac` 在生成 `exampleApp.exe`、`exampleApp.dll` 和
  `example.dll` 的同时生成了 `example.baml`，而随附的
  `example.xaml` 正是判断 BAML 应当表达什么内容的基准真相。
* `481.baml` 以及 4074 的 `desktopaurora` 原始文件来自
  `4047 BAML` 集合。

### 关于 `481.baml` 的一处更正

该集合自带的说明写道：

> This is also from build 4074, but it's an even older BAML format that 4074's
> default BAML parser actually fails to decode.

所以 **481 并不是 build 编号**——它标注的是一种更早的*格式*，而这个文件本身是携带 LONG 框架的 4074 时期产物。这与字节证据一致：`481.baml` 与 `example.baml`（build 3683）共享完全相同的前 16 字节前缀，因此它们属于同一谱系。481 这个标签不应被当作 build 来理解。

### 这两个谱系不可能同时成为一等公民

3683 的反编译结果和 481 语境下的反编译结果都**无法获得**。因此 LONG 谱系只能停留在参考实现的状态：

* 记录*类*由 `4074\System.Windows` 描述（`MS.Internal.BamlRecord`、
  带有固定 `[int64 size][int16 type]` 框架的 `BamlRecordManager`）
* `docs/reference-bamlread.py` 已经能够把这两个文件走查到 EOF（分别为 35 条和
  2149 条记录）
* 这些样本留在 `samples/reference/` 中作为**负面测试**——SHORT 读取器
  必须拒绝它们，而事实确实如此，在偏移 0 处即被拒绝

这是一个有边界、有文档记录的局限，而不是一项待办任务。


### `example.xaml` 及其重要性

```xml
<FlowPanel xmlns="using:System;System.Windows;System.Windows.Controls"
           xmlns:def="Definition" def:Language="C#" Background="LightBlue">
  <Button Height="100px" Click="handleClick">Clicky</Button>
  <TextPanel Foreground="White" FontFamily="Trebuchet MS" FontSize="72pt"
             ID="TextArea">Hello, world!</TextPanel>
  <def:Code><![CDATA[ ... ]]></def:Code>
</FlowPanel>
```

这是一个**配对的源文件/二进制样本**：XAML 明确写出 BAML 必须解码成什么，其中包括 `using:` 命名空间语法、带 `def:Language` 的 `Definition` 命名空间，以及一个 `<def:Code>` CDATA 块。它是 LONG 谱系所能获得的最佳基准真相。

## 对本项目的实际影响

解码器必须**自动检测谱系**，因为文件的格式无法从它的名称、大小，或者（如前面已经确认的）程序集 FileVersion 判断出来。LONG/SHORT 的判别是可靠的，因为每个解码器都会在偏移 0 处拒绝另一个谱系：

```
LONG  on example.baml / 481.baml   -> CLEAN
SHORT on HelloWorld / corpus       -> CLEAN
SHORT on example.baml / 481.baml   -> rejected at offset 0
LONG  on HelloWorld / corpus       -> rejected
```

因此检测方式很简单：先试 SHORT，如果在第一条记录处被拒绝，再试 LONG。这应当取代 `BamlDialect4074.Detect` 中目前的结构性启发式判断。

## 待办事项

1. **`desktopaurora-4074-original.baml` 在 34,720 字节中的第 919 条记录处停下**，
   错误为 `payload @8898: bad string length 12579138`。框架与枚举已由那些干净的
   解码结果证明无误，所以这是载荷布局的细节问题——最可能出在
   `LiteralContent`、`PropertyCustom` 或 `TypeSerializerInfo` 上。
2. **`HelloWorld-AvalonCTP-0.2.baml` 在偏移 47 处停下**，卡在
   `type 20 (XmlAttribute)` 上，该类型码在 build 4074 中没有对应的记录类。它的
   `FormatVersion` 元组是 `(0,2)`，而 Longhorn 那一对是 `(0,0)`，因此它就是集合
   说明中提到的“2004 年 11 月稍晚的版本”，其枚举可能又有所不同。
3. 应当为 LONG 谱系编写一个正式的方言读取器（`BamlDialectOlder`）作为参考实现，
   而不是把它留在那个 Python 脚本里。

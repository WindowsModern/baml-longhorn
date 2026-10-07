# 各代次的现状与 4093 载荷差异

本文件记录哪些内容已被解码、哪些已启用、哪些已知但尚未完成，以免每次都从代码里重新推导当前状态。

## 各代次分别处于什么状态

| generation | framing | enum | version tuple | status |
|---|---|---|---|---|
| **4074** | SHORT | 34 | `(0, 0)` | **完成**：103/103 语料可反编译为 XAML，5660/5661 个属性值已解码 |
| **4083** | SHORT | 34 | `(0, 0)` | **按同一性判定为完成** —— 见下文 |
| **4093** | SHORT | 37 | `(0, 1)` | **完成**：133/133 WCPClient 语料 + 5/5 抽取出的资源可反编译为 XAML |
| **3683** | LONG | 23 | none | **完成**：`example.baml` 35 条记录直达 EOF，可反编译为 XAML |
| **4015** | LONG | 26 | none | **完成**：`481.baml` 2149 条记录直达 EOF，710 行 XAML |
| 3718 | LONG | 25 | none | 已定义剖面；**不存在样本** |
| 4033 | LONG | 28 | none | 已定义剖面；**不存在样本** |
| 4039 | LONG | 33 | none | 已定义剖面；**不存在样本** |
| 4042 | LONG | 33 | none | 已定义剖面；**不存在样本** |
| CoreAvalon | LONG | 26（`MS.Internal`） | none | 不存在样本 |

## LONG 的 XAML 输出

`BamlXamlWriter.Element.Build` 依据 `BamlDialect.Build3683` 分派并调用 `BuildLong`，因此 LONG 谱系现在产出真正的 XAML，而不再是过去那种 "not implemented" 占位输出。

**嵌套关系**来自记录流，而不是来自偏移链接：`Element` … `EndElement` 与 `ClrObject` … `EndClrObject` 界定了树的边界，所以一个栈就足够。每个树节点记录还带有一个 12 字节的头部，含 `depth`、`parentOffset`、`rightSiblingOffset` 和 `leftElementSiblingsCount`，但不必沿着它们走 —— 之所以在转储中保留，是因为它们让结构具备自校验能力。

**名称**的解析方式与 SHORT 谱系不同：

| | SHORT | LONG |
|---|---|---|
| element type | `ElementStart.TypeId`，**负值**表示 `BamlMapTable._knownTypes` 中的一项（`index = -TypeId`） | `Element.Id` **直接**在 `TypeInfo.TypeId` 中查找；没有负数编码，也没有 known-types 表 |
| attribute | `Property.AttributeId` → `AttributeInfo.Name` | `DynamicProperty.AttributeId` → `AttributeInfo.Name` |
| CLR objects | 不是独立的记录 | `ClrObject.Id` 与 `TypeInfo` 共用**同一个** id 空间 |

`ClrObject` 这一处最初是错的，值得记录下来，因为修复它只需要一行查找。在 `481.baml` 中：

```
TypeInfo typeId=5  "System.Windows.Media.LinearGradient"
ClrObject     id=5                                 <- same id space
TypeInfo typeId=6  "System.Windows.Media.GradientStop"
ClrObject     id=6   (four times, one per stop)
```

把它们标成 `clr#5`/`clr#6` 看起来说得通，却会在一份本来正确的文档里产生六个毫无意义的标签。改为通过同一张表解析后，得到的是包含四个 `<System.Windows.Media.GradientStop>` 元素的 `<System.Windows.Media.LinearGradient>`。

### 已对照源文件验证

`example.baml` 由 `example.xaml` 编译而来，二者都在磁盘上：

```xml
<!-- decompiled -->
<Application1.Example xmlns="using:System;System.Windows;System.Windows.Controls"
                      Background="LightBlue">
  <System.Windows.Controls.Button Height="100px" ID="__El2__">Clicky</…>
  <System.Windows.Controls.TextPanel Foreground="White" FontFamily="Trebuchet MS"
                                     FontSize="72pt" ID="TextArea">Hello, world!</…>
</Application1.Example>
```

```xml
<!-- source -->
<FlowPanel xmlns="using:…" xmlns:def="Definition" def:Language="C#" Background="LightBlue">
  <Button Height="100px" Click="handleClick">Clicky</Button>
  <TextPanel Foreground="White" FontFamily="Trebuchet MS" FontSize="72pt" ID="TextArea">…
```

有三处差异，而且**三处都是正确的 BAML 语义，而非解析器错误**：

1. **根名是 `Application1.Example`，不是 `FlowPanel`。** BAML 中的 `TypeInfo id=0` 确实是 `Application1.Example`：`ac` 把该标记编译进了一个派生自 `FlowPanel` 的生成类，根元素指向的正是这个类。归档中还包含承载它的 `example.dll`。
2. **Button 上的 `ID="__El2__"`。** 这个字符串就在 BAML 里。它是编译器自动分配的 id，源文件中从未提及。
3. **`Click="handleClick"` 不存在。** 编译器把处理函数移进了 `example.dll`，因此该事件不再出现在编译后的形式中。

所以反编译器忠实于字节；这些差异是编译过程造成的。

## 复杂属性是作用域，而不是属性

这是一个真实的 bug，而且代价高昂：它静默丢掉了 `481.baml` 中的 111 棵子树。

LONG 的词汇表为复杂属性打开一个嵌套作用域，并单独关闭它：

```
ComplexDynamicProperty      Int16 AttributeId   opens a scope
  ... the property value ...
EndComplexDynamicProperty   (no payload)        closes it

ClrComplexProperty          string Name         opens a CLR-backed scope
EndClrComplexProperty       (no payload)        closes it
```

把 `ComplexDynamicProperty` 当作空属性处理，会产生没有闭合标签的 `<Fill>`，于是作用域内的一切都变成了**外层元素**的子节点，而不是该属性的内容。输出仍能作为 XML 解析，表面上也看似合理，这正是它需要结构检查而不是靠肉眼看的原因。

修复方式是把一个属性元素压入树栈，并在匹配的 `EndComplex*` 记录处弹出。`481.baml` 现在渲染为：

```xml
<System.Windows.Shapes.Path Data="M 0 0 L 149.6 0 L 149.6 238.81 L 0 238.81 Z">
  <Fill>
    <System.Windows.Media.LinearGradient>
      <System.Windows.Media.GradientStop Offset="0.03" Color="#005190DA" />
      <System.Windows.Media.GradientStop Offset="0.46" Color="#BB5190DA" />
      <System.Windows.Media.GradientStop Offset="0.59" Color="#935190DA" />
      <System.Windows.Media.GradientStop Offset="1" Color="#005190DA" />
    </System.Windows.Media.LinearGradient>
  </Fill>
  <TransformEffect>
    <System.Windows.Media.Transform>
      <System.Windows.Media.TranslateTransform Y="-119" X="-74.8" />
      <System.Windows.Media.RotateTransform Center="0,0" Angle="8.6" />
    </System.Windows.Media.Transform>
  </TransformEffect>
</System.Windows.Shapes.Path>
```

### 抓住它的结构检查

标签配平，不需要任何格式知识：

```
example.baml   open=3    close=3    selfclosed=0     balanced=YES
481.baml       open=648  close=300  selfclosed=348   balanced=YES
```

在修复之前，`481.baml` 是不配平的。这一点值得保留为回归断言：一个产出无法解析的标记的反编译器就是错的，无论记录层看起来多么正常，而配平是对此最廉价的检验。

### 两个 LONG 样本中的记录使用情况

有助于了解究竟哪些路径被实际走到：

| record | `example.baml` (3683) | `481.baml` (4015) |
|---|---|---|
| ClrProperty | — | 747 |
| ClrObject / EndClrObject | — | 464 / 464 |
| ComplexDynamicProperty / End | — | **111 / 111** |
| DynamicProperty | 7 | 65 |
| ClrComplexProperty / End | — | 11 / 11 |
| Element / EndElement | 3 / 3 | 62 / 62 |
| DynamicPropertyCustom | — | 7 |
| TypeInfo | 7 | 16 |
| AttributeInfo | 7 | 13 |
| Text | 2 | — |

`example.baml` 是一份规模很小、年代很早的文档；`481.baml` 才真正走到了 CLR 与复杂属性的机制。任何对 LONG 写出器的改动都应先在 `481.baml` 上检验。


## 3718 / 4033 / 4039 / 4042 都不存在样本

这四个剖面来自各世代定义的记录码集合，LONG 的 XAML 写出器在整个谱系上也是通用的，但**从未有任何真实字节走到过它们**。这一点通过两种方式确认，两种方式在这个问题上都可靠：

1. **资源名扫描。** 对每个构建树中的每个 `.dll` 和 `.exe` 搜索 `.baml` 资源名。结果：四者之中皆无。正是这项检验在 4074 和 4093 中成功定位到了 WCPClient 语料，所以它的沉默是有信息量的，而不是搜索本身的局限。
2. **清单普查。** `tools\survey_resx.py` 在每个树中只找到字符串表（`ExceptionStringTable`、`ui.resx`、`Images`、`TrustDialog`、`MessageStringTable`）—— 没有 `.g.resx`，因此也没有内嵌的已编译 XAML。

`4042\Microsoft.NET\Avalon\Microsoft.Windows.WCPClient.dll` 曾是最有希望的线索，因为 4042 构建中的一个 WCPClient 程序集按理说可能携带标记。它有 126,976 字节，且**不含**任何 `.baml` 资源名。

曾尝试对 LONG 框架做字节特征扫描，但**因不可用而放弃**：模式 `[int64 size][int16 type]` 会持续命中普通的 PE 数据（`size=98` 几乎在每个程序集中都会反复出现）。它产生的误报远多于信号，无法替代资源名检验。

所以诚实的结论是：解析器支持该谱系，但它的六个剖面中有四个未经验证。任何手上有 3718、4033、4039 或 4042 时期 BAML 样本的人都能立刻补上这一缺口，因为剖面表和写出器无需任何改动即可消费它。



## 4083 不需要剖面：它在字节流上就是 4074

4083 与 4074 的记录码表定义了**相同的 34 个成员、相同的顺序** —— 编译器侧（`PresentationBuildTasks`）和运行时侧（`PresentationFramework\System.Windows.Serialization`）都是如此。

4074 与 4083 之间有五类运行时记录不同，而其中唯一可能有影响的那类并没有影响：两代中 `PropertyCustom` 记录都在载荷之前只携带 `AttributeId`，因此 4083 与 4074 的字节流读取方式完全相同。4083 新增的字段（`_serializerType`、`_parserContext`、`SerializerType`、`ParserContext`）可能会被**写入**路径填充，但**读取**路径从不消费，因此它们不会出现在字节流中。

结论：4083 与 4074 共用一个剖面，其差异在字节流中不可见。这是数据本身的属性，而不是此处的缺口。如果某个 4083 样本在 4074 剖面下发生失步，上面这五类不同的记录就是该查的地方。

## 4093 是一种真正不同的载荷格式：五个不同的记录码

对 4074 与 4093 的 `System.Windows.Serialization` 记录集合做比较，五个记录码不同，另有四个在 4074 中没有对应者：

| 记录 | 与 4074 的差异 |
|---|---|
| `DocumentStart` | 写入并校验的版本元组是 `(0, 1)`，而 4074 是 `(0, 0)`；差异在于该记录携带并检查的元组本身，而不是某个载荷 |
| `PropertyCustom` | 两个代次的载荷不同 |
| `TypeInfo` | 载荷不同：程序集 id 字段同时携带标志位（见下文） |
| `AttributeInfo` | 载荷不同：该记录携带一个 usage 字节；已应用，但仅凭这一改动字节流仍然失步（见下文） |
| `DefArrayStart` | 仅 4093 定义，4074 中没有对应者（见下文） |
| `DefArrayEnd` | 仅 4093 定义，4074 中没有对应者（见下文） |
| `ResourceInfo` | 仅 4093 定义，4074 中没有对应者（见下文） |
| `PropertyResourceReference` | 仅 4093 定义，4074 中没有对应者（见下文） |

### 已解决：`TypeInfo` 把标志位与程序集 id 打包在一起

该记录按以下顺序携带载荷：一个 16 位类型 id，然后一个 16 位字段 —— 其高四位是 `TypeInfoFlags`（`DemandLoadChildren = 1`、`UnusedOne/Two/Three`），低十二位是程序集 id —— 最后是作为长度前缀字符串的类型名。这个拆分就是一条简单的掩码规则：

```
flags      = (field >> 12) & 0xF
assemblyId =  field       & 0xFFF
```

朴素地读取该字段会得到无意义的结果，例如 id 为 0 时读出 `assemblyId=4096`，而这正是第一次 4093 尝试所产出的东西。`RecordProfile.PacksTypeInfoFlags` 现在承载了这一信息，读取器会报告 `assemblyId=0 typeFlags=1`。

### 已解决：`AttributeInfo` 携带一个 usage 字节

载荷依次为：一个 16 位属性 id、一个 16 位所属类型 id、一个单字节 usage 值，最后是作为长度前缀字符串的名称 —— usage 字节位于两个 id 与名称之间。`RecordProfile.AttributeInfoHasUsage` 承载这一信息；而它本应终结的失步并没有仅靠载荷改动就停止：code 33 是同一条记录上第二处彼此独立的缺陷（见下文的大小分类）。下面的走查中 `AttributeInfo` 恰好消耗完声明的长度，因此该布局是被实际走到过的，而不是假设的。

### 已解决：两处大小分类有误

二者都是把 4093 表从 4074 表推导出来时的机械性疏漏，并且产生了相同的症状 —— 在错误偏移处出现一条看似合理的记录：

| code | record | wrong | right | why |
|---|---|---|---|---|
| 33 | `AttributeInfo` | fixed | **variable** | `BamlAttributeInfoRecord : BamlVariableSizedRecord`，而 4093 的 `AttributeInfo` 位于 33（4074 中它在 32） |
| 35 | `PropertyResourceReference` | fixed + Int16 | **variable** | `BamlPropertyResourceReferenceRecord : BamlPropertyRecord : BamlStringValueRecord : BamlVariableSizedRecord` |

分类错误的记录是这里代价最高的一类错误，因为在本应有一个 4 字节大小字段的地方只读取 2 字节载荷并不会立刻失败 —— 它会落在一对经常看起来像合法 code 的字节上，于是走查还会继续一段时间才崩溃。这就是让症状看起来像载荷问题的原因。

### 这两处错误分类是如何被找出的

把字节流走查两遍，并为每条记录把**声明结束**与**读取其字段后到达的偏移**列表对照：

```
off     name                     size   declaredEnd readTo   ok
0       DocumentStart            41     43       43       OK
192     DefAttribute             28     222      222      OK
222     AttributeInfo            17     241      241      OK
...
619     TypeInfo                 45     666      666      OK
670     DefAttribute             77     749      749      OK
766     ResourceInfo             42     810      810      OK
810     PropertyResourceReference (fixed) 814     818      MISMATCH by +4
```

两列首次出现不一致的那一行就是损坏的记录，无需任何猜测。这远比手工推理原始字节可靠；后者在本次调查中产生过两个错误结论（一个幻想的 "DefAttribute 之前少了 4 个字节"，以及一个误以为 `ElementStart` 没有载荷的判断）。

### 四条 4093 独有的记录

各记录的字节流布局，按字段出现的顺序：

| 记录 | 载荷 |
|---|---|
| `DefArrayStart` | 沿用 `ElementStart` 的载荷：一个 16 位类型 id |
| `DefArrayEnd` | 无载荷 |
| `ResourceInfo` | 一个 16 位资源 id，然后是作为长度前缀字符串的值 |
| `PropertyResourceReference` | 一个 16 位属性 id，然后是一个 16 位资源 id |

四条都已加入 `RecordProfile.Build4093`。其中 `ResourceInfo` 在上面的走查中确实被走到，其字段恰好消耗完声明的长度；`DefArrayStart` 与 `DefArrayEnd` 没有出现在本文档记录的任何一次走查中，因此这两条的布局只是按字节流行为陈述，仍然**未经验证**。

### 4093 已验证

```
installationerror.baml         22 records   clean
installationcancelled.baml     26 records   clean
installationprogress.baml      32 records   clean
trustuicontent.baml          1157 records   clean
themes-classic.baml          3986 records   clean   (51,684 bytes)
```

并且它能反编译为 XAML：

```xml
<TextPanel xmlns="http://schemas.microsoft.com/2003/xaml" xmlns:def="Definition"
           XmlSpace="preserve" FontSize="20" ID="_El1_">
    Application installation cancelled. To continue installation, click the link below:
  <HyperLink FontSize="20" ID="AppHyperLinkID">Install App</HyperLink>
</TextPanel>
```


## LONG 标记按 CLR 类型而非前缀命名元素

两个谱系描述命名空间的方式不同，这就是 LONG 输出放在 SHORT 输出旁边显得不寻常的原因。

`481.baml` (4015) 只声明了一个命名空间，而且是一条没有 URI、也没有前缀的 `using:` 指令：

```
XmlnsProperty  prefix=""  value="using:System;System.Windows;System.Windows.Controls;
    System.Windows.Documents;System.Windows.Media;System.Windows.Media.Animation;
    System.Windows.Navigation;System.Windows.Presenters;System.Windows.Shapes;
    System.Windows.Explorer#System.Windows.Desktop"
```

此后一切均以完整的 CLR 类型名引用：

```
typeId=0   System.Windows.Controls.Canvas          (assemblyId=0  System.Windows)
typeId=5   System.Windows.Media.LinearGradient
typeId=6   System.Windows.Media.GradientStop
typeId=8   System.Windows.Media.TranslateTransform
typeId=10  System.Windows.Media.Animation.FloatAnimation
typeId=15  System.Windows.Desktop.ImageResource    (assemblyId=1  System.Windows.Explorer)
```

相比之下，`4074` 写出 `xmlns="http://schemas.microsoft.com/2005/xaml/"`，并且可以使用无前缀的本地名。XML URI 命名空间与前缀机制是随更晚的代次一起到来的；更早的那个代次是纯粹面向 CLR 的格式。

因此在 LONG 输出中写出完整类型名忠实于该指令，而不是命名 bug。唯一仍能用上前缀的地方是 `GenericAttribute`，它当前渲染为 `{uri}local` —— 合法的 XML，但不是真正的写出器会产出的形式。它被列为已知偏差，而不是被悄悄改掉。
## 已知偏差与有意省略

记录在此，以免日后被误认为 bug。

### `GenericAttribute` 渲染为 `{uri}local`

该记录携带 `namespaceUri`、`localName` 和 `value`，写出器把属性输出为 `{uri}local` —— 这是 XAML 中在作用域内没有前缀时命名某命名空间属性的标准方式。它合法且无损，但真正的写出器会声明一个前缀并输出 `prefix:local`。

**有意保持原样。** `GenericAttribute` 在 `samples\` 下大约 350 个样本中出现**零**次，因此任何前缀分配方案都无法验证，而对命名做一处无法验证的改动，比一个有文档记录且合法的回退方案更糟。

### 集合属性没有样本

`ClrArrayProperty`、`IListProperty` 和 `IDictionaryProperty` 各自打开一个具名作用域，并由 `EndClrArrayProperty` / `EndIListProperty` / `EndIDictionaryProperty` 关闭。现在这三者都会打开一个属性元素并关闭它，与复杂属性的处理方式一致。

在此之前它们会落到默认分支，于是其子节点挂到外层元素上且没有闭合标签 —— 与复杂属性 bug 属于同一类缺陷。它是靠主动查找发现的，而不是靠某个失败样本暴露的。现有样本中没有包含这些记录，因此该修复是**预防性的且未经验证**。

## GUI 中的从右到左布局

阿拉伯语和希伯来语会镜像界面：

```
en-US  IsRightToLeft=False  formRTL=No   RTLlayout=False  boxRTL=No
ar-SA  IsRightToLeft=True   formRTL=Yes  RTLlayout=True   boxRTL=No
he-IL  IsRightToLeft=True   formRTL=Yes  RTLlayout=True   boxRTL=No
zh-CN  IsRightToLeft=False  formRTL=No   RTLlayout=False  boxRTL=No
```

`Localization.IsRightToLeft` 显式列出这两个 RTL 标记，而不是去查询 `CultureInfo`，因为在 .NET Framework 上 `CultureInfo` 的 `TextInfo` 无法可靠地报告方向，而列举出来比根据文字脚本推断更可预测。

`ApplyLanguage` 在赋任何文本**之前**设置 `RightToLeft` 和 `RightToLeftLayout`，因此尺寸计算时镜像布局已经就位。

### 内容面板保持从左到右

每种语言（包括 RTL 语言）都是 `boxRTL=No`。由 `NewText()` 构建的每个文本框都承载机器可读的输出 —— 记录转储、XAML、驻留表 —— 其方向是数据本身的属性，而不是界面语言的属性。若不固定它，选择阿拉伯语会把十六进制偏移和标记翻成从右到左，使其无法阅读。该决定放在唯一的 `NewText()` 工厂中，以免各标签页之间出现漂移。

### 探测过程中修掉的两处潜在索引缺陷

`ApplyLanguage` 从构造函数中运行，并直接索引了 `_tabs.TabPages[0]` 和 `_files.Columns[0..2]` 而未检查数量，因此一个标签集为空或列数更少的构建会在构造期间抛异常。现在两处都已加保护。

这些问题之所以浮出水面，是因为探测脚本为每种语言构建了一个窗体。第一个失败看起来像是 `ar-SA` 中的 RTL bug；只有在各自独立的进程中运行每种语言后，才看出它是在同一进程内构造多个窗体所造成的假象。复用进程状态的探测脚本会制造出并不存在的失败。
## 4093 的值编码与 4074 完全相同

经过查证而非假设，因为两代的记录载荷确有差异。
对真实的 4093 值做解码可见，其打包标签/宽度编码与 4074 读取器所实现的完全一致：

```
Length / FontSize 载荷，以一个前导字节 b 开始：

  若 (b & 0x80) == 0        —— 纯像素值：该字节本身即数值
  否则
      unit  = b & 0x1F      —— 0 Auto、1 Percent、2 Pixel
      width = b & 0xE0
         0xA0  —— 随后跟 Int32
         0x80  —— 随后跟 byte
         0xC0  —— 随后跟 Int16
         0xE0  —— 随后跟 double
```

画刷以一个字首判别值开始：`00` 后跟带长度前缀的字符串，或 `01` 后跟打包为 uint 的 ARGB。
字号使用同一套标签/宽度方案，`FontSizeType` 位于低五位。因此 4093 无需新增解码器，
这也正是 4093 语料达到与 4074 相同解码率的原因：6,803 个属性、6,802 个解码成功，
唯一剩余项即前文所述的 `Center="23 17"` 文本字符串假阳性。
## 以上种种都不影响代次检测

`BamlDialectShort` 依据 `FormatVersion` 元组打分，SHORT 家族的四个观测结果都符合预期：

```
4074 corpus (103 files)   tuple (0,0)   100%   accepted
HelloWorld-Longhorn       tuple (0,0)   100%   accepted
4093 extracted (5 files)  tuple (0,1)   100%   accepted by the 4093 reader
HelloWorld-AvalonCTP      tuple (0,2)    50%   refused
```

正是元组要求阻止了用 4074 的 code 表去解析 4093 字节流 —— 在那张表里 code 33 是 `AttributeInfo` 而不是 `ResourceInfo`。

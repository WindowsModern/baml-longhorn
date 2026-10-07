# Build-4074 BAML —— 依据正确反编译结果确定的格式

## 决定性的修正：枚举在不同 build 之间并不相同

早先的尝试使用了 **6.0.4051.31026**（`4093 - PresentationFramework`）的反编译结果，其 `BamlRecordType` 有 **36** 个成员，`DefArrayStart`/`DefArrayEnd` 位于 24/25。而语料库是 **build 4074**，其 `BamlRecordType` 只有 **34** 个成员，且顺序*不同*。正是这一处不匹配，导致走查（字节流）卡在 57/103。

权威来源：
`E:\Profiles\Bruce\Desktop\4074 - PresentationFramework True\System.Windows.Serialization\`

| file | fact |
|---|---|
| `BamlRecord.cs` | `RecordTypeFieldLength = 2`；`BamlWriterVersion = new VersionTuple(0, 0)` |
| `BamlRecordType.cs` | `enum BamlRecordType : short` —— **34** 个成员，顺序 == 编码 |
| `BamlVariableSizedRecord.cs` | `RecordSizeFieldLength = 4`；大小 = 大小字段 + 载荷 |
| `BamlRecordManager.cs` | `ReadNextRecord`：先 `ReadInt16()` 读类型，再 `LoadRecordSize`，再 `LoadRecordData` |
| each `Baml*Record.cs` | 各自的 `LoadRecordData` 方法体 |

## 记录类型编码（34 个成员，顺序 == 编码）

```
 0 Unknown                   17 RoutedEvent
 1 DocumentStart             18 ClrEvent
 2 DocumentEnd               19 XmlnsProperty
 3 ElementStart              20 XmlAttribute
 4 ElementEnd                21 ProcessingInstruction
 5 Property                  22 Comment
 6 PropertyCustom            23 IncludeTag
 7 PropertyComplexStart      24 DefTag
 8 PropertyComplexEnd        25 DefAttribute
 9 PropertyArrayStart        26 EndAttributes
10 PropertyArrayEnd          27 EndStartElement
11 PropertyIListStart        28 PIMapping
12 PropertyIListEnd          29 AssemblyInfo
13 PropertyIDictionaryStart  30 TypeInfo
14 PropertyIDictionaryEnd    31 TypeSerializerInfo
15 LiteralContent            32 AttributeInfo
16 Text                      33 LastRecordType
```

**编码 20、21、22、24、26、27 没有对应的记录类** ——
`BamlRecordManager.AllocateRecord` 对它们返回 `null`，因此它们永远不会是活动记录。本 build 中**不存在 `DefArrayStart`/`DefArrayEnd`**。

## 框架

```
<type>  int16 LE   always 2 bytes
<size>  int32 LE   only for BamlVariableSizedRecord subclasses
<payload>

next record = (offset of the size field) + size = recordStart + 2 + size
```

推导自 `BamlVariableSizedRecord.Write`，这是耗时最久的一处：

```csharp
long num = bamlBinaryWriter.Seek(0, SeekOrigin.Current);
bamlBinaryWriter.Write((short)RecordType);
num += 2;                                   // num = SIZE FIELD start
WriteRecordSize(bamlBinaryWriter);
WriteRecordData(bamlBinaryWriter);
long num2 = bamlBinaryWriter.Seek(0, SeekOrigin.Current);
RecordSize = (int)(num2 - num);             // size field + payload
```

与字节实测吻合：偏移 2 处的大小字段读数为 41，而 `2 + 41 = 43` 恰好就是下一条记录类型开始的位置。

## 可变大小与固定大小 —— 依据真实字节修正

`BamlStringValueRecord : BamlVariableSizedRecord`，所以每条字符串记录都带一个 4 字节大小字段。但 `ElementStart` 以及那些派生自 `PropertyComplexStartRecord` 的 `...Start` 记录都是**普通的 `BamlRecord`：只有类型字段，没有大小字段**，其载荷至多只有一个 `Int16`。

这一点被弄错过两次，第二次是靠读取 `HelloWorld.baml` 的字节才发现的：

```
@43  03 00        ElementStart, type only
@45  fd ff        Int16 payload (-3), NO size field
@47  1d 00        next record's type
```

修正后的表：

```python
VARIABLE = {1, 5, 6, 15, 16, 17, 19, 23, 25, 28, 29, 30, 31, 32}

FIXED_WITH_INT16 = {3, 7, 9, 11, 13}    # ElementStart + the four ...Start records
# remaining fixed, no payload: 2, 4, 8, 10, 12, 14, and codes with no class
```

## 证明：两个文件都能完美解码至 EOF

### `HelloWorld.baml`（Longhorn build）—— 226 字节，12 条记录，end=226，无错误

```
@0    DocumentStart   size=41  featureId='PreAlpha' reader=(0,0) updater=(0,0)
                               writer=(0,0) loadAsync=0 maxAsyncRecords=-1
@43   ElementStart    size=4   int16=-3
@47   XmlnsProperty   size=45  prefix='' value='http://schemas.microsoft.com/2005/xaml/'
@94   ElementStart    size=4   int16=-18
@98   AssemblyInfo    size=28  assemblyId=0 fullName='PresentationFramework'
@128  TypeInfo        size=40  typeId=0 assemblyId=0
                               typeFullName='System.Windows.FrameworkElement'
@170  AttributeInfo   size=11  attributeId=0 ownerTypeId=0 name='ID'
@183  Property        size=16  attributeId=0 value='HelloText'
@201  Text            size=17  value='Hello World!'
@220  ElementEnd      size=2
@222  ElementEnd      size=2
```

每一个值在语义上都是正确的 —— XAML 命名空间、程序集、类型、`ID` 属性、`HelloText` 属性以及 `Hello World!` 文本，与同一归档中的 `HelloWorld.xaml` 所声明的完全一致。

### `modulesizer.baml`（主语料库中最小者）—— 184 字节，7 条记录，end=184

```
@0    DocumentStart   size=41  featureId='PreAlpha' reader=(0,0) updater=(0,0)
                               writer=(0,0) loadAsync=0 maxAsyncRecords=-1
@43   AssemblyInfo    size=28  assemblyId=0 fullName='PresentationFramework'
@73   TypeInfo        size=49  typeId=0 assemblyId=0
                               typeFullName='System.Windows.Controls.Primitives.Thumb'
@124  ElementStart    size=4   int16=0
@128  XmlnsProperty   size=50  prefix=''
                               value='http:////schemas.microsoft.com//2005//xaml//'
@180  ElementEnd      size=2
@182  DocumentEnd     size=2
```

注意本语料库的命名空间中**斜杠是双写的**，而 `HelloWorld.baml` 中是单斜杠。两者都是真实字节；双写形式是 Longhorn 时代序列化行为的一个真实怪癖，而非解析产生的伪影。

## 全语料库状态：103 / 103 完全反编译

```
$ python tools/xaml_coverage.py
corpus XAML decompile
  files            : 103
  complete         : 103
  partial          : 0
  no tree / error  : 0
```

最后一个阻塞点是 `PropertyCustom`，而它确实是我自己的误读。`BamlPropertyCustomRecord` 派生自 `BamlPropertyRecord`，但其 `LoadRecordData` **重写**了基类实现，并且只读取 `AttributeId` —— 根本没有字符串：

```csharp
internal class BamlPropertyCustomRecord : BamlPropertyRecord
{
    internal override void LoadRecordData(BinaryReader bamlBinaryReader)
    {
        base.AttributeId = bamlBinaryReader.ReadInt16();
        _valueObjectSet = false;              // no ReadString() at all
    }
}
```

它的值是一个固定宽度的序列化对象，稍后由 `SetValueObject` 消费；其布局取决于属性类型 —— 枚举为 `uint`，否则由 `Length` / `GridLength` / `Spacing` / `Brush` / `Thickness` / `FontSize` 的 `DeserializeFrom` 读取器处理。在那里按字符串读取，会让每一条遇到它的走查都失步，而这正是剩余的全部 51 个文件。现在读取器改为读取 `AttributeId`，并把该记录的其余字节原样记录；这在框架层面是正确的，并把值的解码推迟到类型感知阶段。

## 反编译回 XAML

`baml xaml <file>` 执行编译的逆过程。名称来自字节流自身携带的驻留表：

* `ElementStart.TypeId` 为负 → `BamlMapTable._knownTypes[-TypeId]`
* `ElementStart.TypeId` 非负 → 具有该 `typeId` 的 `TypeInfo` 记录
* `Property.AttributeId` → `AttributeInfo` 记录的 `name`
* `XmlnsProperty` → `xmlns` / `xmlns:prefix`

### 针对已知源码的往返 —— 完全一致

`HelloWorld.baml` 是由归档同时提供的源文件编译而来的：

```xml
<!-- source -->
<Canvas xmlns="http://schemas.microsoft.com/2005/xaml/">
  <Text ID="HelloText">Hello World!</Text>
</Canvas>
```

```xml
<!-- baml xaml HelloWorld-Longhorn-0.0.baml -->
<Canvas>
  <Text ID="HelloText">Hello World!</Text>
</Canvas>
```

唯一的差别是省略了默认 XAML 命名空间，而它是隐含的，因此是冗余的。`ID="HelloText"` 是通过 `AttributeInfo` 还原出来的，而不是靠猜。

### 真实应用程序 BAML，34,720 字节

```xml
<Canvas xmlns="http://schemas.microsoft.com/2003/xaml/" Width="" Height="">
  <Canvas ID="DocumentRootMainScene" Background="" Width="" Height="">
    <Canvas ID="Background2">
      <System.Windows.Controls.TransformDecorator AffectsLayout="false">
        <System.Windows.Media.TranslateTransform X="-0.5" Y="-1.13" />
        <Path ID="Rectangle2_Copy1" Data="M 0 0 L 802 0 L 802 603 L 0 603 Z">
          <System.Windows.Media.LinearGradientBrush EndPoint="1,0.5" StartPoint="0,0.5">
            <System.Windows.Media.GradientStopCollection>
              <System.Windows.Media.GradientStop Color="#FF675BE6" Offset="0" />
              <System.Windows.Media.GradientStop Color="#ADA182EC" Offset="0.25" />
```

类型名、属性名、路径几何、颜色以及动画参数都得到了还原。

## 值解码：5661 个属性值中的 5660 个（99.98%）

由 `tools/value_coverage.py` 在整个语料库上实测：

```
attributes total : 5661
VALUE DECODED    : 5660  (99.98%)
RAW BYTES LEFT   :    1  (0.02%)   -- one 'Center' attribute, 2 bytes
```

### 塑造了这项工作的结构性事实

`PropertyCustom` 的值按照属性的 **CLR 类型**排布，而该类型**并不在字节流中**。原始读取器通过反射获得它：

```csharp
// BamlAttributeInfoRecord
internal Type GetPropertyType()
{
    DependencyProperty dP = DP;
    if (dP == null)
    {
        MethodInfo setter = AttachedPropertySetter;
        if ((object)setter == null) return PropInfo.PropertyType;   // reflection
        return setter.GetParameters()[1].ParameterType;
    }
    return dP.PropertyType;
}

// BamlRecordReader
bamlPropertyRecord.SetValueObject(
    isDp ? ((DependencyProperty)dpOrPi).PropertyType
         : ((PropertyInfo)dpOrPi).PropertyType, reader);
```

因此，离线反编译器无法知道 `PropertyCustom` 值的类型。之所以还能还原它，只是因为这些编码在很大程度上是**自描述的**；而下文每个解码器都通过一个条件来验证：它消费的字节数必须与大小字段所分配的数量完全相等。

### 这些编码，转录如下

| 编码 | 形态 | 字节数 | 来源 |
|---|---|---|---|
| 打包标量 | `[tag]`：`tag & 0x80 == 0` → Pixel，值 = tag；否则单位为 `tag & 0x1F`，宽度由 `tag & 0xE0` 决定（0x80→u8，0xC0→i16，0xA0→i32，0xE0→f64） | 1,2,3,5,9 | `Length.DeserializeFrom` |
| 枚举 | 裸 `uint` | 4 | `BamlPropertyCustomRecord.WriteRecordData` |
| 画刷，Other | `[00][string]` —— 7 位长度，随后是 UTF-8 | 2+len | `Brush.SerializeOn` |
| 画刷，SolidColor | `[01][uint ARGB]` | 5 | `SolidColorBrush.SerializeOn` |
| Thickness | `[count]` 取 1/2/4，随后是相应数量的打包标量 | 不定 | `Thickness.SerializeOn` |

`UnitType` 是一个只有三个成员的枚举，**并非**度量单位列表：

```csharp
public enum UnitType { Auto = 0, Percent = 1, Pixel = 2 }
```

正是这一处弄错，导致早期产生了错误的 `Width="100pt"`；同样的字节 `81 64` 正确读法应是 `Width="100%"`，而 `80 20 03` 应为 `Width="800"`（像素，无后缀）。

### 颜色与画刷 —— 来自 PresentationCore

这两种画刷形式来自 `System.Windows.Serialization.IBamlSerialize` 及其在 **`4074 - PresentationCore`** 中的实现：

```csharp
// Brush.cs
public void SerializeOn(BinaryWriter writer, string stringValue)
{
    writer.Write((byte)0);              // SerializationBrushType.Other
    writer.Write(stringValue);          // BinaryWriter.Write(string)
}
public static object DeserializeFrom(BinaryReader reader)
{
    switch ((SerializationBrushType)reader.ReadByte())
    {
        case SerializationBrushType.Other:      return Parsers.ParseBrush(reader.ReadString(), null);
        case SerializationBrushType.SolidColor: return SolidColorBrush.DeserializeFromReader(reader);
    }
}

// SolidColorBrush.cs
public new void SerializeOn(BinaryWriter writer, string stringValue)
{
    KnownColor knownColor = KnownColors.ColorStringToKnownColor(stringValue);
    if (knownColor != KnownColor.UnknownColor)
    {
        writer.Write((byte)1);
        writer.Write((uint)knownColor);
    }
    else base.SerializeOn(writer, stringValue);
}
internal static object DeserializeFromReader(BinaryReader reader)
{
    return KnownColors.SolidColorBrushFromUint(reader.ReadUInt32());
}
```

关键细节在于 **`KnownColor` 是 `: uint`，其成员本身就是打包的 ARGB 值**，因此这个 `uint` 可以直接渲染：

```csharp
internal enum KnownColor : uint
{
    Black = 4278190080u,   // 0xFF000000
    Blue  = 4278190335u,   // 0xFF0000FF
    ...
}
```

于是 `01 00 00 00 ff` → uint `0xFF000000` → `#FF000000`，这恰好就是语料库中 `DocumentRootMainScene` 的 `Background`。带 `00` 标签的形式是普通的 `BinaryWriter.Write(string)`，因此必须按 7 位长度加 UTF-8 来读取 —— 而不是当作单个长度字节。

注意颜色字符串会以两种拼写、来自两个不同分支：8 位的 `#AARRGGBB` 来自 `SolidColor` 的 uint，6 位的 `#RRGGBB` 来自 `Brush.Other` 字符串。两者都出现在语料库中，且都是正确的。

### `LiteralContent` 携带源码位置

```csharp
Value = ReadString(); ReadInt32(); ReadInt32();   // line, position
```

这两个 `Int32` 是原始 XAML 的行号与列号，这正是为什么一个 6 字符字符串的 `LiteralContent` 记录会占 15 字节。

## 代际判别：版本元组

语料库、`HelloWorld` 文件对以及 4093 资源被直接比对。结论是：**区分各个代际的是 `FormatVersion` 版本元组，而非签名字符串**：

| 样本集 | feature id | reader / updater / writer | 判定 |
|---|---|---|---|
| 4074 语料库（103 个文件） | `PreAlpha` | `0.0 / 0.0 / 0.0` | 接受，100% |
| `HelloWorld-Longhorn-0.0.baml` | `PreAlpha` | `0.0 / 0.0 / 0.0` | 接受，100% |
| `HelloWorld-AvalonCTP-0.2.baml` | `PreAlpha` | `0.2 / 0.2 / 0.2` | **拒绝**，50% |
| 4093 资源（5 个文件） | `PreAlpha` | `0.1 / 0.1 / 0.1` | **拒绝**，50% |
| `481.baml`、`example.baml` | — | LONG 框架 | **拒绝**，0% |

仅凭 feature 标识符作为判别依据毫无价值，因为 4093 那一代写入的也是 `"PreAlpha"`，并且使用*相同*的 SHORT 框架。真正不同的是元组。在加入这项检查之前，把 4093 字节流喂给 4074 读取器时，它能撑过若干条记录，随后死在一个只存在于其中一个枚举而不存在于另一个枚举的编码上：

```
walk stopped at offset 197 of 461: type 33 (LastRecordType) has no record class
walk stopped at offset 192 of 51684: type 27 (EndStartElement) has no record class
```

这两个在 4093 枚举中都是*合法*编码（33 = `ResourceInfo`，27 = `EndStartElement`），这正是失败看起来毫无规律的原因。现在读取器要求**满分**，因此部分匹配会被判为拒绝，而不是自信地误解析。

`BamlDetector.MinConfidence = 100` 编码了这一要求。4074 读取器为框架得 20 分，为合理的大小得 10 分，为签名得 20 分，为 `(0, 0)` 元组得 50 分；4093 字节流停在 50 分，因而被拒绝。

## 从 `.g.resx` 提取更多测试素材

嵌入资源是真实 BAML 最丰富的剩余来源，而这条路径现已自动化。`tools/extract_gresx.py` 解码生成的资源清单中的 base64 `<data>` 条目，并保留每个资源的原始名称作为其路径：

```
python tools/extract_gresx.py <file.g.resx> <outdir> [--list]
```

主语料库最初正是用这种方法从 `Microsoft.Windows.WCPClient.g.resx` 得到的。把它应用到反编译目录树后得到：

| 清单 | 提取结果 |
|---|---|
| `反编译\4093\PresentationFramework\PresentationFramework.g.resx` | `themes/classic.baml` —— **51,684 字节**，目前所见最大的 BAML |
| `反编译\4093\PresentationUI\PresentationUI.g.resx` | 4 个 `.baml`（`installationcancelled` 607、`installationerror` 461、`installationprogress` 631、`trustuicontent` 14,044）外加 4 个 `.ico` 和 4 个 `.png` |

那五个文件以 `samples/extracted-4093/` 的形式交付。它们**不是** 4074 素材 —— 它们是 4093 的，保留它们的用意正是作为**代际判别夹具**：4074 读取器必须拒绝全部五个，而它确实拒绝了。

51,684 字节的 `themes/classic.baml` 是面向未来任何 4093 读取器最有价值的单件产物：它是一份完整的主题字典，比 4074 语料库中的任何东西都大一个数量级。

在 4074 目录树中未发现 `.g.resx`，因此以目前手头的素材，无法用这种方式扩充 4074 语料库。


1. **有一个属性值未解码**：一个 2 字节的 `Center`（`23 17`），其类型未知，且其宽度不匹配任何自描述编码。
2. **一个 4 字节值本质上是有歧义的** —— 它既可能是枚举 `uint`，也可能是打包标量。该歧义由标签的高位来消除，对这里每个样本都正确，但并不构成保证。
3. **LONG 框架世系（3683 / 481）没有 C# 读取器** —— 只有 `docs/reference-bamlread.py` 中的 Python 参考实现。
4. **命名空间前缀不会被重新生成**；命名空间以默认 `xmlns` 声明的形式输出，不过 `def:` 属性会被保留，来自 `XmlnsProperty` 的 `xmlns:prefix` 声明也会被复现。
5. **运行时类型信息不可用**，因此值是按编码而非按声明类型渲染的。见上一节。

## 已反编译的程序集：完整清单与核验

现在有一个整合后的反编译目录树位于 `E:\Profiles\Bruce\Desktop\反编译`，分为 `4074\` 和 `4093\`：

| build | 程序集 |
|---|---|
| 4074 | `PresentationBuildTasks`、`PresentationCore`、`PresentationCore2`、`PresentationFramework`、`System.Windows`、`WindowsBase` |
| 4093 | `PresentationBuildTasks`、`PresentationCore`、`PresentationCore2`、`PresentationFramework`、`PresentationUI`、`System.Windows`、`WindowsBase` |

### 已确认 4074 目录树就是解码器所依据的那一份

`反编译\4074\PresentationFramework\System.Windows.Serialization\BamlRecordType.cs`
的哈希为 `39AD3B637C6848C8`，与本研究全程使用的 `4074 - PresentationFramework True` 副本**逐字节相同**，且两者都有相同的 34 个成员。因此解码器的表可证明来源于正确的 build。

`反编译\4074\System.Windows\MS.Internal\BamlRecordType.cs` 的哈希为
`3370FBA3660523D5`，与早先使用的 LONG 世系反编译结果一致（26 个成员，`MSDotnetAvalon` / `MS.Internal`）。

### 为什么 4093 目录树不可用于本语料库

`反编译\4093\PresentationFramework` 的枚举有 **37** 个成员，多出的四个位于列表中部：

```
... IncludeTag, DefArrayStart, DefArrayEnd, DefTag, DefAttribute, EndAttributes,
    PIMapping, AssemblyInfo, TypeInfo, TypeSerializerInfo, AttributeInfo,
    ResourceInfo, PropertyResourceReference, LastRecordType
```

而 4074 是 34 个：

```
... IncludeTag, DefTag, DefAttribute, EndAttributes, EndStartElement,
    PIMapping, AssemblyInfo, ...
```

`DefArrayStart`/`DefArrayEnd` 插在 `DefTag` 之前，使其后每个编码都发生位移，而 `ResourceInfo`/`PropertyResourceReference` 是追加在末尾的。把 4093 的表用于 4074 语料库，正是早先将走查限制在 57/103 的原因。

### `WindowsBase` 拥有 `FormatVersion` —— DocumentStart 版本号的正确归属

`反编译\4074\WindowsBase\System.IO.CompoundFile\` 包含 `BamlDocumentStartRecord.LoadRecordData` 所依赖的那三个文件：

```
System.IO.CompoundFile\FormatVersion.cs
System.IO.CompoundFile\VersionTuple.cs
System.IO.CompoundFile\ContainerUtilities.cs
```

早先，这些等价文件是从 `System.Windows` 目录树中读出的。`WindowsBase` 中的副本才是权威版本，并且存在一个 `4093\WindowsBase` 的对应版本可供跨代际比较。

## 这些素材覆盖了什么、没有覆盖什么

就 4074 这一代而言，格式本身已被完整覆盖：

* 4074 `PresentationFramework` —— 记录框架、34 成员枚举、每个
  `LoadRecordData`、`BamlMapTable._knownTypes`
* 4074 `PresentationCore` —— `IBamlSerialize`、`Brush`、`SolidColorBrush`、
  `KnownColor`、`Parsers.ParseBrush`，以及各类打包标量类型

而 **LONG 框架世系（build 3683 以及更早的 481 格式文件）无法获得
C# 读取器**：3683 与 481 的反编译结果都不可得，用户也无法提供。`4074\System.Windows` 目录树描述的是该世系的*类*（`MS.Internal.BamlRecord`、`BamlRecordManager` 及其固定的 `[int64 size][int16 type]` 框架），而 `docs/reference-bamlread.py` 中的 Python 参考实现已经把 `example.baml` 和 `481.baml` 走查到 EOF，因此该世系停留在参考实现状态，而不会成为一等方言。它的样本保留在 `samples/reference/` 中作为反向测试：SHORT 读取器必须拒绝它们，而它确实拒绝了。




## 语料库

103 个文件，192,327 字节，9 个主题文件夹，提取自
`Microsoft.Windows.WCPClient.g.resx`。每个文件都从偏移 0 处以
`DocumentStart` 开始（已验证：扫描所有偏移寻找 `int16 == 1`，结果恰好只有一个，位于 0）。

## 工具

| 工具 | 用途 |
|---|---|
| `tools/baml4074.py` | Python 参考解码器，采用权威的 34 成员枚举 |
| `tools/xaml_coverage.py` | 在语料库上驱动 `baml xaml` 并报告覆盖率 |
| `tools/diagnose_walk.py` | 报告每个失败样本的失步偏移与字节 |
| `tools/solve_size_base.py` | 确立了基准 `recordStart + 2` 的暴力搜索 |
| `tools/solve_sizing.py` | 大小分类搜索 |
| `tools/align4074.py` | 证明每个样本的 DocumentStart 都在偏移 0 |
| `tools/field_census.py` | 逐字符串的长度证据 |
| `tools/metadump/` | Longhorn 程序集的元数据读取器 |

### 关于文件编码的说明

早先的 `tools/decode4074.py` 被一次 PowerShell `Set-Content` 往返操作损坏（该文件是 UTF-8，而 shell 按控制台代码页写入），后来由 `tools/baml4074.py` 取代。编辑这些工具时，请使用能识别 UTF-8 的编辑器：shell 文本往返操作至今已损坏过一个 Python 工具和一份 Markdown 文档，各两次。



## 载荷读取器，转录自各个 LoadRecordData

| 记录 | 载荷 |
|---|---|
| DocumentStart | `FormatVersion` + `Boolean LoadAsync` + `Int32 MaxAsyncRecords` |
| ElementStart | `Int16 TypeId` |
| Property / PropertyCustom / RoutedEvent | `Int16 AttributeId` + string |
| PropertyComplex/Array/IList/IDictionary Start | `Int16 AttributeId` |
| ...End records, DocumentEnd, ElementEnd | (none) |
| Text / IncludeTag | string |
| LiteralContent | string + `Int32` + `Int32` |
| DefAttribute | string Value + string Name |
| XmlnsProperty | string Prefix + string Value |
| PIMapping | string Xmlns + string Clrns + `Int16 AssemblyId` |
| AssemblyInfo | `Int16 AssemblyId` + string FullName |
| TypeInfo | `Int16 TypeId` + `Int16 AssemblyId` + string TypeFullName |
| TypeSerializerInfo | TypeInfo + `Int16 SerializerTypeId` |
| AttributeInfo | `Int16 AttributeId` + `Int16 OwnerTypeId` + string Name |

`FormatVersion.Read`（位于 `System.Windows.dll` 反编译结果中的
`MSDotnetAvalon.IO.CompoundFile\FormatVersion.cs`）使用
`new BinaryReader(s, Encoding.Unicode)` 和
`ContainerUtilities.ReadByteLengthPrefixedDWordPaddedUnicodeString`：一个 `Int32`
字节长度、`length/2` 个 UTF-16 字符、DWord 填充 —— 随后 reader/updater/writer 为
三个 `(Int16, Int16)` 对。

## 证明这些表现在是对的

`tools/baml4074.py` 在最小样本上解码出真实、语义正确的内容，走查严格推进且类型名正确：

```
modulesizer.baml (184 bytes) -> 5 records, end=130
  @0    DocumentStart  size=41  featureId='PreAlpha' reader=(0,0) updater=(0,0)
                                writer=(0,0) loadAsync=0 maxAsyncRecords=-1
  @43   AssemblyInfo   size=28  assemblyId=0 fullName='PresentationFramework'
  @73   TypeInfo       size=49  typeId=0 assemblyId=0
                                typeFullName='System.Windows.Controls.Primitives.Thumb'
  @124  ElementStart   size=4   int16=0
  @128  XmlnsProperty  size=2
```

`'PresentationFramework'` 和 `'System.Windows.Controls.Primitives.Thumb'` 正是独立的字符串扫描器所找到的那两个字符串，如今是通过结构而非搜索到达的。这是目前能得到的、关于框架与枚举正确的最强确证。

**一个遗留问题：** 走查在偏移 130 处以 `type 50` 停止，因此该字节流的尾部尚未被正确切分。`@128 XmlnsProperty size=2` 很可疑 —— `XmlnsProperty` 是一个 `BamlStringValueRecord`，本应携带两个字符串 —— 所以要么它的大小字段有误，要么它之前的边界有误。前四条记录肯定是正确的；分歧仅限于元素/属性尾部。

## 语料库

103 个文件，192,327 字节，9 个主题文件夹，提取自
`Microsoft.Windows.WCPClient.g.resx`。每个文件都从偏移 0 处以
`DocumentStart` 开始（已验证：扫描所有偏移寻找 `int16 == 1`，结果恰好只有一个，位于 0）。

## 工具

| 工具 | 用途 |
|---|---|
| `tools/baml4074.py` | 采用权威 34 成员枚举的解码器 |
| `tools/diagnose_walk.py` | 报告每个失败样本的失步偏移与字节 |
| `tools/solve_size_base.py` | 确立了基准 `recordStart + 2` 的暴力搜索 |
| `tools/solve_sizing.py` | 大小分类搜索 |
| `tools/align4074.py` | 证明每个样本的 DocumentStart 都在偏移 0 |
| `tools/field_census.py` | 逐字符串的长度证据 |
| `tools/metadump/` | Longhorn 程序集的元数据读取器 |

### 关于文件编码的说明

`tools/baml4074.py` 取代了早先被一次 PowerShell `Set-Content` 往返操作损坏的 `tools/decode4074.py`（该文件是 UTF-8，而 shell 按控制台代码页写入）。编辑这些工具时，请使用能识别 UTF-8 的编辑器：shell 文本往返操作至今已损坏过一个 Python 工具和一份 Markdown 文档，各两次。

## 下一步

1. 修复尾部：解决偏移 128 处的 `XmlnsProperty size=2` 以及
   130 处的 `type 50`。最可能的原因是 124 处固定大小 `ElementStart` 附近的边界。
2. 把现已确定的表转录进 C# `BamlDialect4074` 读取器，并通过
   CLI 重新运行 103 样本回归。
3. 然后反编译为 XAML 文本：id 通过读取器填充的 `TypeInfo` /
   `AttributeInfo` / `PIMapping` 表来解析。

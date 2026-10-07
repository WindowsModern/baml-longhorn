# 两个 BAML 世代，以及哪个程序集拥有哪一个

状态：**世代归属已由证据确定。** 该语料属于 `PresentationFramework` 世代。两棵 `System.Windows.dll` 构建树都是*另一个*世代。剩余任务范围有限，并在文末点名。

## 三处独立来源验证出的区别

### 世代 A —— `MSDotnetAvalon.Windows` / `MS.Internal`（类记录读取器）

由 `System.Windows.dll` 拥有；此处可用的两棵构建树报告的文件版本为 6.0.4051.31026。它不是标识符：同一文件版本也出现在 4083 和 4093 构建树中，而程序集版本 6.0.3708.0 在全部七棵构建树中都相同。标识构建的是构建文件夹，而不是版本号。

框架来自读取器自身的实际行为：记录长度取自开头的 Int64，记录码取自紧随其后的 Int16。

```
Reading the next record:
  recordSize = Int64 read from the leading size field      -- fixed int64 width
  if recordSize < 0: reject the stream (error 1254)
  recordType = Int16 read from the type field that follows -- fixed int16 width
  ... the record's payload follows
```

写出器与之对应：

```
Writing a record, in order:
  Int64  recordSize
  Int16  recordType
  payload
```

25 个成员的 `MS.Internal.BamlRecordType`：
`Unknown StartDocument EndDocument Element EndElement ParseLiteralContent
XmlnsProperty DynamicProperty DynamicEvent GenericAttribute Text AssemblyInfo
TypeInfo AttributeInfo ComplexDynamicProperty EndComplexDynamicProperty ClrObject
EndClrObject ClrProperty ClrArrayProperty EndClrArrayProperty ClrComplexProperty
EndClrComplexProperty IncludeTag DynamicPropertyCustom LastRecordType`

### 世代 B —— `System.Windows.Serialization`（语料实际采用的格式）

由 `PresentationFramework.dll` 拥有。以下类型由 `metadump` 从其元数据中读出（该程序集在任何现代运行时上都无法执行加载，但其元数据完好）：

```
BamlRecord (abstract)              BamlVariableSizedRecord (abstract)
BamlDocumentStartRecord / End      BamlElementStartRecord / End
BamlPropertyRecord                 BamlStringValueRecord
BamlPropertyCustomRecord           BamlPropertyComplexStart/EndRecord
BamlPropertyArrayStart/EndRecord   BamlPropertyIListStart/EndRecord
BamlPropertyIDictionaryStart/EndRecord
BamlXmlnsPropertyRecord            BamlLiteralContentRecord
BamlTextRecord                     BamlRoutedEventRecord
BamlDefAttributeRecord             BamlIncludeTagRecord
BamlAssemblyInfoRecord             BamlTypeInfoRecord
BamlTypeInfoWithSerializerRecord   BamlAttributeInfoRecord
BamlPIMappingRecord
BamlMapTable / BamlMapTableConstants / BamlNodeInfo / BamlPropertyInfo
BamlObjectFactory / BamlReader / BamlRecordReader
BamlWriter / BamlRecordWriter / BamlRecordManager / BamlRecordType / BamlTreeBuilder
BamlNodeType
```

16 个成员的 `System.Windows.Serialization.BamlNodeType`：

```
None=0  StartDocument=1  EndDocument=2  StartElement=3  EndElement=4
Property=5  XmlnsProperty=6  StartComplexProperty=7  EndComplexProperty=8
LiteralContent=9  Text=10  RoutedEvent=11  Event=12  IncludeReference=13
DefAttribute=14  PIMapping=15
```

记录头，来自该程序集的元数据：

```
Record header widths:
  RecordTypeFieldLength   Int32          -- width of the type field
  BamlWriterVersion       VersionTuple   -- the writer's version
  RecordSizeFieldLength   Int32          -- width of the size field, on variable-sized records
  recordSize              Int32          -- the record's own size, on variable-sized records
```

这两个宽度都是记录类的静态字段，在运行时初始化，而不是由元数据表中的字面量固定下来的。

**两个宽度都是可变的**，这恰恰就是为什么所有固定头部和固定长度链式假设在语料上都失败了。

## 证明语料属于世代 B 而非 A 的证据

1. `tools/longframe_test.py` —— 扫描全部 103 个样本的前 96 字节，查找世代 A 的 `[int64 size][int16 type]` 头部：看起来合理的头部只出现在 **103 个文件中的 1 个**，而在最小的样本中，全文件任何位置都有 **0** 个候选。世代 A 的框架对该语料在结构上不可能成立。
2. 世代 B 的 `BamlNodeType` 名称与样本标签吻合得多：语料中出现的标签 `1d`、`1e`、`13`、`03`、`04`，以及一个 `def:` 属性和双斜杠的 xmlns 值——与世代 B 的 `DefAttribute`、`XmlnsProperty`、`PIMapping`、`IncludeReference` 一致。
3. 本仓库中的 `docs/BUILD4074-FINDINGS.md` 记录了这项经验性的字节级工作（文档头位于 0..27，每个字符串前置 1 字节长度，最小样本中在 52/84/136 处有三个真实字符串）。

## 所提供的两棵 `System.Windows.dll` 构建树都属于世代 A

两棵 `System.Windows.dll` 构建树属于同一世代，而且这一点**直接见于二进制本身**，无需推断：

| 属性 | 4093 `System.Windows` 构建树 | 另一棵 `System.Windows` 构建树 |
|---|---|---|
| AssemblyVersion | `6.0.3708.0` | `6.0.3708.0` |
| FileVersion | `6.0.4051.31026` | `6.0.4051.31026` |
| 含世代 A 记录码 | 是 | 是 |

两者都携带世代 A 的记录词表，且都不含世代 B 才引入的任何记录 ——
`BamlNodeType`、`BamlPIMappingRecord`、`BamlDocumentStartRecord` 与 `BamlElementStartRecord`
在两者的字节流中**均不存在**。

注意：这里的版本号一致，但它**不是**标识符——`6.0.4051.31026` 同样出现在 4083 树中，
而 `6.0.3708.0` 在所考察的全部七棵构建树中完全相同。
真正确立这两者属于同一世代的是它们所定义的**记录词表**，而不是它们报告的版本号。

因此，仍然缺失的是对 `PresentationFramework.dll` 的读取支持。

## 为什么 PresentationFramework 在这里无法直接读取或执行

* 它在 .NET 9 和 .NET Framework 4.8 上**都无法加载执行**：
  `BadImageFormatException 0x8013110E`（"文件已损坏"）—— 现代加载器无法解析这些预发布程序集的引用。
* 它的**元数据完好且可读**，上面的类型清单和枚举正是由此获得的。因此它定义的记录布局来自可观察行为，而非权威读本：在拿到该世代的真实字节进行检验之前，这些布局仍属**未经验证**。

存在三份同源副本，它们的 FileVersion 全相同，但二进制内容各不相同 —— 这再次提醒：标识构建的是文件夹，而不是版本字符串：

```
lhx86\...\PresentationFramework.dll        2,879,488
lhx64\...\PresentationFramework.dll        3,088,384
lh4093x86\...\PresentationFramework.dll    3,166,208
```

## 仍然缺失的内容，以及获取它的两种途径

世代 B 的记录头取决于两个 `static readonly` 宽度（`RecordTypeFieldLength`、`RecordSizeFieldLength`），它们的初始化器不在该程序集的元数据表中 —— 而在静态构造函数的 IL 里。

1. **反汇编这两个静态构造函数。** `System.Reflection.Metadata` 可以读取方法体（`MethodDefinition.RelativeVirtualAddress` →
   `PEReader.GetMethodBody`），所以可以扩展 `metadump` 去走查
   `BamlRecord..cctor` 和 `BamlVariableSizedRecord..cctor` 的 IL，并报告赋给每个宽度的字面量。这是成本最低的路径。
2. **从语料推导宽度。** 观察到的类型标签至少达到 `1d`，而大小字段必须把记录串联起来；在 (typeWidth、sizeWidth，以及大小是否包含其自身字段) 上做双参数搜索，并用"精确走查 103 个文件直到 EOF"作为约束，应当能把它们确定下来。

然后读取每条记录的载荷方法体，对此元数据已经给出了每个字段及其类型。

## 建议的下一步行动

为 `metadump` 增加一个用于这两个 `..cctor` 方法体的 IL 读取器，然后在现有的 `BamlDialect4074`/`BamlDialectCoreAvalon` 读取器之外实现 `BamlDialectApplication`（世代 B），并通过 CLI 重新运行 103 个样本的回归测试。

## 工具

| 工具 | 用途 |
|---|---|
| `tools/metadump/` | 面向 Longhorn 程序集的元数据读取器（类型、字段、方法、枚举）—— **正是它确定了拥有该格式的程序集** |
| `tools/decode4074.py` | 文档头解码 + 对语料的分段走查 |
| `tools/field_census.py` | 每个字符串之前的每个整数；标记长度匹配项 |
| `tools/longframe_test.py` | 证明世代 A 的框架对该语料不可能成立 |
| `tools/framing_search.py`、`chain_search.py`、`header_solver.py`、`id_field.py` | 已被否证的假设，作为负面结果保留 |

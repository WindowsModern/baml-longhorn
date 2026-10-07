# 两个 BAML 世代，以及哪个程序集拥有哪一个

状态：**世代归属已由证据确定。** 该语料属于 `PresentationFramework` 世代。两份反编译出的 `System.Windows.dll` 目录树都是*另一个*世代。剩余任务范围有限，并在文末点名。

## 三处独立来源验证出的区别

### 世代 A —— `MSDotnetAvalon.Windows` / `MS.Internal`（类记录读取器）

由 `System.Windows.dll`（6.0.3708.0 / 6.0.4051.31026）拥有。

框架，引自反编译得到的 `MS.Internal\BamlRecordManager.cs`：

```csharp
internal BamlRecord GetNextRecord(BinaryReader bamlBinaryReader)
{
    long num = bamlBinaryReader.ReadInt64();      // fixed int64 record size
    if (0 > num) { AvUtility.Throw(1254); }
    return GetNextRecord(bamlBinaryReader, num);
}

internal BamlRecord GetNextRecord(BinaryReader bamlBinaryReader, long recordSize)
{
    BamlRecordType bamlRecordType = (BamlRecordType)bamlBinaryReader.ReadInt16();  // fixed int16
    ...
}
```

以及 `MS.Internal\BamlRecord.cs`：

```csharp
bamlBinaryWriter.Write(RecordSize);        // long
bamlBinaryWriter.Write((short)RecordType); // short
WriteRecordData(bamlBinaryWriter);
```

25 个成员的 `MS.Internal.BamlRecordType`：
`Unknown StartDocument EndDocument Element EndElement ParseLiteralContent
XmlnsProperty DynamicProperty DynamicEvent GenericAttribute Text AssemblyInfo
TypeInfo AttributeInfo ComplexDynamicProperty EndComplexDynamicProperty ClrObject
EndClrObject ClrProperty ClrArrayProperty EndClrArrayProperty ClrComplexProperty
EndClrComplexProperty IncludeTag DynamicPropertyCustom LastRecordType`

### 世代 B —— `System.Windows.Serialization`（语料实际采用的格式）

由 **`PresentationFramework.dll` 6.0.4023.30521** 拥有。以下类型由 `metadump` 从其元数据中读出（该程序集在任何现代运行时上都无法执行加载，但其元数据完好）：

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

记录头，来自元数据：

```csharp
abstract class BamlRecord {
    static readonly int RecordTypeFieldLength;        // width of the type field
    static readonly VersionTuple BamlWriterVersion;
}
abstract class BamlVariableSizedRecord : BamlRecord {
    static readonly int RecordSizeFieldLength;        // width of the size field
    int _recordSize;
}
```

**两个宽度都是可变的**，这恰恰就是为什么所有固定头部和固定长度链式假设在语料上都失败了。

## 证明语料属于世代 B 而非 A 的证据

1. `tools/longframe_test.py` —— 扫描全部 103 个样本的前 96 字节，查找世代 A 的 `[int64 size][int16 type]` 头部：看起来合理的头部只出现在 **103 个文件中的 1 个**，而在最小的样本中，全文件任何位置都有 **0** 个候选。世代 A 的框架对该语料在结构上不可能成立。
2. 世代 B 的 `BamlNodeType` 名称与样本标签吻合得多：语料中出现的标签 `1d`、`1e`、`13`、`03`、`04`，以及一个 `def:` 属性和双斜杠的 xmlns 值——与世代 B 的 `DefAttribute`、`XmlnsProperty`、`PIMapping`、`IncludeReference` 一致。
3. 本仓库中的 `docs/BUILD4074-FINDINGS.md` 记录了这项经验性的字节级工作（文档头位于 0..27，每个字符串前置 1 字节长度，最小样本中在 52/84/136 处有三个真实字符串）。

## 用户提供的两棵反编译目录树都属于世代 A

`E:\Profiles\Bruce\Desktop\4093 - System.Windows`（2777 个 .cs 文件）与 `E:\Profiles\Bruce\Desktop\System.Windows`（2778 个 .cs 文件）属于同一世代。BAML 核心部分的文件哈希：

| 文件 | 4093 目录树 | 另一棵目录树 | 结论 |
|---|---|---|---|
| `MS.Internal\BamlRecordType.cs` | `3370FBA3660523D5` | `3370FBA3660523D5` | **相同** |
| `MS.Internal\BamlRecord.cs` | `7F9A2C4B24718D82` | `7F9A2C4B24718D82` | **相同** |
| `MS.Internal\BamlNodeRecord.cs` | `FE64551E6EA57352` | `FE64551E6EA57352` | **相同** |
| `MS.Internal\BamlRecordManager.cs` | `502D5758C42043F4` | `502D5758C42043F4` | **相同** |
| `MS.Internal\BamlReader.cs` | `A1F53B4F3B62E686` | `1DB7968A6064C5B0` | 不同（1131 行 vs 约 1353 行） |

两棵目录树都不包含 `BamlNodeType`、`BamlPIMappingRecord`、`BamlDocumentStartRecord` 或 `BamlElementStartRecord` —— 在全部 2777 个文件中搜索这些名称已证实：**一个都没有**。

因此，仍然缺失的是对 `PresentationFramework.dll` 的反编译。

## 为什么 PresentationFramework 无法在这里直接反编译或执行

* 它在 .NET 9 和 .NET Framework 4.8 上**都无法加载执行**：
  `BadImageFormatException 0x8013110E`（"文件已损坏"）—— 现代加载器无法解析这些预发布程序集的引用。
* 没有安装任何反编译器（`ilspycmd`、`dotPeek`、dnSpy 全都不存在）。
* 它的**元数据完好且可读**，上面的类型清单和枚举正是由此获得的。

存在三份同源副本，它们的 FileVersion 全相同，但二进制内容各不相同 —— 这再次提醒不要凭版本字符串来匹配：

```
lhx86\...\PresentationFramework.dll        2,879,488
lhx64\...\PresentationFramework.dll        3,088,384
lh4093x86\...\PresentationFramework.dll    3,166,208
```

## 仍然缺失的内容，以及获取它的两种途径

世代 B 的记录头取决于两个 `static readonly` 宽度（`RecordTypeFieldLength`、`RecordSizeFieldLength`），它们的初始化器不在元数据表中 —— 而在静态构造函数的 IL 里。

1. **反汇编这两个静态构造函数。** `System.Reflection.Metadata` 可以读取方法体（`MethodDefinition.RelativeVirtualAddress` →
   `PEReader.GetMethodBody`），所以可以扩展 `metadump` 去走查
   `BamlRecord..cctor` 和 `BamlVariableSizedRecord..cctor` 的 IL，并报告赋给每个宽度的字面量。无需反编译器。这是成本最低的路径。
2. **从语料推导宽度。** 观察到的类型标签至少达到 `1d`，而大小字段必须把记录串联起来；在 (typeWidth、sizeWidth，以及大小是否包含其自身字段) 上做双参数搜索，并用"精确走查 103 个文件直到 EOF"作为约束，应当能把它们确定下来。

然后转写每条记录的 `LoadRecordData` 方法体，对此元数据已经给出了每个字段及其类型。

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

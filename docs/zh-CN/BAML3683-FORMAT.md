# LONG 框架谱系（3683、3718）与枚举的演化

`example.baml` 与 `481.baml` 两个样例使用的记录框架，与主要的 4074 语料不同。本文档是读取 **3683** 反编译产物（位于 `反编译\3683\Avalon.Core\MS.Internal\`）并用 C# 读取器走查这两个样例的结果。

## 框架

来自 `3683\Avalon.Core\MS.Internal\BamlRecord.cs`：

```csharp
internal void Write(BinaryWriter bamlBinaryWriter)
{
    int num = (int)bamlBinaryWriter.Seek(0, SeekOrigin.Current);
    if (FilePos == -1) FilePos = num;
    bamlBinaryWriter.Write(RecordSize);           // Int64
    bamlBinaryWriter.Write((short)RecordType);    // Int16
    WriteRecordData(bamlBinaryWriter);
    int num2 = (int)bamlBinaryWriter.Seek(0, SeekOrigin.Current);
    if (RecordSize < 1)
    {
        RecordSize = num2 - num;                  // whole record, from its start
        bamlBinaryWriter.Seek(num, SeekOrigin.Begin);
        bamlBinaryWriter.Write(RecordSize);
        bamlBinaryWriter.Seek(num2, SeekOrigin.Begin);
    }
}
```

```
[int64 size][int16 type][payload]      next record = recordStart + size
```

大小把大小字段、类型字段与载荷一并计入。请注意，这与 4047 起使用的 SHORT 框架是**不同的规则**：在 SHORT 框架中，大小字段只覆盖自身加上载荷，并且仅出现在可变大小的记录上。

## 3683 的枚举 —— 23 个成员

`3683\Avalon.Core\MS.Internal\BamlRecordType.cs`：

```
 0 Uknown                        12 TypeInfo
 1 StartDocument                 13 AttributeInfo
 2 EndDocument                   14 ComplexDynamicProperty
 3 Element                       15 EndComplexDynamicProperty
 4 EndElement                    16 ClrObject
 5 ParseLiteralContent           17 EndClrObject
 6 XmlnsProperty                 18 ClrProperty
 7 DynamicProperty               19 ClrArrayProperty
 8 DynamicEvent                  20 EndClrArrayProperty
 9 GenericAttribute              21 ClrComplexProperty
10 Text                          22 EndClrComplexProperty
11 AssemblyInfo                  23 LastRecordType
```

在反编译产物中，第一个成员确实拼写为 `Uknown`；代码表中保留了这个拼写，以便与源文件保持一致。该构建中**没有 `IncludeTag`**，也**没有 `DynamicPropertyCustom`**，而且命名空间是 `MS.Internal`，不是 `System.Windows.Serialization`。

## 树节点记录

`BamlNodeRecord` 会在记录自身字段之前读取并加上一个 12 字节的节点头：

```csharp
Depth = ReadInt16();
ParentOffset = ReadInt32() + FilePos;           // offset-relative to the record start
RightSiblingOffset = ReadInt32() + FilePos;
LeftElementSiblingsCount = ReadInt16();
```

`Element`（3）、`ParseLiteralContent`（5）、`Text`（10）与 `ClrObject`（16）都从它派生。读取器会把这两个偏移量重新基准化为绝对偏移，这正是它们在转储中有用的原因。

## 载荷布局

其形态与后来的 `System.Windows` 谱系完全相同，因此同一个基于名称的分派即可同时服务两者：

| 记录 | 载荷 |
|---|---|
| StartDocument | `Int32 RootElement`（相对偏移）+ `Int32 MaxAsyncRecords` |
| Element | 节点头 + `Int16 Id`, `Int16 ChildNodes`, `Int16 ElementNodes`, `Int32 FirstChildOffset`（相对偏移） |
| ClrObject | 节点头 + `Int16 Id` |
| Text | 节点头 + string |
| ParseLiteralContent | 节点头 + string + `Int32` line + `Int32` position |
| XmlnsProperty | string Prefix + string Value |
| DynamicProperty / DynamicEvent | `Int16 AttributeId` + string |
| ComplexDynamicProperty | `Int16 AttributeId` |
| GenericAttribute | string NamespaceUri + string LocalName + string Value |
| ClrProperty | string Name + string Value + `Int16 FieldTypeId` |
| ClrArrayProperty / ClrComplexProperty | string Name |
| AssemblyInfo | `Int16 AssemblyId` + string AssemblyFullName |
| TypeInfo | `Int16 TypeId` + `Int16 AssemblyId` + string TypeFullName |
| AttributeInfo | `Int16 AttributeId` + `Int16 OwnerTypeId` + string Name |
| EndDocument, EndElement, EndClrObject, End* | （无） |

## 对样例的验证

`example.baml`（965 字节）**走查 35 条记录后恰好到达 EOF**，而且内容前后连贯：

```
@0    StartDocument   size=19
@19   AssemblyInfo    size=20  assemblyId=0 fullName="example"
@39   TypeInfo        size=35  typeId=0 assemblyId=0 typeFullName="Application1.Example"
@74   Element         size=32  depth=0 parentOffset=-1 rightSiblingOffset=-1
                               leftElementSiblingsCount=0 id=0 childNodes=0
                               elementNodes=0 firstChildOffset=-1
@106  XmlnsProperty   size=63  prefix="" value="using:System;System.Windows;System.Windows.Controls"
@169  AssemblyInfo    size=22  assemblyId=1 fullName="Avalon.UI"
```

这与独立编写的 Python 参考实现 `docs\reference-bamlread.py` 一致，后者同样报告 35 条记录与 965 字节。

`HelloWorld-Longhorn-0.0.baml`（SHORT 框架）会被 LONG 读取器**正确地拒绝**，这补全了用于检测的互斥性质。

## `481.baml` 并非构建 3683 —— 其实际下界是 4015

用每个候选剖面走查 `481.baml`，靠实测而非文件夹上的标签来解答这个问题：

| 剖面 | 成员数 | 在 `481.baml` 上的结果 |
|---|---|---|
| 3683 | 23 | 在 9 条记录后失败：`type 24 not in the 3683 enum` |
| 3718 | 25 | 在 9 条记录后失败：`type 24 (LastRecordType) has no record class` |
| **4015** | **26** | **2149 条记录，end = 43627，无异常** |
| 4033 | 28 | 2149 条记录，无异常 |
| 4039 | 33 | 2149 条记录，无异常 |
| 4042 | 33 | 2149 条记录，无异常 |

所有剖面都能读出的那九条记录是连贯的，因此框架是对的，只是枚举太小。从 4015 起，类型码 24 是 `DynamicPropertyCustom`；而在 3718 中，码 24 的位置是哨兵 `LastRecordType`，它没有对应的类 —— 所以 3718 同样失败。

因此，该合集标注为“481”的文件来自 **4015 时代或更晚**，这恰好与枚举增长所预测的一致：

| 构建 | 成员数 | 命名空间 | 新增内容 |
|---|---|---|---|
| 3683 | 23 | `Avalon.Core\MS.Internal` | — |
| 3718 | 25 | `Avalon.Core\MS.Internal` | `IncludeTag` |
| 4015 | 26 | `System.Windows\MS.Internal` | `DynamicPropertyCustom` |
| 4033 | 28 | `PresentationFramework\System.Windows.Serialization` | `Dynamic*` 重命名为 `DependencyID*`；`IListProperty` |
| 4039 | 33 | `PresentationFramework\MSAvalon.Windows.Serialization` | 字典记录、`DictionaryKeyTag`、`PIMapping`、`ClrPropertyCustom` |
| 4042 | 33 | 与 4039 相同 | — |
| 4074 | 34 | `PresentationFramework\System.Windows.Serialization` | SHORT 框架开始 |

`Dynamic` → `DependencyID` → `Dependency` 的改名贯穿 3718/4015/4033/4039，这是 Avalon 到 WPF 的架构重构波及到线格式的结果。它纯粹是词法层面的，因此同一个基于名称的载荷分派依然能覆盖全部六个剖面。

合集自身的注释称该文件“来自构建 4074，但它是更古老的 BAML 格式” —— 这个标签描述的是*格式*及其年代，而不是构建号。

## 剖面并不总能被唯一确定，而这没关系

`example.baml` 在**全部六个剖面下都能干净地走查**，35 条记录直至 EOF，因为它只使用了 3683 中就已存在的类型码。这些字节确实无法说明是哪个构建写出的它。因此读取器报告**能够干净走查的最旧剖面**，作为数据所能支持的最紧致结论：

```
example.baml  ->  Longhorn LONG framing (3683 profile)
481.baml      ->  Longhorn LONG framing (4015 profile)
```

若改为报告最新的匹配项，就会默默接受文档从未使用过的类型码。一份文档只有在确实需要更晚的类型码时才会收窄剖面 —— 这正是 `481.baml` 有信息量、而 `example.baml` 没有的原因。

## 与 Python 参考实现的交叉核对

`docs\reference-bamlread.py` 是在本读取器出现之前独立编写的，它对 `example.baml` 同样报告 **35 条记录 / 965 字节**，对 `481.baml` 报告 **2149 条记录**。C# 走查器在两者上都一致，并且在 4015 剖面下对 `481.baml` 恰好到达 EOF（43627 字节中的 43627 字节）。

## 为什么这不止关乎两个文件

枚举单调增长（23 → 25 → 26 → 28 → 33 → 33 → 34），而框架只变了一次。这给出了两个独立的判别依据：

* **框架**把 3683..4042 时代与 4074 及更晚的版本区分开
* **枚举大小与成员顺序**区分每个时代内部的不同构建

读取器直接表达了这一点：`RecordProfile` 携带框架、代码表和大小分类，因此新增一代只是加一张表，而不是写一个解析器。`RecordProfile.LongLineage` 按年代顺序列出全部六个剖面，`BamlDialectLong` 则从最旧的开始依次走查。

## 为什么这不止关乎一个文件

枚举单调增长（23 → 25 → 26 → 28 → 33 → 33 → 34），而框架只变了一次。这给出了两个独立的判别依据：

* **框架**把 3683/3718 时代与 4047 及更晚的一切区分开
* **枚举大小与成员顺序**区分每个时代内部的不同构建

读取器现在直接表达了这一点：`RecordProfile` 携带框架、代码表和大小分类，因此新增一代只是加一张表，而不是写一个解析器。`BamlFraming.Long` 目前包含 3683 剖面；3718 剖面是下一个条目。

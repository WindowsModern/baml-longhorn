# Longhorn BAML 格式规范（v0 —— 早期 `MS.Internal` 变体）

**格式变体：** Avalon 早期的 `MS.Internal` BAML，它不同于 4074 与 4093 语料所使用的 `System.Windows.Serialization` 变体。
**考察材料：** 来自 **build 4074 构建树** 的 `System.Windows.dll`（`Microsoft.NET\Avalon\System.Windows.dll`，PKT `a29c01bbd4e39ac5`）。注意它的文件版本 6.0.4051.31026 **不是**标识符：同一文件版本也出现在 4083 和 4093 构建树中，而程序集版本 6.0.3708.0 在所考察的全部七棵构建树中都相同。可靠的标识符是文件夹，不是版本号。
**状态：** 结构由可观察的字节流行为与该变体定义的记录集合确立。**尚未与该变体的真实字节比对验证** —— 没有找到这样的样本，因此该变体虽已实现但未经实际运行。见 §7。

---

## 1. 字节流框架

**没有魔数，也没有版本头。** 该字节流是一串扁平的记录序列
（一直读取，直到某条记录的大小字段 `<= 0`，或剩余字节不足 8 个）。

```
+0   int64   recordSize   // LE, TOTAL record length INCLUDING this 8-byte field
+8   int16   recordType   // LE, BamlRecordType (0..24)
+10  ...     payload      // recordSize - 10 bytes, plus a pad byte when needed
```

> **已更正。** 本规范初稿称 `recordSize` *不包含*那 8 个字节的字段。
> 这是错误的：真正的 reader 以及任何正确的解析器都会执行 `pos += recordSize`，
> 因此它必须覆盖整条记录 —— `8 + 2 + len(payload)`，并向上取整到偶数。确立这一点的
> 调试记录见 `baml/STATUS.md` §2。

填充：记录长度保持为偶数，使下一条记录从偶数偏移处开始；填充字节被计入
`recordSize` **之内**。

经核实的 reader 守卫，按字节流上的实际观察：
```
A record is accepted only when all of these hold:
  at least 8 bytes remain                     -- otherwise stop; no record
  the leading Int64 size is > 0               -- otherwise step back, then stop; no record
  the size does not exceed the bytes remaining
                                              -- otherwise step back, then stop; no record
The accepted record spans `size` bytes.

Framing helper used by the record loop:
  the leading Int64 size must be >= 0         -- a negative size is error 1254,
                                                 "Invalid recordSize in Baml"
  otherwise framing continues for a record of `size` bytes
```

### 1.1 `FilePos` 语义（重要）

`FilePos` 被设为 **recordType** 字段的位置……但 writer 是在写入那 8 字节大小字段
*之前*记录它的，而且**在写入路径上从不重新同步它**。读取路径把 `FilePos = -1`，
并且完全不给它赋值。因此，所有被存储的指针最稳妥的看待方式是
**相对于记录自身框架位置的偏移**，而最安全的 reader 规则是：

> 令 `recordPos` = `recordType` 字段的偏移（= `filePos + 8`）。
> 所有“偏移”字段都相对于记录自身的位置，因此绝对目标地址按
> `target = relativeField + recordPos` 还原（reader 会加上 `base.FilePos`）。

⚠️ 写入路径计算的是 `ParentOffset - base.FilePos` / `RightSiblingOffset - base.FilePos`，
因此**如果 `FilePos` 是 `filePos+8`，writer 会写出负偏移** —— 这自身是自洽的（reader 会把
`FilePos` 加回去），但对第三方解析器很不友好。**在依赖这一点之前，必须与真实字节比对确认**（§7，条目 A）。

---

## 2. 记录类型编码

数值本身就是记录码，顺序即此：

```
0  Unknown                      12 TypeInfo
1  StartDocument                13 AttributeInfo
2  EndDocument                  14 ComplexDynamicProperty
3  Element                      15 EndComplexDynamicProperty
4  EndElement                   16 ClrObject
5  ParseLiteralContent          17 EndClrObject
6  XmlnsProperty                18 ClrProperty
7  DynamicProperty              19 ClrArrayProperty
8  DynamicEvent                 20 EndClrArrayProperty
9  GenericAttribute             21 ClrComplexProperty
10 Text                         22 EndClrComplexProperty
11 AssemblyInfo                 23 IncludeTag
                                24 DynamicPropertyCustom
```

---

## 3. 节点头（仅存在于 `BamlNodeRecord` 子类中）

适用于：**Element (3)、ParseLiteralContent (5)、Text (10)、ClrObject (16)**。
载荷在记录自身字段 *之前*，以这个 12 字节块开始：

```
+0   int16   depth                       // -1 == unset
+2   int32   parentOffset                // RELATIVE to record position
+6   int32   rightSiblingOffset          // RELATIVE to record position
+10  int16   leftElementSiblingsCount
```

这些记录的读写行为：
```
The fields are consumed in this order:
  depth                     Int16
  parentOffset              Int32; the value read is stored as `read + FilePos`
  rightSiblingOffset        Int32; the value read is stored as `read + FilePos`
  leftElementSiblingsCount  Int16
Writing mirrors the read, in the same order, with each offset field
written as its stored value minus `FilePos`.
```

导航辅助方法依赖于符号约定：
```
SeekNextChild: if rightSiblingOffset <= 0, there is no sibling -- stop
               otherwise seek to rightSiblingOffset
SeekToParent : if depth <= 0, there is no parent -- stop
               otherwise seek to parentOffset
```
所以 **`<= 0` 表示“无”** —— 正是相对编码让这一判断得以成立。

---

## 4. 各记录类型的载荷布局

| # | 记录 | 基类 | 12 字节节点头之后的载荷（如适用） |
|---|---|---|---|
| 0 | `Unknown` | BamlRecord | —（不分配；`AllocateRecord` 返回 null） |
| 1 | `StartDocument` | BamlRecord | `int32 rootElementOffset`（相对）、`bool loadAsync`、`int32 maxAsyncRecords` |
| 2 | `EndDocument` | BamlRecord | （空） |
| 3 | `Element` | **Node** | `int16 id`、`int16 childNodes`、`int16 elementNodes`、`int32 firstChildOffset`（相对） |
| 4 | `EndElement` | BamlRecord | （空） |
| 5 | `ParseLiteralContent` | **Node** | `string value`、`int32 lineNumber`、`int32 linePosition` |
| 6 | `XmlnsProperty` | BamlRecord | `string prefix`、`string value` |
| 7 | `DynamicProperty` | BamlRecord | `int16 attributeId`、`string value`、`bool complex` |
| 8 | `DynamicEvent` | BamlRecord | `int16 attributeId`、`string value` |
| 9 | `GenericAttribute` | BamlRecord | `string namespaceUri`、`string localName`、`string value` |
| 10 | `Text` | **Node** | `string value` |
| 11 | `AssemblyInfo` | BamlRecord | `int16 assemblyId`、`string assemblyFullName` |
| 12 | `TypeInfo` | BamlRecord | `int16 typeId`、`int16 assemblyId`、`string typeFullName` |
| 13 | `AttributeInfo` | BamlRecord | `int16 attributeId`、`int16 ownerTypeId`、`string name` |
| 14 | `ComplexDynamicProperty` | BamlRecord | `int16 attributeId` |
| 15 | `EndComplexDynamicProperty` | BamlRecord | （空） |
| 16 | `ClrObject` | **Node** | `int16 id` |
| 17 | `EndClrObject` | BamlRecord | （空） |
| 18 | `ClrProperty` | BamlRecord | `string name`、`string value`、`int16 fieldTypeId` |
| 19 | `ClrArrayProperty` | BamlRecord | `string name` |
| 20 | `EndClrArrayProperty` | BamlRecord | （空） |
| 21 | `ClrComplexProperty` | BamlRecord | `string name` |
| 22 | `EndClrComplexProperty` | BamlRecord | （空） |
| 23 | `IncludeTag` | BamlRecord | `string value` |
| 24 | `DynamicPropertyCustom` | **DynamicProperty** | `int16 attributeId`（+ 可选载荷；见 §6） |

所有字符串都以 **7 位编码的长度前缀 + UTF-8 字节** 表示。
`bool` = 1 字节。所有整数均为小端序。

### 4.1 `Element` 很特殊，且很容易搞错
`Element` 记录属于节点头记录，它会先读取继承来的 12 字节节点头 ——
因此**元素记录的 12 字节节点头之后紧跟它自己的四个字段**，并且
`firstChildOffset` *同样*是相对的。非元素的节点记录（Text、ParseLiteralContent、
ClrObject）也带有该节点头，但没有 `firstChildOffset`。

### 4.2 驻留表
`AssemblyInfo` / `TypeInfo` / `AttributeInfo` 会构建三张以 id 为键的表
（`BamlMapTable`）。元素记录和属性记录存储的是**id，而不是名称** —— 这是相对 XAML
的主要体积优势。`BamlRecordManager` 有意**不**缓存这三类记录
（`AllocateRecord` 会抛出 `"Attempted to allocate uncacheable records"`），因为每一个都是
可变的有状态表输入，而其他所有记录类型都是池化/复用的。

---

## 5. Reader 状态机

`ReadRecord` 是一个针对 `RecordType` 的 switch，它会修改一个
reader 侧的上下文栈。对重新实现而言的关键行为：

- **`EndDocument` 终止解析**：把待挂入的元素加入树中，读取循环返回 `false`，并置 `EndOfDocument = true`。
- **`XmlnsProperty` 只在 Element / ClrObject / ComplexProperty 上下文中生效**，并且它会
  同时写入 reader 级别的 `XmlnsDictionary` 和元素自身已密封的
  `XmlAttributes.XmlnsDictionary(element)`（解封 → 设置 → 密封）。
- **没有 Designer 时 `DynamicEvent` 是致命错误** —— 错误码 1253
  （1253 == `"DynamicEvent in Baml file."`）。也就是说，一个纯 BAML→对象的 reader 要么必须
  被告知如何绑定事件处理器，要么必须拒绝处理。
- **`BamlMapTable.AddNamespaceMap` 是一个空方法**（死钩子）。

每个数值错误 id 都会索引一张 623 项的消息表，查表时执行的是
**`id - 1000`**。这使得每个数值型抛出点的文本都可还原、
可交叉核对 —— 在验证解析器时这是一个非常有用的参照。本领域已知的 id 有：
`1253` DynamicEvent in Baml file · `1254` Invalid recordSize in Baml · `1255` Designer callback is
not support in baml · `1256/1257` ParserContext / xmlns+mapper · `1258/1259` Element/textreader
null · `1266` stream argument is null · `1267` Parser Context is null · `1268` parserContext
argument is null · `1286` Cannot specify multiple roots on an asynchronous Parse call ·
`1287` No Root to Attach Resource to.

---

## 6. 已知的不对称之处 / 需要防范的可疑缺陷

以下是 reader 与 writer 不一致，或者字节流表现得反常的地方。解析器必须选定一种行为并将其记录下来：

1. **`DynamicPropertyCustom`（24）的读/写不对称**：
   读取路径**只**读取 `AttributeId`，而写入路径会写入 `AttributeId`
   **再加上**一个枚举 / `IBamlSerialize` 载荷。此外，当 `TypeDescriptor` 转换失败时，
   writer 还会**把记录类型 short 重写为 `7`（= DynamicProperty）** —— 也就是说，这是一个在写入期发生的回退，
   它*改变了记录在字节流中的身份*。
   → 因此，一个以类型 24 为键的 reader 可能会漏掉 writer 以类型 7 写出的记录。
2. **`XamlTreeBuilderBamlWriter` 总是绕过自定义
   路径** —— 因此在 XAML→BAML 方向上，类型 24 可能永远不会被产生。
3. **`BamlReader` 的 `GenericAttribute` 分支至少在其中一条路径上会丢弃该记录。**
4. **`PropertyManager.DPData` 构造函数中把 `fIsATDP` 字段赋给其自身** —— 读取该区域时
   会报出 CS1717 自赋值；无害，但解析器不应把它当作任何有含义的信号。
5. `Element.RoleProperty` **找不到已知的使用方**（来自属性系统调查的未决
   问题）—— 之所以相关，只是因为 `Role` 参与了 BAML 能够编码的 `PropertySelector`
   匹配。

---

## 7. 必须与真实字节比对验证的内容（按风险排序）

- **A. 偏移编码。** `parentOffset`/`rightSiblingOffset`/`firstChildOffset`/`rootElement`
  是相对于记录位置、相对于大小字段，还是绝对地址存储的？需通过实验解决：
  对第一条 `Element` 记录，解码 `firstChildOffset`，然后检查哪个候选基准恰好落在
  一条有效记录边界上（`int64 size` 后跟 `int16 type in 0..24`）。
- **B. `recordSize` 语义。** 确认 `recordSize == 2 + payloadLen`（包含类型字段），并
  在奇数长度时定位填充字节。
- **C. 最初的记录。** 预期顺序：`StartDocument`，然后是填充 `AssemblyInfo` / `TypeInfo` /
  `AttributeInfo` 表的块，然后是元素树。请确认。
- **D. 字符串编码。** 确认是朴素的 7 位长度 + UTF-8 字符串编码。
- **E. 节点头是否存在。** 在样本中确认哪些记录实际带有 12 字节头
  （例如，元素内部的 `Text` 子节点出现时是否带节点头？）。
- **F. 是否会出现任何 `DynamicPropertyCustom`（24）。**

---

## 8. 难题：BAML 字节究竟从何而来？

解析器需要有 BAML 可读。按价值排序，有三个候选来源：

1. **磁盘上的 `.baml` 文件** —— `lh` 构建树中含有来自
   *较晚* WPF 项目的已构建 BAML（WPF.Themes、Sidebar SDK、AvalonBar）。这些是**现代 WPF BAML**，
   不是 Longhorn 6.0.3708 BAML，因此只能用作*格式对照* —— 记录类型编码和记录集合都不同
   （现代 BAML 有版本头，记录也不同）。**不要用它们来验证 Longhorn 解析器。**
2. **嵌在 Longhorn 自身程序集中的 BAML** —— `TrustManager.xml.cache` 是某个
   安全配置 XAML 文件的 *BAML 缓存*，由 `XamlParser` + `BamlWriter` + `BamlTreeBuilder`
   生成（据 XAML 调查）。如果该缓存作为托管资源嵌入某个 Longhorn 程序集，那么它就是
   一个**真正的、符合当时世代的**样本。首要检查候选：
   `lh\lhx86\Microsoft.NET\Windows\v6.0.4030\` 下的 `System.Windows.dll`、
   `System.Windows.Explorer.dll`、`Microsoft.Windows.Client.dll`、
   `System.Help.Pane.dll`。
3. **合成** —— 用一个小的 XAML 文件驱动该世代自己的 `BamlWriter`/`XamlParser`（重新编译后）
   来输出已知正确的 BAML。置信度最高，但受制于整棵目录树能否编译
   （见尚未解决的约 50 个文件缺口以及缺失的 `CollectionView` 系列）。

---

## 9. 立即可采取的后续步骤

1. 完成 `System.Windows.dll` 副本的暂存，并枚举嵌入的托管资源；
   识别任何 BAML/`.cache` 资源（该任务已委派给一个暂存子代理）。
2. 找到一份真正的 Longhorn 时期 BAML 样本（上述来源 2）。
3. 编写一个**仅**由 §1–§4 驱动的字节级解析器，在样本上运行它，并调和每一处
   不一致 —— 这种调和*就是*剩余的规范工作（解决 §7 的 A–F）。
4. 只有到那时，才构建树重建（深度 + 兄弟/父链接 → 文档树）以及
   反编译（通过三张驻留表把 id → 名称）。

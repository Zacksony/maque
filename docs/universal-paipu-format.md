# 通用牌谱格式 v1

文件标识：

```json
{
  "format": "maque-universal-paipu",
  "formatVersion": 1
}
```

设计目标：

- 不依赖雀魂内部消息类名完成常规复盘；
- 保留来源、协议版本和原始数据校验值；
- 使用稳定、可读、可进行 Git diff 的 JSON；
- 遇到尚未识别的新消息时不猜测其语义。

## 顶层字段

| 字段 | 含义 |
|---|---|
| `gameId` | 对局唯一编号 |
| `source` | 来源平台、分享链接、抓取时间、版本及原始文件校验值 |
| `startedAt` / `endedAt` | UTC ISO 8601 时间 |
| `rules` | 玩家数、东/南场及来源规则编号 |
| `players` | 座位、昵称、段位来源值和终局点数 |
| `rounds` | 按发生顺序排列的局 |
| `unmappedEvents` | 尚未进入任何局且无法映射的来源消息 |

## 座位和局

- 座位统一使用从 `0` 开始的整数。
- `dealerSeat` 是本局庄家的座位。
- `handNumber` 从 `1` 开始。
- `wind` 使用 `east`、`south`、`west`、`north`。
- `initialHands` 的键是座位，值是起手牌列表。

## 牌表示

沿用雀魂的紧凑表示：

- 万子：`1m`～`9m`
- 筒子：`1p`～`9p`
- 索子：`1s`～`9s`
- 字牌：`1z`～`7z`
- 赤五：通常为 `0m`、`0p`、`0s`

当前版本不在导入阶段改写牌名，避免损失来源信息。

## 事件

每个事件至少包含：

- `sequence`：局内从 `0` 开始的顺序；
- `type`：通用事件类型；
- `sourceAction`：用于追踪来源协议消息。

已定义事件：

| `type` | 含义 |
|---|---|
| `draw` | 摸牌 |
| `discard` | 打牌，可带摸切、立直和双立直属性 |
| `riichi-accepted` | 立直成立及扣棒 |
| `call` | 吃、碰或大明杠 |
| `kan` | 暗杠或加杠 |
| `north` | 三麻拔北 |
| `win` | 荣和或自摸，包含役、番、符和点数 |
| `exhaustive-draw` | 荒牌流局 |
| `nagashi-mangan` | 流局满贯 |
| `abortive-draw` | 途中流局 |
| `unknown` | 尚未支持的来源动作 |

`unknown` 事件会把原始动作正文保存为 `rawPayloadBase64`。完整原始牌谱始终另存在 `source.rawPath`，可在解析器升级后重新转换。

## 完整性

`source.rawSha256` 是原始 `.bin` 文件的 SHA-256。分析程序应在重新解析前校验它，避免把损坏或被替换的原始数据与现有 JSON 混用。

格式字段只做向后兼容的增加。若必须改变既有字段语义，应递增 `formatVersion`。

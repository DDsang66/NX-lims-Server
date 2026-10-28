# 终端串口实时监听 —— 前端对接说明

> 后端已就绪，本篇是给前端 Agent 的对接说明，可直接转达。
> 涉及两个接口：终端 A 的**上报**、终端 B 的**订阅**。后端只做转发，不做解析。

---

## 一、两个接口

### 1. 上报（终端 A，被监听方）

```
POST /api/Terminal/report
Content-Type: application/json
```

```json
{
  "terminalId": null,
  "timestamp": "2025-01-01T10:00:00+08:00",
  "payload": { "weight": 12.345, "unit": "g" }
}
```

| 字段 | 必填 | 说明 |
|---|---|---|
| `terminalId` | 否 | **建议不传**（传 `null` 或整个字段省略）。后端会按请求来源 IP 自动识别，避免前端传错或伪造 |
| `timestamp` | 否 | 设备采集时间；省略则后端用接收时间 |
| `payload` | **是** | 串口设备返回的**原始 JSON 对象**。不是字符串，不要再 `JSON.stringify` 包一层 |

返回（项目统一 `Result` 结构）：

```json
{ "isSuccess": true, "value": null, "error": null }
```

失败示例：

```json
{ "isSuccess": false, "value": null, "error": "上报内容缺少 payload（串口原始 JSON）" }
```

失败只有三种可能：载荷为空、缺 `payload`、无法确定终端标识。

### 2. 订阅（终端 B，监听方）

```
GET /api/Terminal/stream        ← SSE 长连接
```

- 不需要请求体、不需要请求头
- 浏览器直接用 `new EventSource('/api/Terminal/stream')` 连
- ⚠️ **`EventSource` 只支持 GET，不能设自定义请求头，也不能 POST**。所以上报必须另用 `fetch`，两者是独立通道

---

## 二、三个 SSE 事件

### `ready` —— 连接建立后立即收到（每次重连也会再收到）

```json
{
  "terminals": ["10.0.0.5", "10.0.0.8"],
  "serverTime": "2025-01-01T10:00:00+08:00"
}
```

- `terminals`：**最近 5 分钟内有上报过的终端 IP 列表**（已按字符串排序）
- 用途：初始化「有哪些终端可监」的勾选面板
- ⚠️ 浏览器自动重连后会**再次**收到该事件，前端请按幂等方式重建列表，并保留用户已有的勾选状态

### `message` —— 每收到一条终端上报就推一次

```json
{
  "terminalId": "10.0.0.5",
  "timestamp": "2025-01-01T10:00:00+08:00",
  "payload": { "weight": 12.345, "unit": "g" }
}
```

- `payload` 就是设备原始 JSON，**字段名与设备输出完全一致**，没有经过后端加工
- `terminalId` 用于前端分流到对应面板

### `heartbeat` —— 每 15 秒一次

```json
{ "ts": "2025-01-01T10:00:15+08:00" }
```

仅用于判断连接是否存活。建议：连续 45 秒收不到任何事件 → 界面上标记「连接中断」（但**不要**自己调 `close()` 后重建，浏览器会自动重连）。

---

## 三、监听页（终端 B）怎么写

```js
const selectedTerminals = new Set();   // 用户勾选的终端，动态增删只改这个集合

const es = new EventSource('/api/Terminal/stream');

// 自定义事件名必须用 addEventListener；es.onmessage 只对 'message' 有效
es.addEventListener('ready', (e) => {
  const data = JSON.parse(e.data);
  renderTerminalPicker(data.terminals);   // 幂等：保留已有勾选
});

es.addEventListener('message', (e) => {
  const data = JSON.parse(e.data);
  if (!selectedTerminals.has(data.terminalId)) return;  // ★ 前端过滤，动态增删在此生效
  renderMessage(data.terminalId, data.payload);
});

es.addEventListener('heartbeat', () => markAlive());

es.onerror = () => markDisconnected();   // 只更新状态，不要 close() 后自己重建

window.addEventListener('beforeunload', () => es.close());
```

**五个必须注意的点**：

1. **只建一个 `EventSource`**，不要为每台终端各建一个 —— 浏览器同域连接数有限，而且完全没有必要
2. **动态增删监听终端 = 只改本地 `selectedTerminals`** —— 不需要调任何后端接口，也不需要重连，零延迟、不丢帧
3. `event.data` 是**字符串**，必须 `JSON.parse`
4. `ready` / `heartbeat` 是**自定义事件名**，必须 `addEventListener`；只有 `message` 可以用 `es.onmessage`
5. 断线由浏览器自动重连（约 3 秒），重连后重新收到 `ready`；页面卸载前记得 `es.close()`

---

## 四、上报端（终端 A）怎么写

```js
async function reportSerialData(rawJsonObject) {
  try {
    await fetch('/api/Terminal/report', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ payload: rawJsonObject })   // 只包一层 payload
    });
  } catch (err) {
    console.warn('[serial-report] 上报失败，不影响串口主流程', err);
  }
}

// 串口回调里：先完成本地处理，再异步上报（不要 await 阻塞主流程）
onSerialData((raw) => {
  handleLocally(raw);
  reportSerialData(raw);        // 不 await
});
```

**四条要求**：

1. **不要 `await` 阻塞串口交互主流程** —— 上报失败绝不能影响设备交互
2. 失败**只记日志、不重试**（实时监听允许丢帧，重试反而可能造成乱序）
3. **天平这类事件级设备可能连发多条** —— 建议在 **50~100ms** 窗口内合并同一设备的连续报文再发，避免瞬间打出一串请求
4. `payload` 直接放**设备返回的 JSON 对象**；不要先 `JSON.stringify` 成字符串再塞进去，那会让监听端拿不到字段

---

## 五、明确未做的部分（避免误解）

| 项 | 现状 | 影响 |
|---|---|---|
| 鉴权 | ❌ 无 | 任何人可上报、可订阅任意终端数据（内网暂可接受，后续随用户体系重构补） |
| 历史回放 | ❌ 无 | 连上之前的数据收不到，只能看到连接之后的新数据 |
| 服务端节流/合批 | ❌ 无 | 突发由前端自行合批 |
| 数据落库 | ❌ 无 | 刷新页面后历史只能靠前端自己缓存 |
| 多副本部署 | ❌ 不支持 | 当前单实例；多副本时订阅与推送可能落在不同副本上 |
| 断线补发 | ❌ 无 | 重连后拿到 `ready` + 后续新事件，中间丢失的补不回来 |

---

## 六、联调自查

1. DevTools → Network → 过滤 `stream`，确认响应类型是 `text/event-stream`、Protocol 为 `h2`
2. 用 curl / Postman 打一次 `POST /api/Terminal/report`，确认监听页收到 `message`
3. 断开网络再恢复，确认自动重连后收到新的 `ready`，且后续 `message` 继续到达
4. 勾选/取消勾选某台终端，确认只是界面过滤，`EventSource` 的 readyState 始终保持 `OPEN`

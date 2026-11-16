<script setup>
/**
 * 概览页：展示服务状态、连接方式与配置信息。
 *
 * 数据来源：App.vue 定时从 /api/system 拉取后通过 props 传入，
 * 因此本组件本身不发起请求，切换页面时也不会产生额外流量。
 */
import { computed, ref } from 'vue'

const props = defineProps({
  /** 后端返回的系统信息，未连接时为 null */
  system: { type: Object, default: null },
  /** 后端是否在线 */
  online: { type: Boolean, default: false },
})

/** 把秒数格式化成「x 天 x 小时 x 分 x 秒」。 */
function formatUptime(seconds) {
  if (typeof seconds !== 'number' || seconds < 0) return '—'
  const d = Math.floor(seconds / 86400)
  const h = Math.floor((seconds % 86400) / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  const s = Math.floor(seconds % 60)

  if (d > 0) return `${d} 天 ${h} 小时 ${m} 分`
  if (h > 0) return `${h} 小时 ${m} 分`
  if (m > 0) return `${m} 分 ${s} 秒`
  return `${s} 秒`
}

/** 顶部的关键指标卡片 */
const stats = computed(() => {
  const s = props.system
  if (!s) return []
  return [
    { label: 'MCP 协议版本', value: s.protocolVersion, hint: 'Streamable HTTP' },
    { label: '已注册工具', value: `${s.toolCount} 个`, hint: '自动反射发现' },
    { label: '笔记数量', value: `${s.noteCount} 条`, hint: 'JSON 文件存储' },
    { label: '已运行', value: formatUptime(s.uptimeSeconds), hint: `进程号 ${s.processId}` },
    { label: '内存占用', value: `${s.workingSetMb} MB`, hint: `${s.processorCount} 个逻辑核心` },
  ]
})

/** 服务地址：页面本身是同源访问，这里展示给外部客户端使用的地址 */
const endpoint = computed(() => {
  if (typeof window === 'undefined') return 'http://localhost:5080/mcp'
  // 生产环境由后端托管前端，端口就是当前页面端口；开发环境 Vite 在 5173，后端在 5080
  const port = window.location.port === '5173' ? '5080' : window.location.port
  const host = window.location.hostname || 'localhost'
  return `${window.location.protocol}//${host}${port ? `:${port}` : ''}/mcp`
})

/** Claude Desktop 的配置（通过 mcp-remote 桥接远程 HTTP 服务） */
const claudeSnippet = computed(() =>
  JSON.stringify(
    {
      mcpServers: {
        mcpdesk: {
          command: 'npx',
          args: ['-y', 'mcp-remote', endpoint.value],
        },
      },
    },
    null,
    2,
  ),
)

/** Cursor 的配置（~/.cursor/mcp.json，支持 url 形式的 HTTP 传输） */
const cursorSnippet = computed(() =>
  JSON.stringify(
    {
      mcpServers: {
        mcpdesk: { url: endpoint.value },
      },
    },
    null,
    2,
  ),
)

/** VS Code 的配置（.vscode/mcp.json） */
const vscodeSnippet = computed(() =>
  JSON.stringify(
    {
      servers: {
        mcpdesk: { type: 'http', url: endpoint.value },
      },
    },
    null,
    2,
  ),
)

/** 复制反馈：记录刚复制的是哪一段，1.5 秒后恢复 */
const copied = ref('')

async function copy(text, key) {
  try {
    await navigator.clipboard.writeText(text)
    copied.value = key
    window.setTimeout(() => {
      if (copied.value === key) copied.value = ''
    }, 1500)
  } catch {
    copied.value = ''
  }
}
</script>

<template>
  <h1 class="page-title">服务概览</h1>
  <p class="page-desc">
    这是一个跑在本机的个人 MCP Server：给 AI 客户端提供时间、计算、笔记、文件、文本处理等工具，
    同时用这个 Vue 界面做可视化调试与管理。
  </p>

  <template v-if="system">
    <!-- 关键指标 -->
    <div class="grid-stats" style="margin-bottom: 18px">
      <div v-for="item in stats" :key="item.label" class="card card-pad">
        <div class="faint">{{ item.label }}</div>
        <div style="font-size: 19px; font-weight: 700; margin: 2px 0">{{ item.value }}</div>
        <div class="faint">{{ item.hint }}</div>
      </div>
    </div>

    <div class="grid-2">
      <!-- 左：运行信息 -->
      <div class="card card-pad">
        <h2 class="card-title">运行信息</h2>

        <div class="field">
          <label>服务名称</label>
          <div class="mono">{{ system.title }}（{{ system.server }} v{{ system.version }}）</div>
        </div>

        <div class="field">
          <label>运行环境</label>
          <div class="mono">{{ system.osDescription }}</div>
          <div class="mono faint">{{ system.framework }} · {{ system.machineName }}</div>
        </div>

        <div class="field">
          <label>MCP 端点</label>
          <div class="mono">{{ endpoint }}</div>
          <div class="hint">支持的协议版本：{{ system.supportedProtocolVersions.join('、') }}</div>
        </div>

        <div class="field">
          <label>文件沙盒根目录</label>
          <div class="mono">{{ system.sandboxRoot }}</div>
          <div class="hint">文件类工具只能访问该目录内的内容，可修改 appsettings.json 的 FileSandboxRoot。</div>
        </div>

        <div class="field">
          <label>笔记数据文件</label>
          <div class="mono">{{ system.dataFile }}</div>
        </div>

        <div class="row">
          <span class="badge" :class="system.apiKeyRequired ? 'warn' : 'ok'">
            {{ system.apiKeyRequired ? '已启用 X-Api-Key 校验' : '本机免鉴权' }}
          </span>
          <span class="badge" :class="system.allowPrivateNetwork ? 'warn' : 'info'">
            {{ system.allowPrivateNetwork ? '允许访问内网地址' : '禁止访问内网地址' }}
          </span>
        </div>
      </div>

      <!-- 右：接入配置 -->
      <div style="display: flex; flex-direction: column; gap: 18px">
        <div class="card card-pad">
          <h2 class="card-title">接入 Claude Desktop</h2>
          <p class="faint" style="margin-top: 0">
            写入 claude_desktop_config.json（Claude 桌面端通过 mcp-remote 桥接 HTTP 服务）。
          </p>
          <pre class="code">{{ claudeSnippet }}</pre>
          <div class="row" style="margin-top: 10px">
            <button class="btn btn-sm" type="button" @click="copy(claudeSnippet, 'claude')">
              {{ copied === 'claude' ? '已复制' : '复制配置' }}
            </button>
          </div>
        </div>

        <div class="card card-pad">
          <h2 class="card-title">接入 Cursor</h2>
          <p class="faint" style="margin-top: 0">写入 ~/.cursor/mcp.json，然后在 Cursor 设置中启用。</p>
          <pre class="code">{{ cursorSnippet }}</pre>
          <div class="row" style="margin-top: 10px">
            <button class="btn btn-sm" type="button" @click="copy(cursorSnippet, 'cursor')">
              {{ copied === 'cursor' ? '已复制' : '复制配置' }}
            </button>
          </div>
        </div>

        <div class="card card-pad">
          <h2 class="card-title">接入 VS Code</h2>
          <p class="faint" style="margin-top: 0">
            在工作区的 .vscode/mcp.json 中写入以下内容，然后在 Copilot Chat 中启用该服务。
          </p>
          <pre class="code">{{ vscodeSnippet }}</pre>
          <div class="row" style="margin-top: 10px">
            <button class="btn btn-sm" type="button" @click="copy(vscodeSnippet, 'vscode')">
              {{ copied === 'vscode' ? '已复制' : '复制配置' }}
            </button>
          </div>
        </div>
      </div>
    </div>

    <div class="alert info" style="margin-top: 18px">
      小提示：配置了 ApiKey 时，HTTP 客户端还需要附带请求头
      <code>X-Api-Key: &lt;你的密钥&gt;</code>，否则 /mcp 会返回 401。
    </div>
  </template>

  <div v-else class="card card-pad empty">
    正在等待后端信息…
  </div>
</template>

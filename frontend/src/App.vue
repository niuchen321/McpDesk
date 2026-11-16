<script setup>
/**
 * 应用根组件
 *
 * 职责：
 *   1. 提供顶部导航，在「概览 / 工具调试 / 笔记 / 调用日志」之间切换；
 *   2. 定时探测后端 /api/system，把在线状态与服务信息传给子组件；
 *   3. 子组件按需创建（v-if 方式），避免不必要的数据请求。
 */
import { computed, onMounted, onUnmounted, ref, shallowRef } from 'vue'
import { api } from './api'
import OverviewPanel from './components/OverviewPanel.vue'
import ToolsPanel from './components/ToolsPanel.vue'
import NotesPanel from './components/NotesPanel.vue'
import LogsPanel from './components/LogsPanel.vue'

// 当前选中的页面 key。用 URL hash 记录，刷新页面后仍停留在原页面，也方便直接分享链接（如 /#tools）
const tabs = [
  { key: 'overview', label: '概览' },
  { key: 'tools', label: '工具调试' },
  { key: 'notes', label: '笔记' },
  { key: 'logs', label: '调用日志' },
]

const tabKeys = tabs.map((tab) => tab.key)

/** 读取当前 hash，非法值回退到「概览」 */
function readHash() {
  const key = window.location.hash.replace(/^#\/?/, '')
  return tabKeys.includes(key) ? key : 'overview'
}

const active = ref(readHash())

/** 切换页面时同步到地址栏 */
function switchTab(key) {
  active.value = key
  if (window.location.hash !== `#${key}`) {
    window.location.hash = key
  }
}

/** 监听浏览器的前进/后退 */
function onHashChange() {
  active.value = readHash()
}

// 后端状态：null 表示还没探测过
const online = ref(false)
const checked = ref(false)
const system = shallowRef(null)
const healthError = ref('')

/**
 * 探测后端状态。
 * 失败时不弹窗，只在右上角把状态点变红，避免打断当前操作。
 */
async function refreshHealth() {
  try {
    system.value = await api.getSystem()
    online.value = true
    healthError.value = ''
  } catch (error) {
    online.value = false
    healthError.value = error.message
  } finally {
    checked.value = true
  }
}

/** 右上角状态文案 */
const statusText = computed(() => {
  if (!checked.value) return '正在连接后端…'
  if (!online.value) return '后端未连接'
  return `${system.value?.server} v${system.value?.version} · 在线`
})

let timer = null

onMounted(() => {
  refreshHealth()
  // 每 15 秒刷新一次在线状态与统计数据；页面切回前台时也刷新
  timer = window.setInterval(refreshHealth, 15000)
  document.addEventListener('visibilitychange', onVisible)
  window.addEventListener('hashchange', onHashChange)
})

onUnmounted(() => {
  if (timer) window.clearInterval(timer)
  document.removeEventListener('visibilitychange', onVisible)
  window.removeEventListener('hashchange', onHashChange)
})

function onVisible() {
  if (document.visibilityState === 'visible') refreshHealth()
}
</script>

<template>
  <header class="app-header">
    <div class="app-header-inner">
      <div class="brand">
        <span class="brand-mark">M</span>
        <span>McpDesk</span>
        <span class="brand-sub">个人 MCP 服务台</span>
      </div>

      <nav class="nav">
        <button
          v-for="tab in tabs"
          :key="tab.key"
          :class="{ active: active === tab.key }"
          type="button"
          @click="switchTab(tab.key)"
        >
          {{ tab.label }}
        </button>
      </nav>

      <div
        class="header-status"
        style="cursor: pointer"
        title="每 15 秒自动刷新，点击可立即刷新"
        @click="refreshHealth"
      >
        <span class="dot" :class="online ? 'online' : (checked ? 'offline' : '')"></span>
        <span>{{ statusText }}</span>
      </div>
    </div>
  </header>

  <main class="app-main">
    <!-- 后端离线时给出明确的排查提示 -->
    <div v-if="checked && !online" class="alert error">
      无法连接后端：{{ healthError }}<br />
      请在另一个终端执行：<code>cd backend/McpDesk.Api &amp;&amp; dotnet run</code>
      （默认监听 http://localhost:5080）
    </div>

    <!--
      只用 v-if 渲染当前页面：
        · 其他页面不会被创建，也就不会发起请求；
        · 概览页需要 system / online，其它页面不接收这两个属性，
          避免未声明的 props 作为普通属性渲染到 DOM 上。
    -->
    <OverviewPanel v-if="active === 'overview'" :system="system" :online="online" />
    <ToolsPanel v-else-if="active === 'tools'" />
    <NotesPanel v-else-if="active === 'notes'" />
    <LogsPanel v-else />
  </main>
</template>

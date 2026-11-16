<script setup>
/**
 * 调用日志页
 *
 * 日志保存在后端内存中（默认保留 200 条，可在 appsettings.json 调整），
 * 每次 AI 客户端（/mcp）或本界面（/api）调用工具都会记录一条。
 *
 * 用途：确认 AI 实际传了哪些参数、返回了什么、耗时多少，是调试 MCP 最直接的手段。
 */
import { onMounted, onUnmounted, ref } from 'vue'
import { api } from '../api'

const logs = ref([])
const loading = ref(false)
const error = ref('')
const autoRefresh = ref(true)

let timer = null

/** 加载最近的日志 */
async function load() {
  loading.value = true
  error.value = ''

  try {
    const data = await api.getLogs(100)
    logs.value = data.logs ?? []
  } catch (err) {
    error.value = err.message
  } finally {
    loading.value = false
  }
}

/** 清空日志 */
async function clear() {
  if (!window.confirm('确定清空全部调用日志吗？')) return

  try {
    await api.clearLogs()
    logs.value = []
  } catch (err) {
    error.value = err.message
  }
}

/** 格式化时间 */
function formatTime(value) {
  if (!value) return ''
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleTimeString('zh-CN', { hour12: false })
}

/** 来源的中文名称 */
function sourceLabel(source) {
  return source === 'mcp' ? 'AI 客户端' : '管理界面'
}

/** 切换自动刷新 */
function toggleAutoRefresh() {
  autoRefresh.value = !autoRefresh.value
}

onMounted(() => {
  load()
  // 每 5 秒静默刷新一次（不显示 loading，避免表格闪烁）
  timer = window.setInterval(() => {
    if (autoRefresh.value && !loading.value) load()
  }, 5000)
})

onUnmounted(() => {
  if (timer) window.clearInterval(timer)
})
</script>

<template>
  <h1 class="page-title">调用日志</h1>
  <p class="page-desc">最近 100 条工具调用（最新在前）。日志仅保存在内存中，服务重启后清空。</p>

  <div v-if="error" class="alert error">{{ error }}</div>

  <div class="card card-pad">
    <div class="row" style="margin-bottom: 14px">
      <button class="btn btn-sm" type="button" :disabled="loading" @click="load">
        {{ loading ? '刷新中…' : '立即刷新' }}
      </button>
      <button class="btn btn-sm" type="button" @click="toggleAutoRefresh">
        自动刷新：{{ autoRefresh ? '开' : '关' }}
      </button>
      <span class="spacer"></span>
      <span class="badge info">{{ logs.length }} 条</span>
      <button class="btn btn-sm btn-danger" type="button" @click="clear">清空</button>
    </div>

    <div class="table-wrap">
      <table>
        <thead>
          <tr>
            <th style="width: 90px">时间</th>
            <th style="width: 90px">来源</th>
            <th style="width: 150px">工具</th>
            <th style="width: 100px">耗时</th>
            <th style="width: 80px">状态</th>
            <th>参数</th>
            <th>结果预览</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="entry in logs" :key="entry.id">
            <td class="mono faint">{{ formatTime(entry.timestamp) }}</td>
            <td>
              <span class="badge" :class="entry.source === 'mcp' ? 'info' : ''">{{ sourceLabel(entry.source) }}</span>
            </td>
            <td class="mono">{{ entry.tool }}</td>
            <td class="mono faint">{{ entry.elapsedMs }} ms</td>
            <td>
              <span class="badge" :class="entry.isError ? 'err' : 'ok'">
                {{ entry.isError ? '失败' : '成功' }}
              </span>
            </td>
            <td class="mono faint" style="max-width: 280px">{{ JSON.stringify(entry.arguments) }}</td>
            <td class="mono faint" style="max-width: 340px; white-space: pre-wrap">{{ entry.preview }}</td>
          </tr>
        </tbody>
      </table>
    </div>

    <div v-if="logs.length === 0" class="empty">
      还没有调用记录。去「工具调试」页调用一个工具试试。
    </div>
  </div>
</template>

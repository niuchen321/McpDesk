<script setup>
/**
 * 工具调试页
 *
 * 左侧：所有已注册工具的名称与简介，支持搜索；
 * 右侧：根据工具 inputSchema 自动生成参数表单，调用后展示结果。
 *
 * 调用走的是 /api/tools/{name}/invoke（普通 REST），
 * 与 MCP 客户端走 /mcp 调用的是同一份工具实现，结果完全一致。
 */
import { computed, onMounted, ref } from 'vue'
import { api } from '../api'
import SchemaForm from './SchemaForm.vue'

// 工具列表与加载状态
const tools = ref([])
const loadingTools = ref(false)
const loadError = ref('')

// 关键词过滤
const keyword = ref('')

// 当前选中的工具名
const selectedName = ref('')

// 表单值与调用状态
const formValues = ref({})
const invoking = ref(false)
const invokeError = ref('')
const result = ref(null)

/** 按关键词过滤后的工具列表 */
const filteredTools = computed(() => {
  const text = keyword.value.trim().toLowerCase()
  if (!text) return tools.value

  return tools.value.filter((tool) =>
    `${tool.name} ${tool.title} ${tool.description}`.toLowerCase().includes(text),
  )
})

/** 当前选中的工具对象 */
const selectedTool = computed(() => tools.value.find((tool) => tool.name === selectedName.value) ?? null)

/** 请求里实际会发送的参数（过滤掉 undefined 与空数组） */
const argumentsPreview = computed(() => {
  const args = {}
  Object.entries(formValues.value).forEach(([key, value]) => {
    if (value === undefined || value === null) return
    if (Array.isArray(value) && value.length === 0) return
    args[key] = value
  })
  return args
})

/** 展示用的 MCP 请求示例，帮助理解协议 */
const mcpPreview = computed(() =>
  JSON.stringify(
    {
      jsonrpc: '2.0',
      id: 1,
      method: 'tools/call',
      params: { name: selectedName.value, arguments: argumentsPreview.value },
    },
    null,
    2,
  ),
)

/**
 * 根据 inputSchema 的默认值初始化表单。
 * 只填入有 default 的字段，其余保持 undefined，让后端使用自己的默认逻辑。
 */
function buildDefaults(tool) {
  const values = {}
  const properties = tool?.inputSchema?.properties ?? {}

  Object.entries(properties).forEach(([name, definition]) => {
    if (definition && definition.default !== undefined) {
      values[name] = definition.default
    }
  })

  return values
}

/** 选中某个工具 */
function selectTool(tool) {
  selectedName.value = tool.name
  formValues.value = buildDefaults(tool)
  result.value = null
  invokeError.value = ''
}

/** 加载工具列表 */
async function loadTools() {
  loadingTools.value = true
  loadError.value = ''

  try {
    const data = await api.getTools()
    tools.value = data.tools ?? []

    // 保持原选择；若原选择不存在则选第一个
    if (!tools.value.some((tool) => tool.name === selectedName.value)) {
      const first = tools.value[0]
      if (first) selectTool(first)
    }
  } catch (error) {
    loadError.value = error.message
  } finally {
    loadingTools.value = false
  }
}

/** 调用当前工具 */
async function invoke() {
  if (!selectedTool.value) return

  invoking.value = true
  invokeError.value = ''
  result.value = null

  try {
    result.value = await api.invokeTool(selectedTool.value.name, argumentsPreview.value)
  } catch (error) {
    invokeError.value = error.message
  } finally {
    invoking.value = false
  }
}

/** 重置表单为默认值 */
function resetForm() {
  if (selectedTool.value) {
    formValues.value = buildDefaults(selectedTool.value)
  }
  result.value = null
  invokeError.value = ''
}

/** 复制调用结果 */
const copied = ref(false)
async function copyResult() {
  if (!result.value) return
  try {
    await navigator.clipboard.writeText(result.value.text)
    copied.value = true
    window.setTimeout(() => (copied.value = false), 1500)
  } catch {
    copied.value = false
  }
}

onMounted(loadTools)
</script>

<template>
  <h1 class="page-title">工具调试</h1>
  <p class="page-desc">选择一个工具，填写参数并调用；调用记录会出现在「调用日志」页。</p>

  <div v-if="loadError" class="alert error">
    加载工具列表失败：{{ loadError }}
    <button class="btn btn-sm" type="button" style="margin-left: 10px" @click="loadTools">重试</button>
  </div>

  <div class="grid-2">
    <!-- 左：工具列表 -->
    <div class="card card-pad">
      <div class="row" style="margin-bottom: 10px">
        <h2 class="card-title" style="margin: 0">工具列表</h2>
        <span class="badge info">{{ tools.length }}</span>
        <span class="spacer"></span>
        <button class="btn btn-sm" type="button" :disabled="loadingTools" @click="loadTools">
          {{ loadingTools ? '刷新中…' : '刷新' }}
        </button>
      </div>

      <input v-model="keyword" class="search-box" type="search" placeholder="搜索工具名称或说明…" />

      <div class="tool-list">
        <button
          v-for="tool in filteredTools"
          :key="tool.name"
          type="button"
          class="tool-item"
          :class="{ active: tool.name === selectedName }"
          @click="selectTool(tool)"
        >
          <div class="tool-item-name">{{ tool.name }}</div>
          <div class="tool-item-title">{{ tool.title }}</div>
          <div class="tool-item-desc">{{ tool.description }}</div>
        </button>

        <div v-if="filteredTools.length === 0" class="empty">
          {{ tools.length === 0 ? '还没有加载到工具。' : '没有匹配的工具。' }}
        </div>
      </div>
    </div>

    <!-- 右：调用面板 -->
    <div class="card card-pad">
      <template v-if="selectedTool">
        <div class="row" style="align-items: baseline">
          <h2 class="card-title" style="margin: 0">{{ selectedTool.title }}</h2>
          <span class="mono faint">{{ selectedTool.name }}</span>
        </div>
        <p class="muted" style="margin: 6px 0 18px">{{ selectedTool.description }}</p>

        <!-- 参数表单 -->
        <SchemaForm v-model="formValues" :schema="selectedTool.inputSchema" />

        <div class="row" style="margin-top: 16px">
          <button class="btn btn-primary" type="button" :disabled="invoking" @click="invoke">
            {{ invoking ? '调用中…' : '调用工具' }}
          </button>
          <button class="btn" type="button" :disabled="invoking" @click="resetForm">重置参数</button>
        </div>

        <div v-if="invokeError" class="alert error" style="margin-top: 16px">{{ invokeError }}</div>

        <!-- 结果 -->
        <div v-if="result" class="result-box">
          <div class="result-head">
            <span class="badge" :class="result.isError ? 'err' : 'ok'">
              {{ result.isError ? '调用失败' : '调用成功' }}
            </span>
            <span class="faint">耗时 {{ result.elapsedMs }} ms</span>
            <span class="spacer"></span>
            <button class="btn btn-sm" type="button" @click="copyResult">
              {{ copied ? '已复制' : '复制结果' }}
            </button>
          </div>
          <pre class="result-body">{{ result.text }}</pre>
        </div>

        <!-- 结构化元数据 -->
        <details v-if="result && result.meta" style="margin-top: 12px">
          <summary class="faint" style="cursor: pointer">结构化元数据（structuredContent）</summary>
          <pre class="result-body" style="border: 1px solid var(--border); border-radius: 8px; margin-top: 8px">{{ JSON.stringify(result.meta, null, 2) }}</pre>
        </details>

        <!-- 协议视角：展示等价的 MCP 请求，便于理解 /mcp 端点 -->
        <details style="margin-top: 14px">
          <summary class="faint" style="cursor: pointer">查看等价的 MCP 请求（POST /mcp）</summary>
          <pre class="code" style="margin-top: 8px">{{ mcpPreview }}</pre>
        </details>
      </template>

      <div v-else class="empty">请在左侧选择一个工具。</div>
    </div>
  </div>
</template>

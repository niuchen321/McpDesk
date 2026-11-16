<script setup>
/**
 * 笔记管理页
 *
 * 笔记数据由后端的 NoteStore 统一管理（JSON 文件持久化），
 * 这里的增删改查与 MCP 工具 notes_* 共用同一份数据：
 * 在页面上改完，AI 下一次调用工具就能看到最新内容。
 */
import { computed, onMounted, ref } from 'vue'
import { api } from '../api'

// 列表与查询条件
const notes = ref([])
const keyword = ref('')
const tag = ref('')
const loading = ref(false)
const listError = ref('')

// 当前编辑的笔记；null 表示新建
const selectedId = ref(null)
const draft = ref({ title: '', content: '', tagsText: '' })

// 操作状态与提示
const saving = ref(false)
const actionError = ref('')
const notice = ref('')

/** 所有出现过的标签，用于快捷筛选 */
const allTags = computed(() => {
  const set = new Set()
  notes.value.forEach((note) => (note.tags ?? []).forEach((item) => set.add(item)))
  return [...set]
})

/** 是否处于新建状态 */
const isNew = computed(() => selectedId.value === null)

/** 把时间格式化为本地可读形式 */
function formatTime(value) {
  if (!value) return ''
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString('zh-CN', { hour12: false })
}

/** 把「标签字符串」解析成数组（支持中英文逗号） */
function parseTags(text) {
  return [...new Set(
    text
      .split(/[,，]/)
      .map((item) => item.trim())
      .filter(Boolean),
  )]
}

/** 加载笔记列表 */
async function loadNotes() {
  loading.value = true
  listError.value = ''

  try {
    const data = await api.getNotes({ keyword: keyword.value, tag: tag.value, limit: 100 })
    notes.value = data.notes ?? []
  } catch (error) {
    listError.value = error.message
  } finally {
    loading.value = false
  }
}

/** 选中一条笔记并填入编辑框 */
function selectNote(note) {
  selectedId.value = note.id
  draft.value = {
    title: note.title,
    content: note.content,
    tagsText: (note.tags ?? []).join(', '),
  }
  actionError.value = ''
  notice.value = ''
}

/** 切换到新建状态 */
function newNote() {
  selectedId.value = null
  draft.value = { title: '', content: '', tagsText: '' }
  actionError.value = ''
  notice.value = ''
}

/** 保存：新建走 POST，已有笔记走 PUT */
async function save() {
  saving.value = true
  actionError.value = ''
  notice.value = ''

  const payload = {
    title: draft.value.title,
    content: draft.value.content,
    tags: parseTags(draft.value.tagsText),
  }

  try {
    const saved = isNew.value
      ? await api.createNote(payload)
      : await api.updateNote(selectedId.value, payload)

    notice.value = isNew.value ? '笔记已创建。' : '笔记已保存。'
    selectedId.value = saved.id
    draft.value = {
      title: saved.title,
      content: saved.content,
      tagsText: (saved.tags ?? []).join(', '),
    }

    await loadNotes()
  } catch (error) {
    actionError.value = error.message
  } finally {
    saving.value = false
  }
}

/** 删除当前笔记（需要二次确认） */
async function remove() {
  if (selectedId.value === null) return

  const current = notes.value.find((note) => note.id === selectedId.value)
  const name = current?.title ?? '这条笔记'
  if (!window.confirm(`确定删除「${name}」吗？此操作不可撤销。`)) return

  actionError.value = ''
  notice.value = ''

  try {
    await api.deleteNote(selectedId.value)
    newNote()
    notice.value = '笔记已删除。'
    await loadNotes()
  } catch (error) {
    actionError.value = error.message
  }
}

/** 点击标签快捷筛选 */
function filterByTag(value) {
  tag.value = tag.value === value ? '' : value
  loadNotes()
}

onMounted(loadNotes)
</script>

<template>
  <h1 class="page-title">个人笔记</h1>
  <p class="page-desc">
    笔记同时以 MCP 资源（mcpdesk://notes/{id}）和工具（notes_list / notes_create / notes_update / notes_delete）的形式暴露给 AI。
  </p>

  <div v-if="listError" class="alert error">{{ listError }}</div>

  <div class="grid-2">
    <!-- 左：列表 -->
    <div class="card card-pad">
      <div class="row" style="margin-bottom: 10px">
        <h2 class="card-title" style="margin: 0">全部笔记</h2>
        <span class="badge info">{{ notes.length }}</span>
        <span class="spacer"></span>
        <button class="btn btn-sm btn-primary" type="button" @click="newNote">新建</button>
      </div>

      <div class="row" style="margin-bottom: 10px">
        <input
          v-model="keyword"
          type="search"
          placeholder="搜索标题、正文或标签…"
          style="flex: 1; min-width: 140px"
          @keyup.enter="loadNotes"
        />
        <button class="btn btn-sm" type="button" :disabled="loading" @click="loadNotes">
          {{ loading ? '…' : '搜索' }}
        </button>
      </div>

      <div v-if="allTags.length" class="row" style="margin-bottom: 10px; gap: 6px">
        <button
          v-for="item in allTags"
          :key="item"
          class="tag"
          type="button"
          :style="tag === item ? 'background:#4f5bd5;color:#fff;border:none;cursor:pointer' : 'border:none;cursor:pointer'"
          @click="filterByTag(item)"
        >
          {{ item }}
        </button>
      </div>

      <div class="note-list">
        <div
          v-for="note in notes"
          :key="note.id"
          class="note-item"
          :class="{ active: selectedId === note.id }"
          @click="selectNote(note)"
        >
          <div class="note-item-title">{{ note.title }}</div>
          <div class="note-item-preview">{{ note.content || '（空笔记）' }}</div>
          <div class="row" style="margin-top: 4px; gap: 4px">
            <span v-for="item in note.tags" :key="item" class="tag">{{ item }}</span>
            <span class="spacer"></span>
            <span class="faint">{{ formatTime(note.updatedAt) }}</span>
          </div>
        </div>

        <div v-if="notes.length === 0" class="empty">
          还没有笔记。点击「新建」写第一条，或让 AI 调用 notes_create 创建。
        </div>
      </div>
    </div>

    <!-- 右：编辑器 -->
    <div class="card card-pad">
      <div class="row" style="margin-bottom: 6px">
        <h2 class="card-title" style="margin: 0">{{ isNew ? '新建笔记' : '编辑笔记' }}</h2>
        <span v-if="selectedId" class="mono faint">ID: {{ selectedId }}</span>
        <span class="spacer"></span>
        <button class="btn btn-sm" type="button" @click="newNote">清空 / 新建</button>
      </div>

      <div v-if="actionError" class="alert error" style="margin-top: 10px">{{ actionError }}</div>
      <div v-if="notice" class="alert success" style="margin-top: 10px">{{ notice }}</div>

      <div class="field">
        <label for="note-title">标题</label>
        <input id="note-title" v-model="draft.title" type="text" maxlength="120" placeholder="留空则自动命名为「未命名笔记」" />
      </div>

      <div class="field">
        <label for="note-tags">标签</label>
        <input id="note-tags" v-model="draft.tagsText" type="text" placeholder="多个标签用逗号分隔，最多 10 个" />
        <div class="hint">标签会参与 AI 的检索（notes_list 的 tag 参数）。</div>
      </div>

      <div class="field">
        <label for="note-content">正文</label>
        <textarea id="note-content" v-model="draft.content" rows="16" placeholder="支持 Markdown 文本…"></textarea>
      </div>

      <div class="row">
        <button class="btn btn-primary" type="button" :disabled="saving" @click="save">
          {{ saving ? '保存中…' : (isNew ? '创建笔记' : '保存修改') }}
        </button>
        <button v-if="!isNew" class="btn btn-danger" type="button" :disabled="saving" @click="remove">
          删除
        </button>
        <span v-if="!isNew" class="faint">
          最后更新时间：{{ formatTime(notes.find((n) => n.id === selectedId)?.updatedAt) }}
        </span>
      </div>
    </div>
  </div>
</template>

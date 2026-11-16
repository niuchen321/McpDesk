<script setup>
/**
 * 根据 JSON Schema 自动生成参数表单
 *
 * 我们为每个工具手写了一份 inputSchema（见后端 JsonSchemaBuilder），
 * 前端读取它就能渲染出正确的控件类型、默认值、说明与校验范围。
 * 好处是：后端新增工具时，前端不需要改任何代码。
 *
 * 支持的字段类型：
 *   · string            → 文本框；带 enum 时变成下拉框
 *   · integer / number  → 数字输入框（带 min / max）
 *   · boolean           → 复选框
 *   · array（items 为 string）→ 逗号分隔的文本框
 */
import { computed } from 'vue'

const props = defineProps({
  /** 工具的 inputSchema */
  schema: { type: Object, required: true },
  /** 当前表单值，由父组件用 v-model 绑定 */
  modelValue: { type: Object, required: true },
})

const emit = defineEmits(['update:modelValue'])

/**
 * 把 schema.properties 转成便于模板遍历的数组。
 * required 是参数名数组，这里合并进每个字段，方便显示星号。
 */
const fieldList = computed(() => {
  const properties = props.schema?.properties ?? {}
  const required = props.schema?.required ?? []

  return Object.entries(properties).map(([name, definition]) => ({
    name,
    definition: definition ?? {},
    required: required.includes(name),
  }))
})

/** 读取当前值 */
function valueOf(name) {
  return props.modelValue?.[name]
}

/** 更新单个字段 */
function setValue(name, value) {
  emit('update:modelValue', { ...props.modelValue, [name]: value })
}

/** 文本 / 数字 / 下拉框的统一处理 */
function onInput(name, event, kind) {
  const raw = event.target.value

  if (raw === '') {
    // 空字符串表示「不传该参数」，交给后端使用默认值
    setValue(name, undefined)
    return
  }

  if (kind === 'number') {
    const num = Number(raw)
    setValue(name, Number.isNaN(num) ? undefined : num)
    return
  }

  setValue(name, raw)
}

/** 字符串数组：用逗号分隔显示，输入时再拆分 */
function arrayText(name) {
  const value = valueOf(name)
  return Array.isArray(value) ? value.join(', ') : ''
}

function onArrayInput(name, event) {
  const raw = event.target.value
  if (!raw.trim()) {
    setValue(name, undefined)
    return
  }

  // 支持中文逗号与英文逗号，自动去空白
  const items = raw
    .split(/[,，]/)
    .map((item) => item.trim())
    .filter(Boolean)

  setValue(name, items)
}

/** 布尔值 */
function onCheckbox(name, event) {
  setValue(name, event.target.checked)
}

/** 复选框的选中状态：未设置时按 schema 默认值处理 */
function isChecked(name, definition) {
  const value = valueOf(name)
  return value === undefined ? definition.default === true : value === true
}

/** 提示文本：把默认值、取值范围拼在一起 */
function hintOf(definition) {
  const parts = []

  if (definition.description) parts.push(definition.description)

  const extras = []
  if (definition.default !== undefined) extras.push(`默认 ${JSON.stringify(definition.default)}`)
  if (definition.minimum !== undefined) extras.push(`最小 ${definition.minimum}`)
  if (definition.maximum !== undefined) extras.push(`最大 ${definition.maximum}`)
  if (definition.maxLength !== undefined) extras.push(`最多 ${definition.maxLength} 字符`)

  return { description: parts.join(''), extras: extras.join(' · ') }
}
</script>

<template>
  <div>
    <div v-if="fieldList.length === 0" class="faint">该工具不需要任何参数，直接调用即可。</div>

    <div v-for="field in fieldList" :key="field.name" class="field">
      <label :for="`f-${field.name}`">
        <span class="mono">{{ field.name }}</span>
        <span v-if="field.required" class="req">*</span>
      </label>

      <!-- 枚举 → 下拉框 -->
      <select
        v-if="Array.isArray(field.definition.enum)"
        :id="`f-${field.name}`"
        :value="valueOf(field.name) ?? field.definition.default ?? ''"
        @change="setValue(field.name, $event.target.value === '' ? undefined : $event.target.value)"
      >
        <option value="">（使用默认值）</option>
        <option v-for="option in field.definition.enum" :key="option" :value="option">
          {{ option }}
        </option>
      </select>

      <!-- 布尔 → 复选框 -->
      <div v-else-if="field.definition.type === 'boolean'" class="check-row">
        <input
          :id="`f-${field.name}`"
          type="checkbox"
          :checked="isChecked(field.name, field.definition)"
          @change="onCheckbox(field.name, $event)"
        />
        <span class="faint">勾选表示 true</span>
      </div>

      <!-- 数字 → number 输入框 -->
      <input
        v-else-if="field.definition.type === 'integer' || field.definition.type === 'number'"
        :id="`f-${field.name}`"
        type="number"
        :value="valueOf(field.name) ?? ''"
        :min="field.definition.minimum"
        :max="field.definition.maximum"
        :step="field.definition.type === 'integer' ? 1 : 'any'"
        @input="onInput(field.name, $event, 'number')"
      />

      <!-- 字符串数组 → 逗号分隔输入 -->
      <input
        v-else-if="field.definition.type === 'array'"
        :id="`f-${field.name}`"
        type="text"
        :value="arrayText(field.name)"
        placeholder="多个值用逗号分隔，例如：工作, 学习"
        @input="onArrayInput(field.name, $event)"
      />

      <!-- 长文本（schema 里没有 maxLength 限制的字符串且名称含 content/text） -->
      <textarea
        v-else-if="field.definition.maxLength > 1000"
        :id="`f-${field.name}`"
        :value="valueOf(field.name) ?? ''"
        rows="6"
        :maxlength="field.definition.maxLength"
        @input="onInput(field.name, $event, 'text')"
      ></textarea>

      <!-- 其余字符串 → 单行文本框 -->
      <input
        v-else
        :id="`f-${field.name}`"
        type="text"
        :value="valueOf(field.name) ?? ''"
        :maxlength="field.definition.maxLength"
        :placeholder="field.definition.default !== undefined ? String(field.definition.default) : ''"
        @input="onInput(field.name, $event, 'text')"
      />

      <div class="hint">
        {{ hintOf(field.definition).description }}
        <span v-if="hintOf(field.definition).extras" class="faint">
          （{{ hintOf(field.definition).extras }}）
        </span>
      </div>
    </div>
  </div>
</template>

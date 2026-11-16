/**
 * 后端接口封装
 *
 * 所有请求都使用相对路径：
 *   · 开发时由 Vite 代理转发到 http://localhost:5080；
 *   · 生产时前端文件由同一个 ASP.NET Core 进程托管，天然同源。
 *
 * 这样前端代码不需要区分环境，也不需要处理跨域。
 */

/**
 * 统一的请求函数：发送请求、解析 JSON、把错误整理成可以直接展示的中文消息。
 */
async function request(path, { method = 'GET', body } = {}) {
  const options = {
    method,
    headers: {},
  }

  if (body !== undefined) {
    options.headers['Content-Type'] = 'application/json; charset=utf-8'
    options.body = JSON.stringify(body)
  }

  let response
  try {
    response = await fetch(path, options)
  } catch {
    throw new Error('无法连接后端服务，请确认 McpDesk.Api 已启动（默认 http://localhost:5080）。')
  }

  // 204 / 空响应直接返回 null
  const text = await response.text()
  let data = null
  if (text) {
    try {
      data = JSON.parse(text)
    } catch {
      data = text
    }
  }

  if (!response.ok) {
    throw new Error(extractErrorMessage(data, response.status))
  }

  return data
}

/**
 * 从后端的各种错误响应中提取一句可读的中文提示。
 * 兼容三种格式：
 *   · { error: "..." }                      —— 我们自己定义的接口
 *   · { errors: { 字段: ["..."] } }          —— ASP.NET Core 模型验证
 *   · JSON-RPC 的 { error: { message: "" } }
 */
function extractErrorMessage(data, status) {
  if (data && typeof data === 'object') {
    if (typeof data.error === 'string') return data.error
    if (data.error && typeof data.error.message === 'string') return data.error.message
    if (data.errors && typeof data.errors === 'object') {
      const first = Object.values(data.errors)[0]
      if (Array.isArray(first) && first.length > 0) return first[0]
    }
    if (typeof data.title === 'string' && data.title) return data.title
  }

  if (typeof data === 'string' && data.trim()) return data
  return `请求失败（HTTP ${status}）`
}

/** 把查询参数对象拼成 ?a=1&b=2，忽略空值。 */
function toQuery(params = {}) {
  const search = new URLSearchParams()
  Object.entries(params).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== '') {
      search.set(key, value)
    }
  })
  const text = search.toString()
  return text ? `?${text}` : ''
}

export const api = {
  // --------------------------- 系统 ---------------------------
  getSystem: () => request('/api/system'),

  // --------------------------- 工具 ---------------------------
  getTools: () => request('/api/tools'),

  /** 调用工具：arguments 就是工具的输入对象 */
  invokeTool: (name, args) =>
    request(`/api/tools/${encodeURIComponent(name)}/invoke`, {
      method: 'POST',
      body: args ?? {},
    }),

  // --------------------------- 笔记 ---------------------------
  getNotes: (params) => request(`/api/notes${toQuery(params)}`),

  createNote: (note) => request('/api/notes', { method: 'POST', body: note }),

  updateNote: (id, note) =>
    request(`/api/notes/${encodeURIComponent(id)}`, { method: 'PUT', body: note }),

  deleteNote: (id) =>
    request(`/api/notes/${encodeURIComponent(id)}`, { method: 'DELETE' }),

  // --------------------------- 日志 ---------------------------
  getLogs: (limit = 50) => request(`/api/logs${toQuery({ limit })}`),

  clearLogs: () => request('/api/logs', { method: 'DELETE' }),
}

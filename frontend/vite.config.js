import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

/**
 * Vite 配置
 *
 * 开发模式下前端跑在 5173 端口，后端 ASP.NET Core 跑在 5080 端口。
 * 通过 proxy 把 /api 与 /mcp 转发到后端，避免浏览器跨域问题，
 * 同时让前端的请求路径与生产环境（同源）保持完全一致。
 */
export default defineConfig({
  plugins: [vue()],

  server: {
    port: 5173,
    strictPort: true,

    // 反向代理：开发时把接口请求转给后端
    proxy: {
      // 管理后台的 REST 接口
      '/api': {
        target: 'http://localhost:5080',
        changeOrigin: true,
      },
      // MCP 协议端点（管理界面里的「协议测试」也会用到）
      '/mcp': {
        target: 'http://localhost:5080',
        changeOrigin: true,
      },
      // 健康检查
      '/health': {
        target: 'http://localhost:5080',
        changeOrigin: true,
      },
    },
  },

  build: {
    // 构建产物直接输出到后端的 wwwroot，
    // 这样 `dotnet run` 就能用同一个进程同时提供页面与接口。
    outDir: '../backend/McpDesk.Api/wwwroot',
    emptyOutDir: true,
  },
})

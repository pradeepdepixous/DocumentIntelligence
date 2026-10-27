import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vite'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5174,
    proxy: {
      // Trailing slash matters: a bare '/api' prefix-matches the client route '/api-access' too,
      // silently proxying that page request to the backend (which 404s) instead of letting React
      // Router handle it.
      '/api/': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
      '/swagger': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
})

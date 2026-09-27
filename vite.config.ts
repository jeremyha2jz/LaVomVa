import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  test: {
    include: ['src/**/*.test.{js,jsx,ts,tsx}'],
    coverage: { provider: 'v8', include: ['src/**/*.{ts,tsx}'], exclude: ['src/**/*.test.{ts,tsx}', 'src/vite-env.d.ts'], reporter: ['text', 'lcov'] },
  },
  server: {
    host: '127.0.0.1',
    port: 5173,
    proxy: { '/api': { target: 'http://localhost:5007', changeOrigin: true } },
  },
})

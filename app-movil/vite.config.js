// vite.config.js
import { defineConfig } from 'vite';

export default defineConfig({
  server: {
    allowedHosts: ['.ngrok-free.dev', '.ngrok-free.app'],
    proxy: {
      '/api': {
        target: 'http://localhost:5007',
        changeOrigin: true
      }
    }
  }
});

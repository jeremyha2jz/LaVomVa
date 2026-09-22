// vite.config.js
import { defineConfig } from 'vite';

export default defineConfig({
  server: {
    allowedHosts: ['.ngrok-free.dev', '.ngrok-free.app']
  }
});
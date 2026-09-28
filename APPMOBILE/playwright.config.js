import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.spec.js',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 90_000,
  globalTimeout: 12 * 60_000,
  reporter: [['list'], ['json', { outputFile: 'test-results/e2e-results.json' }]],
  outputDir: 'test-results/playwright',
  use: {
    baseURL: process.env.E2E_PWA_URL || 'http://127.0.0.1:5174',
    viewport: { width: 390, height: 844 },
    trace: 'off',
    screenshot: 'only-on-failure',
    video: 'off',
  },
});

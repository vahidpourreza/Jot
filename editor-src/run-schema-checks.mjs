import { build } from 'esbuild';
import { createRequire } from 'node:module';
import { resolve } from 'node:path';

// The optional first argument selects an existing Playwright installation.
// This harness needs no app process, network, database, or user note files.
const require = createRequire(import.meta.url);
const { chromium } = require(process.argv[2] || 'playwright');
const bundle = await build({ entryPoints: ['editor-src/schema-checks.ts'], bundle: true, write: false, format: 'iife', globalName: 'JotSchemaChecks', target: 'chrome130' });
const browser = await chromium.launch({ channel: 'msedge', headless: true });
try {
  const page = await browser.newPage();
  await page.setContent('<!doctype html><html><body></body></html>');
  await page.addScriptTag({ path: resolve('bidi.js') });
  await page.addScriptTag({ content: bundle.outputFiles[0].text });
  const results = await page.evaluate(() => window.JotSchemaChecks.runSchemaChecks());
  for (const result of results) console.log(`${result.passed ? 'PASS' : 'FAIL'} ${result.name}${result.detail ? `: ${result.detail}` : ''}`);
  console.log(`${results.filter(result => result.passed).length}/${results.length} schema checks passed`);
  if (results.some(result => !result.passed)) process.exitCode = 1;
} finally { await browser.close(); }

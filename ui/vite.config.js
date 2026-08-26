import { defineConfig } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'
import { resolve } from 'path'
import { readFileSync } from 'fs'
import { execSync } from 'child_process'

function readCsprojVersion() {
  const csproj = readFileSync(resolve(__dirname, '../src/Slate.csproj'), 'utf-8')
  return csproj.match(/<Version>(.*?)<\/Version>/)[1]
}

function readCommitHash() {
  try {
    const dirty = execSync('git status --porcelain', { cwd: __dirname }).toString().trim() ? '-dirty' : ''
    return execSync('git rev-parse --short HEAD', { cwd: __dirname }).toString().trim() + dirty
  } catch {
    return 'unknown'
  }
}

export default defineConfig({
  define: {
    __SLATE_VERSION__: JSON.stringify(readCsprojVersion()),
    __SLATE_COMMIT__: JSON.stringify(readCommitHash()),
  },
  plugins: [svelte()],
  build: {
    outDir: resolve(__dirname, '../src/Resources'),
    emptyOutDir: true,
    // No code splitting — single JS bundle for embedded use
    rollupOptions: {
      output: {
        entryFileNames: 'assets/slate.js',
        chunkFileNames: 'assets/slate-[name].js',
        assetFileNames: 'assets/slate[extname]',
      }
    }
  },
  // Dev server proxies messages to a local C# debug endpoint (future)
  server: {
    port: 5173,
  }
})

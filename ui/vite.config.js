import { defineConfig } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'
import { resolve } from 'path'

export default defineConfig({
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

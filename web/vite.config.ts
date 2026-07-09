import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // the app calls /api/...; in production Vercel rewrites it to Railway. The API has no /api prefix, so strip it.
    proxy: { '/api': { target: 'http://localhost:5276', rewrite: (path) => path.replace(/^\/api/, '') } },
  },
})

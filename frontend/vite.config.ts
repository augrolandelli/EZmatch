import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    // En desarrollo, /api se proxea a la API local (perfil http de EZmatchApi).
    proxy: {
      '/api': 'http://localhost:5219',
    },
  },
})

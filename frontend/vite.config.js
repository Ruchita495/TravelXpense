import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// During development, any request to /api is forwarded to the .NET API (so no CORS headaches).
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, proxy: { '/api': 'http://localhost:5000' } },
});

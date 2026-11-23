import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The API serves the built app from src/MKAsh.Api/wwwroot in production.
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, proxy: { "/api": "http://localhost:5080" } },
  build: { outDir: "../src/MKAsh.Api/wwwroot", emptyOutDir: true },
});

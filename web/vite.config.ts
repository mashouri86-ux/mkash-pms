import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The API serves the built app from src/MKAsh.Api/wwwroot in production.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    host: true,
    // Codespaces serves the app from https://<name>-5173.app.github.dev
    allowedHosts: [".app.github.dev", "localhost"],
    proxy: { "/api": "http://localhost:5080" },
  },
  build: { outDir: "../src/MKAsh.Api/wwwroot", emptyOutDir: true },
});

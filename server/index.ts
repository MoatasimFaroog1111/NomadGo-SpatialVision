import express from "express";
import path from "path";
import { fileURLToPath } from "url";
import pulseRouter    from "./routes/pulse.js";
import sessionsRouter from "./routes/sessions.js";

const app  = express();
const PORT = Number(process.env.PORT ?? 5000);

const __dirname = path.dirname(fileURLToPath(import.meta.url));

// ── Middleware ────────────────────────────────────────────────────────────────

app.use(express.json({ limit: "1mb" }));

// Basic request logger
app.use((req, _res, next) => {
  console.log(`[${new Date().toISOString()}] ${req.method} ${req.path}`);
  next();
});

// ── Routes ────────────────────────────────────────────────────────────────────

app.use("/api/pulse",    pulseRouter);
app.use("/api/sessions", sessionsRouter);

app.get("/health", (_req, res) => {
  res.json({ ok: true, ts: new Date().toISOString() });
});

// ── Static client in production ───────────────────────────────────────────────

if (process.env.NODE_ENV === "production") {
  const clientDist = path.join(__dirname, "public");
  app.use(express.static(clientDist));
  // SPA fallback
  app.get("*", (_req, res) => {
    res.sendFile(path.join(clientDist, "index.html"));
  });
} else {
  // 404 handler for dev (client served by Vite dev server)
  app.use((_req, res) => {
    res.status(404).json({ error: "Not found" });
  });
}

// Global error handler
app.use((err: Error, _req: express.Request, res: express.Response, _next: express.NextFunction) => {
  console.error("[server] Unhandled error:", err);
  res.status(500).json({ error: "Internal server error" });
});

// ── Start (only when not in test environment) ─────────────────────────────────

if (process.env.NODE_ENV !== "test") {
  app.listen(PORT, "0.0.0.0", () => {
    console.log(`[server] NomadGo backend running on port ${PORT}`);
  });
}

export default app;

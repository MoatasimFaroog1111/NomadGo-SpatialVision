import express from "express";
import pulseRouter    from "./routes/pulse.js";
import sessionsRouter from "./routes/sessions.js";

const app  = express();
const PORT = Number(process.env.PORT ?? 5000);

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

// 404 handler
app.use((_req, res) => {
  res.status(404).json({ error: "Not found" });
});

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

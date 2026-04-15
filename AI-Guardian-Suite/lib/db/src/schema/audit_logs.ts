import {
  pgTable,
  serial,
  integer,
  text,
  jsonb,
  timestamp,
  pgEnum,
} from "drizzle-orm/pg-core";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod/v4";

export const auditSeverityEnum = pgEnum("audit_severity", [
  "info",
  "warning",
  "error",
  "critical",
]);

export const auditLogsTable = pgTable("audit_logs", {
  id: serial("id").primaryKey(),
  documentId: integer("document_id"),
  transactionId: integer("transaction_id"),
  agentName: text("agent_name").notNull(),
  action: text("action").notNull(),
  details: jsonb("details"),
  severity: auditSeverityEnum("severity").notNull().default("info"),
  createdAt: timestamp("created_at").notNull().defaultNow(),
});

export const insertAuditLogSchema = createInsertSchema(auditLogsTable).omit({
  id: true,
  createdAt: true,
});
export type InsertAuditLog = z.infer<typeof insertAuditLogSchema>;
export type AuditLog = typeof auditLogsTable.$inferSelect;

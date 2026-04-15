import {
  pgTable,
  serial,
  text,
  boolean,
  integer,
  numeric,
  jsonb,
  timestamp,
  pgEnum,
} from "drizzle-orm/pg-core";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod/v4";

export const fileTypeEnum = pgEnum("file_type", [
  "pdf",
  "image",
  "email",
  "whatsapp",
  "csv",
  "other",
]);
export const sourceEnum = pgEnum("source", [
  "upload",
  "email",
  "whatsapp",
  "api",
]);
export const documentStatusEnum = pgEnum("document_status", [
  "pending",
  "preprocessing",
  "extracting",
  "classifying",
  "validating",
  "awaiting_approval",
  "approved",
  "rejected",
  "posted",
  "failed",
]);
export const classificationLabelEnum = pgEnum("classification_label", [
  "invoice",
  "receipt",
  "expense",
  "bank_statement",
  "credit_note",
  "other",
]);

export const documentsTable = pgTable("documents", {
  id: serial("id").primaryKey(),
  fileName: text("file_name").notNull(),
  fileType: fileTypeEnum("file_type").notNull().default("other"),
  source: sourceEnum("source").notNull().default("upload"),
  status: documentStatusEnum("status").notNull().default("pending"),
  fileHash: text("file_hash"),
  ocrFingerprint: text("ocr_fingerprint"),
  isDuplicate: boolean("is_duplicate").notNull().default(false),
  duplicateOfId: integer("duplicate_of_id"),
  extractedData: jsonb("extracted_data"),
  classificationLabel: classificationLabelEnum("classification_label"),
  classificationConfidence: numeric("classification_confidence", {
    precision: 5,
    scale: 4,
  }),
  validationPassed: boolean("validation_passed"),
  validationErrors: jsonb("validation_errors"),
  requiresHumanApproval: boolean("requires_human_approval")
    .notNull()
    .default(false),
  odooEntryId: text("odoo_entry_id"),
  rawContent: text("raw_content"),
  createdAt: timestamp("created_at").notNull().defaultNow(),
  updatedAt: timestamp("updated_at").notNull().defaultNow(),
});

export const insertDocumentSchema = createInsertSchema(documentsTable).omit({
  id: true,
  createdAt: true,
  updatedAt: true,
});
export type InsertDocument = z.infer<typeof insertDocumentSchema>;
export type Document = typeof documentsTable.$inferSelect;

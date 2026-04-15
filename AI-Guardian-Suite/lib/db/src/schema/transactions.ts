import {
  pgTable,
  serial,
  integer,
  text,
  numeric,
  timestamp,
  pgEnum,
} from "drizzle-orm/pg-core";
import { createInsertSchema } from "drizzle-zod";
import { z } from "zod/v4";
import { documentsTable } from "./documents";

export const transactionTypeEnum = pgEnum("transaction_type", [
  "invoice",
  "receipt",
  "expense",
  "bank_statement",
  "credit_note",
  "other",
]);
export const transactionStatusEnum = pgEnum("transaction_status", [
  "draft",
  "validated",
  "posted",
  "reconciled",
  "cancelled",
]);

export const transactionsTable = pgTable("transactions", {
  id: serial("id").primaryKey(),
  documentId: integer("document_id").references(() => documentsTable.id),
  type: transactionTypeEnum("type").notNull().default("other"),
  status: transactionStatusEnum("status").notNull().default("draft"),
  supplier: text("supplier"),
  invoiceNumber: text("invoice_number"),
  invoiceDate: text("invoice_date"),
  currency: text("currency").notNull().default("USD"),
  totalAmount: numeric("total_amount", { precision: 15, scale: 2 })
    .notNull()
    .default("0"),
  taxAmount: numeric("tax_amount", { precision: 15, scale: 2 }),
  odooEntryId: text("odoo_entry_id"),
  createdAt: timestamp("created_at").notNull().defaultNow(),
  updatedAt: timestamp("updated_at").notNull().defaultNow(),
});

export const insertTransactionSchema = createInsertSchema(
  transactionsTable,
).omit({
  id: true,
  createdAt: true,
  updatedAt: true,
});
export type InsertTransaction = z.infer<typeof insertTransactionSchema>;
export type Transaction = typeof transactionsTable.$inferSelect;

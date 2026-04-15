/**
 * Supplier Memory Table
 *
 * The AI accountant's long-term memory. Every time a document is processed
 * and posted, the system learns: which account, which journal, VAT rate,
 * and partner ID were used for this supplier. Future documents from the same
 * supplier reuse this knowledge instantly — no AI re-learning needed.
 */
import {
  pgTable,
  serial,
  text,
  integer,
  numeric,
  jsonb,
  timestamp,
  boolean,
} from "drizzle-orm/pg-core";

export const supplierMemoryTable = pgTable("supplier_memory", {
  id: serial("id").primaryKey(),

  // Supplier identification (lookup key = normalized lowercase)
  supplierKey: text("supplier_key").notNull().unique(), // normalized for matching
  supplierName: text("supplier_name").notNull(), // canonical display name
  supplierNameAr: text("supplier_name_ar"), // Arabic name
  vatNumber: text("vat_number"), // VAT/CR number

  // Odoo partner data
  partnerId: integer("partner_id"), // Odoo partner ID
  partnerName: text("partner_name"), // Odoo partner display name
  matchType: text("match_type"), // how partner was matched

  // Accounting mappings (learned)
  accountCode: text("account_code"), // expense account code
  accountName: text("account_name"), // expense account label
  journalId: integer("journal_id"), // Odoo journal ID
  journalName: text("journal_name"), // journal display name
  taxRate: numeric("tax_rate", { precision: 5, scale: 2 }), // VAT rate (e.g. 15.00)
  currency: text("currency").default("SAR"),

  // Statistical learning
  invoiceCount: integer("invoice_count").notNull().default(1),
  totalAmountSum: numeric("total_amount_sum", {
    precision: 20,
    scale: 4,
  }).default("0"),
  averageAmount: numeric("average_amount", { precision: 20, scale: 4 }),
  lastInvoiceDate: text("last_invoice_date"),
  lastDocumentId: integer("last_document_id"),

  // AI reasoning and corrections
  lastAiReasoning: text("last_ai_reasoning"),
  userCorrections: jsonb("user_corrections"), // manual overrides
  isVerified: boolean("is_verified").notNull().default(false), // human-confirmed

  // Timestamps
  createdAt: timestamp("created_at").notNull().defaultNow(),
  updatedAt: timestamp("updated_at").notNull().defaultNow(),
});

export type SupplierMemory = typeof supplierMemoryTable.$inferSelect;
export type InsertSupplierMemory = typeof supplierMemoryTable.$inferInsert;

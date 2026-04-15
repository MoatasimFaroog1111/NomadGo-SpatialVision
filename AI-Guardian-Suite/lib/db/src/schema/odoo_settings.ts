import {
  pgTable,
  serial,
  text,
  varchar,
  integer,
  numeric,
  boolean,
  timestamp,
} from "drizzle-orm/pg-core";

export const odooSettingsTable = pgTable("odoo_settings", {
  id: serial("id").primaryKey(),

  // ── Connection ────────────────────────────────────────────────────
  odooUrl: text("odoo_url").notNull().default(""),
  odooDb: varchar("odoo_db", { length: 200 }).notNull().default(""),
  odooUsername: varchar("odoo_username", { length: 200 }).notNull().default(""),
  odooApiKey: text("odoo_api_key").notNull().default(""),

  // ── Company identity ──────────────────────────────────────────────
  companyName: varchar("company_name", { length: 300 })
    .notNull()
    .default("GITC INTERNATIONAL HOLDING CO."),
  companyId: integer("company_id").default(1),

  // ── Tax & Accounting ──────────────────────────────────────────────
  defaultCurrency: varchar("default_currency", { length: 10 })
    .notNull()
    .default("SAR"),
  defaultVatPercent: numeric("default_vat_percent", { precision: 5, scale: 2 })
    .notNull()
    .default("15.00"),
  purchaseJournalId: integer("purchase_journal_id").default(9),
  bankJournalId: integer("bank_journal_id").default(13),
  payableAccountCode: varchar("payable_account_code", { length: 50 }).default(
    "2110",
  ),
  taxAccountCode: varchar("tax_account_code", { length: 50 }).default("2410"),
  defaultExpenseAccCode: varchar("default_expense_acc_code", {
    length: 50,
  }).default("5010"),

  // ── ZATCA / Compliance ────────────────────────────────────────────
  vatRegistrationNumber: varchar("vat_registration_number", {
    length: 50,
  }).default(""),
  crNumber: varchar("cr_number", { length: 50 }).default(""),
  zatcaEnabled: boolean("zatca_enabled").notNull().default(true),

  // ── Operational ───────────────────────────────────────────────────
  autoPostThreshold: numeric("auto_post_threshold", { precision: 4, scale: 2 })
    .notNull()
    .default("0.85"),
  requireDualApproval: boolean("require_dual_approval")
    .notNull()
    .default(false),
  maxInvoiceAmount: numeric("max_invoice_amount", {
    precision: 18,
    scale: 2,
  }).default("50000"),

  updatedAt: timestamp("updated_at").defaultNow().notNull(),
});

export type OdooSettings = typeof odooSettingsTable.$inferSelect;
export type InsertOdooSettings = typeof odooSettingsTable.$inferInsert;

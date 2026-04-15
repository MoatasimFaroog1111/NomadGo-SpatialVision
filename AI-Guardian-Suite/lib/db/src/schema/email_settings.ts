import {
  pgTable,
  serial,
  text,
  varchar,
  boolean,
  integer,
  timestamp,
} from "drizzle-orm/pg-core";

export const emailSettingsTable = pgTable("email_settings", {
  id: serial("id").primaryKey(),
  enabled: boolean("enabled").default(false).notNull(),
  imapHost: varchar("imap_host", { length: 300 }).default("").notNull(),
  imapPort: integer("imap_port").default(993).notNull(),
  imapSsl: boolean("imap_ssl").default(true).notNull(),
  imapUsername: varchar("imap_username", { length: 300 }).default("").notNull(),
  imapPassword: text("imap_password").default("").notNull(),
  imapMailbox: varchar("imap_mailbox", { length: 200 })
    .default("INBOX")
    .notNull(),
  pollIntervalSeconds: integer("poll_interval_seconds").default(300).notNull(),
  autoPostMaxAmount: integer("auto_post_max_amount").default(10000).notNull(),
  markAsRead: boolean("mark_as_read").default(true).notNull(),
  moveProcessedTo: varchar("move_processed_to", { length: 200 })
    .default("")
    .notNull(),
  lastPolledAt: timestamp("last_polled_at"),
  totalEmailsProcessed: integer("total_emails_processed").default(0).notNull(),
  totalAutoPosted: integer("total_auto_posted").default(0).notNull(),
  totalPendingApproval: integer("total_pending_approval").default(0).notNull(),
  updatedAt: timestamp("updated_at").defaultNow(),
});

export type EmailSettings = typeof emailSettingsTable.$inferSelect;
export type NewEmailSettings = typeof emailSettingsTable.$inferInsert;

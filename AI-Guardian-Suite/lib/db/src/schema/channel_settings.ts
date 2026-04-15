import {
  pgTable,
  serial,
  text,
  varchar,
  boolean,
  integer,
  timestamp,
} from "drizzle-orm/pg-core";

export const channelSettingsTable = pgTable("channel_settings", {
  id: serial("id").primaryKey(),
  // WhatsApp
  whatsappEnabled: boolean("whatsapp_enabled").default(false).notNull(),
  twilioAccountSid: varchar("twilio_account_sid", { length: 100 })
    .default("")
    .notNull(),
  twilioAuthToken: text("twilio_auth_token").default("").notNull(),
  twilioWhatsappNumber: varchar("twilio_whatsapp_number", { length: 30 })
    .default("")
    .notNull(),
  // SMS
  smsEnabled: boolean("sms_enabled").default(false).notNull(),
  twilioSmsNumber: varchar("twilio_sms_number", { length: 30 })
    .default("")
    .notNull(),
  // Shared limits
  autoPostMaxAmount: integer("auto_post_max_amount").default(10000).notNull(),
  // Stats
  totalWhatsappProcessed: integer("total_whatsapp_processed")
    .default(0)
    .notNull(),
  totalSmsProcessed: integer("total_sms_processed").default(0).notNull(),
  totalAutoPosted: integer("total_auto_posted").default(0).notNull(),
  totalPendingApproval: integer("total_pending_approval").default(0).notNull(),
  updatedAt: timestamp("updated_at").defaultNow(),
});

export type ChannelSettings = typeof channelSettingsTable.$inferSelect;
export type NewChannelSettings = typeof channelSettingsTable.$inferInsert;

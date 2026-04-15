import {
  pgTable,
  serial,
  varchar,
  text,
  timestamp,
  boolean,
} from "drizzle-orm/pg-core";

export const llmSettingsTable = pgTable("llm_settings", {
  id: serial("id").primaryKey(),
  activeProvider: varchar("active_provider", { length: 20 })
    .notNull()
    .default("openai"),
  openaiModel: varchar("openai_model", { length: 100 })
    .notNull()
    .default("gpt-5.4-mini"),
  anthropicFastModel: varchar("anthropic_fast_model", { length: 100 })
    .notNull()
    .default("claude-haiku-4-5"),
  anthropicSmartModel: varchar("anthropic_smart_model", { length: 100 })
    .notNull()
    .default("claude-opus-4-5"),
  customName: varchar("custom_name", { length: 100 }),
  customBaseUrl: text("custom_base_url"),
  customModel: varchar("custom_model", { length: 200 }),
  customApiKey: text("custom_api_key"),
  customEnabled: boolean("custom_enabled").notNull().default(false),
  updatedAt: timestamp("updated_at").defaultNow().notNull(),
});

export type LlmSettings = typeof llmSettingsTable.$inferSelect;
export type InsertLlmSettings = typeof llmSettingsTable.$inferInsert;

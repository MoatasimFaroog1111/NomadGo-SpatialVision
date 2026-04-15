import { Link, useLocation } from "wouter";
import {
  LayoutDashboard,
  FileText,
  CheckSquare,
  ArrowRightLeft,
  History,
  BarChart3,
  Upload,
  Brain,
  MessageSquareText,
  Settings,
  Cpu,
  Zap,
  Server,
  Bot,
  MessageCircle,
  Activity,
  ChevronRight,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { useQuery } from "@tanstack/react-query";
import gitcLogo from "@/assets/gitc-logo.png";
import { getApiUrl } from "@/lib/api";

const navGroups = [
  {
    label: "النظام",
    items: [
      { href: "/", label: "لوحة التحكم", icon: LayoutDashboard },
      {
        href: "/autonomous",
        label: "Autonomous OS",
        icon: Bot,
        badge: "AI",
        badgeColor: "cyan",
      },
      {
        href: "/channels",
        label: "قنوات التواصل",
        icon: MessageCircle,
        badge: "NEW",
        badgeColor: "violet",
      },
    ],
  },
  {
    label: "المعالجة",
    items: [
      { href: "/documents", label: "قائمة المستندات", icon: FileText },
      { href: "/upload", label: "رفع مستند", icon: Upload },
      { href: "/approvals", label: "الموافقات", icon: CheckSquare },
      { href: "/transactions", label: "المعاملات", icon: ArrowRightLeft },
    ],
  },
  {
    label: "التحليل",
    items: [
      { href: "/audit", label: "سجل المراجعة", icon: History },
      { href: "/chat", label: "محادثة AI", icon: MessageSquareText },
      { href: "/reports", label: "التقارير", icon: BarChart3 },
    ],
  },
  {
    label: "الذكاء الاصطناعي",
    items: [
      { href: "/memory", label: "ذاكرة AI", icon: Brain },
      { href: "/settings", label: "إعدادات AI", icon: Settings },
    ],
  },
];

const PROVIDER_ICONS = {
  openai: { Icon: Zap, label: "OpenAI GPT", color: "text-emerald-400" },
  anthropic: { Icon: Cpu, label: "Claude AI", color: "text-violet-400" },
  custom: { Icon: Server, label: "Custom LLM", color: "text-sky-400" },
} as const;

function LlmBadge() {
  const { data } = useQuery({
    queryKey: ["llmSettings"],
    queryFn: async () => {
      const res = await fetch(getApiUrl("settings/llm"));
      if (!res.ok) throw new Error("Failed");
      return res.json() as Promise<{
        activeProvider: string;
        customName: string | null;
      }>;
    },
    refetchInterval: 30_000,
    retry: 1,
  });

  const provider = (data?.activeProvider ??
    "openai") as keyof typeof PROVIDER_ICONS;
  const meta = PROVIDER_ICONS[provider] ?? PROVIDER_ICONS.openai;
  const { Icon } = meta;

  return (
    <Link href="/settings">
      <div className="flex items-center gap-1.5 px-2 py-1 rounded-md bg-white/5 border border-white/8 hover:border-cyan-500/30 transition-all cursor-pointer group">
        <Icon className={cn("w-3 h-3", meta.color)} />
        <span className={cn("text-[10px] font-medium", meta.color)}>
          {provider === "custom" ? (data?.customName ?? "Custom") : meta.label}
        </span>
      </div>
    </Link>
  );
}

function OdooStatus() {
  const { data, isLoading } = useQuery({
    queryKey: ["odooStatus"],
    queryFn: async () => {
      const res = await fetch(getApiUrl("odoo/status"));
      if (!res.ok) throw new Error("Failed");
      return res.json() as Promise<{
        connected: boolean;
        company: string;
        uid: number;
      }>;
    },
    refetchInterval: 60_000,
    retry: 1,
  });

  if (isLoading) {
    return (
      <div className="flex items-center gap-1.5">
        <div className="w-1.5 h-1.5 rounded-full bg-yellow-400 animate-pulse" />
        <span className="text-[10px] text-yellow-400">Connecting...</span>
      </div>
    );
  }

  if (data?.connected) {
    return (
      <div
        className="flex items-center gap-1.5"
        title={`${data.company} · UID ${data.uid}`}
      >
        <div className="w-1.5 h-1.5 rounded-full bg-emerald-400 relative pulse-dot" />
        <span className="text-[10px] text-emerald-400 font-medium">
          Odoo Live
        </span>
      </div>
    );
  }

  return (
    <div className="flex items-center gap-1.5">
      <div className="w-1.5 h-1.5 rounded-full bg-red-500" />
      <span className="text-[10px] text-red-400">Odoo Offline</span>
    </div>
  );
}

export function Layout({ children }: { children: React.ReactNode }) {
  const [location] = useLocation();

  return (
    <div className="flex min-h-[100dvh] w-full bg-background">
      {/* ── Sidebar ─────────────────────────────────────────────────── */}
      <aside className="w-[240px] hidden md:flex flex-col border-r border-white/[0.06] bg-sidebar relative overflow-hidden shrink-0">
        {/* Subtle background glow orbs */}
        <div className="absolute top-0 left-0 w-48 h-48 bg-cyan-500/5 rounded-full blur-3xl pointer-events-none" />
        <div className="absolute bottom-0 right-0 w-48 h-48 bg-violet-500/5 rounded-full blur-3xl pointer-events-none" />

        {/* Logo */}
        <div className="h-16 flex items-center px-4 border-b border-white/[0.05] gap-3 shrink-0 relative">
          <div className="relative">
            <div className="w-9 h-9 rounded-xl bg-gradient-to-br from-cyan-400/20 to-violet-500/20 flex items-center justify-center border border-cyan-400/20 overflow-hidden">
              <img
                src={gitcLogo}
                alt="GITC"
                className="w-7 h-7 object-contain"
              />
            </div>
            <div className="absolute -bottom-0.5 -right-0.5 w-2.5 h-2.5 bg-emerald-400 rounded-full border-2 border-sidebar" />
          </div>
          <div className="flex flex-col min-w-0">
            <span className="font-bold text-sm text-white tracking-tight leading-tight">
              GuardianAI
            </span>
            <span className="text-[9px] text-cyan-400/70 tracking-widest uppercase font-medium">
              GITC International
            </span>
          </div>
        </div>

        {/* Navigation */}
        <nav className="flex-1 overflow-y-auto py-4 px-2 space-y-5">
          {navGroups.map((group) => {
            const hasActive = group.items.some((item) =>
              item.href === "/"
                ? location === "/"
                : location.startsWith(item.href),
            );
            return (
              <div key={group.label}>
                <div className="px-3 mb-1.5 flex items-center gap-2">
                  <span className="text-[9px] font-bold tracking-widest uppercase text-white/25 select-none">
                    {group.label}
                  </span>
                  {hasActive ? (
                    <div className="h-px flex-1 bg-gradient-to-r from-cyan-500/30 to-transparent" />
                  ) : (
                    <div className="h-px flex-1 bg-white/5" />
                  )}
                </div>
                <div className="space-y-0.5">
                  {group.items.map((item) => {
                    const isActive =
                      item.href === "/"
                        ? location === "/"
                        : location.startsWith(item.href);
                    return (
                      <Link key={item.href} href={item.href}>
                        <div
                          className={cn(
                            "flex items-center gap-2.5 px-3 py-2 rounded-lg text-sm font-medium transition-all duration-200 cursor-pointer group relative",
                            isActive
                              ? "nav-item-active text-cyan-300"
                              : "text-white/50 hover:text-white/85 hover:bg-white/[0.04]",
                          )}
                        >
                          <item.icon
                            className={cn(
                              "w-4 h-4 shrink-0 transition-colors",
                              isActive
                                ? "text-cyan-400"
                                : "text-white/35 group-hover:text-white/60",
                            )}
                          />
                          <span className="truncate flex-1 text-[13px]">
                            {item.label}
                          </span>
                          {(item as { badge?: string }).badge ? (
                            <span
                              className={cn(
                                "text-[8px] font-bold px-1.5 py-0.5 rounded tracking-wider",
                                (item as { badgeColor?: string }).badgeColor ===
                                  "cyan"
                                  ? "bg-cyan-500/15 text-cyan-300 border border-cyan-500/25"
                                  : "bg-violet-500/15 text-violet-300 border border-violet-500/25",
                              )}
                            >
                              {(item as { badge?: string }).badge}
                            </span>
                          ) : null}
                          {isActive ? (
                            <ChevronRight className="w-3 h-3 text-cyan-400/60 shrink-0" />
                          ) : null}
                        </div>
                      </Link>
                    );
                  })}
                </div>
              </div>
            );
          })}
        </nav>

        {/* Footer */}
        <div className="shrink-0 p-3 border-t border-white/[0.05] space-y-3 relative">
          {/* Status row */}
          <div className="flex items-center justify-between px-1">
            <OdooStatus />
            <LlmBadge />
          </div>

          {/* User */}
          <div className="flex items-center gap-2.5 px-2 py-2 rounded-lg bg-white/[0.03] border border-white/[0.05]">
            <div className="w-7 h-7 rounded-lg bg-gradient-to-br from-cyan-500/30 to-violet-500/30 flex items-center justify-center text-[10px] font-bold text-cyan-300 border border-cyan-500/20 shrink-0">
              MN
            </div>
            <div className="flex flex-col min-w-0">
              <span className="text-xs font-semibold text-white/80 leading-tight">
                Motasim Noor
              </span>
              <span className="text-[10px] text-white/35 leading-tight">
                CFO / System Admin
              </span>
            </div>
            <Activity className="w-3 h-3 text-emerald-400 shrink-0 ml-auto" />
          </div>
        </div>
      </aside>

      {/* ── Main Content ─────────────────────────────────────────── */}
      <main className="flex-1 flex flex-col min-w-0 overflow-hidden">
        <div className="flex-1 overflow-y-auto">
          <div className="p-6 md:p-8">{children}</div>
        </div>
      </main>
    </div>
  );
}

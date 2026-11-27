import type { Metadata } from "next";
import { AdminProviders } from "./AdminProviders";

// Admin pages are thin shells around client components that call the API from the browser (PLAN.md Phase 7).
export const metadata: Metadata = {
  title: { default: "Admin", template: "%s · Admin · ArmoryWiki" },
  robots: { index: false, follow: false },
};

export default function AdminLayout({ children }: { children: React.ReactNode }) {
  return <AdminProviders>{children}</AdminProviders>;
}

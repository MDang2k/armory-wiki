"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import Link from "next/link";
import { useRouter } from "next/navigation";
import type { ReactNode } from "react";
import { Notice, buttonClass } from "@/components/admin/ui";
import { adminApi, forgetAntiforgeryToken } from "@/lib/admin/client";
import { ApiProblem, unwrap } from "@/lib/admin/problems";
import { RESOURCES } from "@/lib/admin/resources";
import { LOGIN_PATH } from "@/lib/admin/returnTo";

const NAV = [
  { href: "/admin/firearms", label: "Firearms" },
  ...Object.entries(RESOURCES).map(([route, spec]) => ({ href: `/admin/${route}`, label: spec.plural })),
  { href: "/admin/sources", label: "Sources" },
];

/**
 * Checks the session (`/me`) before showing an admin page; a 401 is sent to login by the query client. The API
 * still checks every request: this only decides what to render.
 */
export function SessionGate({ children }: { children: ReactNode }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const me = useQuery({
    queryKey: ["admin", "me"],
    queryFn: async () => unwrap(await adminApi.GET("/api/admin/auth/me")),
    staleTime: Infinity,
  });
  const logout = useMutation({
    mutationFn: async () => unwrap(await adminApi.POST("/api/admin/auth/logout")),
    onSettled: () => {
      forgetAntiforgeryToken();
      queryClient.clear();
      router.replace(LOGIN_PATH);
    },
  });

  if (me.isPending) {
    return <p className="text-muted">Checking sign-in…</p>;
  }

  if (me.isError) {
    return me.error instanceof ApiProblem && me.error.status === 401 ? (
      <p className="text-muted">Taking you to sign-in…</p>
    ) : (
      <Notice>Could not reach the API: {me.error.message}</Notice>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 border-b border-border pb-3 text-sm">
        <span className="font-semibold">Admin</span>
        <nav aria-label="Admin" className="flex flex-wrap gap-x-4 gap-y-1">
          {NAV.map((item) => (
            <Link key={item.href} href={item.href}>
              {item.label}
            </Link>
          ))}
        </nav>
        <span className="ml-auto text-muted">{me.data.email}</span>
        <button type="button" className={buttonClass} disabled={logout.isPending} onClick={() => logout.mutate()}>
          Sign out
        </button>
      </div>
      {children}
    </div>
  );
}

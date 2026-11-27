"use client";

import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useState, type ReactNode } from "react";
import { ApiProblem } from "@/lib/admin/problems";
import { LOGIN_PATH, loginHref } from "@/lib/admin/returnTo";

/** One query client for the admin. Any 401, from a read or a write, sends the admin to login and back. */
export function AdminProviders({ children }: { children: ReactNode }) {
  const router = useRouter();
  const [client] = useState(() => {
    const onError = (error: unknown) => {
      // No cache clearing here: that would refetch the failed query at once and loop. Login clears it.
      if (error instanceof ApiProblem && error.status === 401 && window.location.pathname !== LOGIN_PATH) {
        router.replace(loginHref(window.location.pathname + window.location.search));
      }
    };

    const client = new QueryClient({
      queryCache: new QueryCache({ onError }),
      mutationCache: new MutationCache({ onError }),
      defaultOptions: {
        // Client errors (4xx) won't change on retry; server and network errors get two more tries.
        queries: { retry: (count, error) => !(error instanceof ApiProblem && error.status < 500) && count < 2, refetchOnWindowFocus: false },
      },
    });
    return client;
  });

  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useRouter } from "next/navigation";
import { useState, type FormEvent } from "react";
import { Field, Notice, inputClass, primaryButtonClass } from "@/components/admin/ui";
import { adminApi, forgetAntiforgeryToken } from "@/lib/admin/client";
import { toProblem } from "@/lib/admin/problems";
import { safeReturnTo } from "@/lib/admin/returnTo";

export function LoginForm() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  async function signIn(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    setPending(true);
    setError(null);
    try {
      const { error: body, response } = await adminApi.POST("/api/admin/auth/login", {
        body: { email: String(form.get("email")), password: String(form.get("password")) },
      });
      if (!response.ok) {
        setError(response.status === 429 ? "Too many attempts. Wait a minute and try again." : (toProblem(response, body).detail ?? "Sign-in failed."));
        return;
      }

      forgetAntiforgeryToken(); // a token belongs to the session that fetched it
      queryClient.clear();
      // Read at submit time, so the page needs no useSearchParams (and no Suspense) to render.
      router.replace(safeReturnTo(new URLSearchParams(window.location.search).get("returnTo")));
    } catch {
      setError("Could not reach the API.");
    } finally {
      setPending(false);
    }
  }

  return (
    <form onSubmit={signIn} className="space-y-4">
      {error && <Notice>{error}</Notice>}
      <Field label="Email" htmlFor="email">
        <input id="email" name="email" type="email" autoComplete="username" required className={inputClass} />
      </Field>
      <Field label="Password" htmlFor="password">
        <input id="password" name="password" type="password" autoComplete="current-password" required className={inputClass} />
      </Field>
      <button type="submit" className={primaryButtonClass} disabled={pending}>
        {pending ? "Signing in…" : "Sign in"}
      </button>
    </form>
  );
}

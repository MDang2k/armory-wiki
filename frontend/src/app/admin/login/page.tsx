import type { Metadata } from "next";
import { LoginForm } from "./LoginForm";

export const metadata: Metadata = { title: "Sign in" };

export default function LoginPage() {
  return (
    <div className="mx-auto max-w-sm space-y-4 py-8">
      <h1 className="font-serif text-2xl">Admin sign-in</h1>
      <LoginForm />
    </div>
  );
}

import { SessionGate } from "./SessionGate";

/** Every admin page but login: shown only to a signed-in admin. */
export default function SignedInLayout({ children }: { children: React.ReactNode }) {
  return <SessionGate>{children}</SessionGate>;
}

export const LOGIN_PATH = "/admin/login";
const HOME = "/admin/firearms";

/** The login page's address that brings the admin back to `here` (a path plus query) afterwards. */
export function loginHref(here: string): string {
  return `${LOGIN_PATH}?returnTo=${encodeURIComponent(here)}`;
}

/**
 * Where to go after login: the `returnTo` it was given when that is an admin page on this site, else the firearm
 * list. Anything else (another site, `//host`, a backslash trick, the login page itself) is ignored, so the login
 * page can't be used to bounce someone elsewhere.
 */
export function safeReturnTo(returnTo: string | null): string {
  if (!returnTo || !/^\/admin(\/|\?|$)/.test(returnTo) || returnTo.includes("\\") || returnTo.startsWith(LOGIN_PATH)) {
    return HOME;
  }

  return returnTo;
}

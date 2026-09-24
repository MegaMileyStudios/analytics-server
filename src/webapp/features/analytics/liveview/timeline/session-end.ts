export const SESSION_SHUTDOWN_EVENT = "app_shutdown";
export const SESSION_INACTIVITY_TIMEOUT_MS = 60 * 60 * 1000;

export type SessionEndReason = "shutdown" | "inactive";

export function getSessionEndReason(
  eventNames: string[],
  lastEventAt: Date | undefined,
  now: number
): SessionEndReason | null {
  if (eventNames.includes(SESSION_SHUTDOWN_EVENT)) return "shutdown";
  if (lastEventAt && now - lastEventAt.getTime() > SESSION_INACTIVITY_TIMEOUT_MS) return "inactive";
  return null;
}

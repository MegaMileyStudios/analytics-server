import { atom } from "jotai";
import { atomWithLocalAndUrlPersistence, atomWithSearchParam, locationAtom } from "../../../atoms/location-atoms";
import type { SessionStartEndDateFilters } from "./SessionsGridDisplay";

export const SESSIONS_PAGE_SIZES = [10, 25, 50];

export const sessionsPageSizeAtom = atomWithLocalAndUrlPersistence("pageSize", "10");

export const sessionsUserIdAtom = atomWithSearchParam("userId");

// The pager cursor lives in the URL, so back/forward and returning from a session keep the page
const cursorParams = {
  endDate: "before",
  startDate: "after",
  sessionId: "cursor",
} as const;

export const sessionsCursorAtom = atom(
  (get) => {
    const searchParams = get(locationAtom).searchParams;
    const cursor: SessionStartEndDateFilters = {};
    for (const [key, param] of Object.entries(cursorParams)) {
      const value = searchParams?.get(param);
      if (value) cursor[key as keyof SessionStartEndDateFilters] = value;
    }
    return cursor;
  },
  (get, set, cursor: SessionStartEndDateFilters & { userId?: string }, options?: { replace?: boolean }) => {
    const nextSearchParams = new URLSearchParams(get(locationAtom).searchParams);
    for (const [key, param] of Object.entries(cursorParams)) {
      const value = cursor[key as keyof SessionStartEndDateFilters];
      if (value) nextSearchParams.set(param, value);
      else nextSearchParams.delete(param);
    }

    // Switching to (or away from) a single user's sessions starts from the first page, in the same history entry
    if ("userId" in cursor) {
      if (cursor.userId) nextSearchParams.set("userId", cursor.userId);
      else nextSearchParams.delete("userId");
    }

    set(locationAtom, (prev) => ({ ...prev, searchParams: nextSearchParams }), options);
  }
);

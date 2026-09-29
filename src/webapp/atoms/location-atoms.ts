import { atom } from "jotai";
import { atomWithLocation } from "jotai-location";

const routerListeners = new Set<() => void>();

// React Router navigations use pushState without a popstate event, so the router reports them here
export function notifyLocationChanged() {
  routerListeners.forEach((listener) => listener());
}

export const locationAtom = atomWithLocation({
  subscribe: (callback) => {
    routerListeners.add(callback);
    window.addEventListener("popstate", callback);
    return () => {
      routerListeners.delete(callback);
      window.removeEventListener("popstate", callback);
    };
  },
});

export function atomWithSearchParam(eventName: string) {
  return atom(
    (get) => get(locationAtom).searchParams?.get(eventName) ?? "",
    (get, set, name) => {
      const nextSearchParams = get(locationAtom).searchParams ?? new URLSearchParams();
      if (name) {
        nextSearchParams.set(eventName, name as string);
      } else {
        nextSearchParams.delete(eventName);
      }
      set(locationAtom, (prev) => ({
        ...prev,
        searchParams: nextSearchParams,
      }));
    }
  );
}

export function atomWithLocalAndUrlPersistence<T extends string | number | boolean>(key: string, initialValue: T) {
  const defaultValue = localStorage.hasOwnProperty(key) ? decodeURIComponent(localStorage[key]) : initialValue;
  const _local = atom(defaultValue);

  return atom(
    (get) => {
      const searchParamValue = get(locationAtom).searchParams?.get(key);
      if (searchParamValue) {
        return decodeURIComponent(searchParamValue);
      }

      const localValue = get(_local);
      if (typeof localValue === "string") {
        return decodeURIComponent(localValue);
      }
      return localValue;
    },
    (get, set, newValue: T) => {
      const storedValue = encodeURIComponent(newValue);
      localStorage[key] = storedValue;
      set(locationAtom, (prev) => {
        let newSearchParams = new URLSearchParams([[key, storedValue]]);
        if (prev.searchParams) {
          newSearchParams = prev.searchParams;
        }

        newSearchParams.set(key, storedValue);
        return {
          ...prev,
          searchParams: newSearchParams,
        };
      });
      set(_local, storedValue);
    }
  );
}

"use client";

import { useEffect } from "react";
import { reportClientError } from "@/lib/api/logs";

/** Capture les erreurs JS et promesses rejetées non gérées côté navigateur, les remonte à Omnia. */
export function ClientErrorReporter() {
  useEffect(() => {
    const onError = (event: ErrorEvent) => {
      reportClientError(event.message, event.filename, event.error?.stack);
    };
    const onRejection = (event: PromiseRejectionEvent) => {
      const reason = event.reason;
      reportClientError(
        reason instanceof Error ? reason.message : String(reason),
        "unhandledrejection",
        reason instanceof Error ? reason.stack : undefined,
      );
    };

    window.addEventListener("error", onError);
    window.addEventListener("unhandledrejection", onRejection);
    return () => {
      window.removeEventListener("error", onError);
      window.removeEventListener("unhandledrejection", onRejection);
    };
  }, []);

  return null;
}

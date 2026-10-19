import { ArticleStatus, articleStatusLabel, type ArticleStatusCode } from "@/lib/api/catalog";

const STYLES: Record<ArticleStatusCode, string> = {
  [ArticleStatus.PendingReview]: "bg-amber-100 text-amber-800 dark:bg-amber-900/40 dark:text-amber-300",
  [ArticleStatus.Published]: "bg-emerald-100 text-emerald-800 dark:bg-emerald-900/40 dark:text-emerald-300",
  [ArticleStatus.Rejected]: "bg-red-100 text-red-800 dark:bg-red-900/40 dark:text-red-300",
  [ArticleStatus.Sold]: "bg-zinc-200 text-zinc-700 dark:bg-zinc-800 dark:text-zinc-300",
};

export function StatusBadge({ status }: { status: ArticleStatusCode }) {
  return (
    <span className={`rounded-full px-2.5 py-1 text-xs font-medium ${STYLES[status]}`}>
      {articleStatusLabel(status)}
    </span>
  );
}

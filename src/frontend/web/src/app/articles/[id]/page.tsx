import { ArticleDetailClient } from "./ArticleDetailClient";

export default async function ArticleDetailPage({ params }: PageProps<"/articles/[id]">) {
  const { id } = await params;
  return <ArticleDetailClient id={id} />;
}

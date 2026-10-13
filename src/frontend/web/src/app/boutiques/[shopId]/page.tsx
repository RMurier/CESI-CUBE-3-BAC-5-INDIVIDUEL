import { ShopDetailClient } from "./ShopDetailClient";

export default async function ShopDetailPage({ params }: PageProps<"/boutiques/[shopId]">) {
  const { shopId } = await params;
  return <ShopDetailClient shopId={shopId} />;
}

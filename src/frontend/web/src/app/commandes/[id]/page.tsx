import { OrderDetailClient } from "./OrderDetailClient";

export default async function OrderDetailPage({ params }: PageProps<"/commandes/[id]">) {
  const { id } = await params;
  return <OrderDetailClient id={id} />;
}

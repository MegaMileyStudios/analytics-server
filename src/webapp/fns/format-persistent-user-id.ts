// Persistent user IDs are 64 characters long, so only show the start and end to keep them readable
export function formatPersistentUserId(id: string): string {
  if (id.length <= 11) return id;
  return `${id.slice(0, 4)}...${id.slice(-4)}`;
}

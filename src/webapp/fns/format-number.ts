import { FormatDurationOptions, formatDuration, intervalToDuration } from "date-fns";

const formatDistanceLocale: Record<string, string> = {
  xSeconds: "{{count}}s",
  xMinutes: "{{count}}m",
  xHours: "{{count}}h",
};

const durationFormat: FormatDurationOptions = {
  format: ["hours", "minutes", "seconds"],
  locale: {
    formatDistance: (token, count) => formatDistanceLocale[token].replace("{{count}}", count.toString()),
  },
};

function formatLargeNumber(num: number): string {
  if (num >= 1e6) {
    return (num / 1e6).toFixed(2) + "M";
  } else if (num >= 1e3) {
    return (num / 1e3).toFixed(1) + "k";
  }
  return num.toFixed(0);
}

export function formatBytes(bytes: number): string {
  const units = ["B", "KB", "MB", "GB", "TB"];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${value.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`;
}

export function formatNumber(value: number | undefined, format?: "number" | "duration"): string {
  if (format === "duration") {
    if (!value || value < 1) return "0s";

    const interval = intervalToDuration({
      start: 0,
      end: (value ?? 0) * 1000,
    });

    return formatDuration(interval, durationFormat);
  }

  return formatLargeNumber(value ?? 0);
}
